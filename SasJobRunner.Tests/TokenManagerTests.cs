using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using SasJobRunner.Services;
using Xunit;

namespace SasJobRunner.Tests;

/// <summary>
/// Test implementation of ISession for mocking session state.
/// </summary>
public class TestSession : ISession
{
    private readonly Dictionary<string, byte[]> _store = new();

    public bool IsAvailable => true;
    public string Id => "test-session-id";
    public IEnumerable<string> Keys => _store.Keys;

    public void Clear() => _store.Clear();

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Remove(string key) => _store.Remove(key);

    public void Set(string key, byte[] value) => _store[key] = value;

    public bool TryGetValue(string key, out byte[]? value) => _store.TryGetValue(key, out value);
}

public class TokenManagerTests
{
    [Fact]
    public async Task EnsureValidTokenAsync_WhenTokenIsValid_ReturnsExistingToken()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://test-hub.local")
        };

        var session = new TestSession();
        session.SetString("BearerToken", "existing-token");
        session.SetInt32("BearerTokenExpiresIn", 3600);
        session.SetString("BearerTokenAcquiredAt", DateTime.UtcNow.ToString("O"));

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(c => c.Session).Returns(session);

        var mockContextAccessor = new Mock<IHttpContextAccessor>();
        mockContextAccessor.Setup(a => a.HttpContext).Returns(mockHttpContext.Object);

        var mockConfiguration = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<TokenManager>>();

        var tokenManager = new TokenManager(
            httpClient,
            mockContextAccessor.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act
        var token = await tokenManager.EnsureValidTokenAsync();

        // Assert
        Assert.Equal("existing-token", token);
        mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task EnsureValidTokenAsync_WhenTokenIsAbsent_AcquiresNewToken()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        // Setup login response
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.PathAndQuery.Contains("/api/v2/auth/login")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { Token = "impersonate-token", ExpiresIn = 3600 })
            });

        // Setup impersonate response
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.PathAndQuery.Contains("/api/v2/auth/impersonate")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { Token = "user-token", ExpiresIn = 3600 })
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://test-hub.local")
        };

        var session = new TestSession();
        // Don't set any token - simulate absent token scenario
        // This will force token acquisition

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(c => c.Session).Returns(session);

        var mockContextAccessor = new Mock<IHttpContextAccessor>();
        mockContextAccessor.Setup(a => a.HttpContext).Returns(mockHttpContext.Object);

        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["SlcHub:ServiceAccount:Username"]).Returns("service-user");
        mockConfiguration.Setup(c => c["SlcHub:ServiceAccount:Password"]).Returns("service-pass");
        mockConfiguration.Setup(c => c["SlcHub:UserId"]).Returns("test-user");

        var mockLogger = new Mock<ILogger<TokenManager>>();

        var tokenManager = new TokenManager(
            httpClient,
            mockContextAccessor.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act
        var token = await tokenManager.EnsureValidTokenAsync();

        // Assert
        Assert.Equal("user-token", token);
        Assert.Equal("user-token", session.GetString("BearerToken"));
        Assert.Equal(3600, session.GetInt32("BearerTokenExpiresIn"));
        Assert.NotNull(session.GetString("BearerTokenAcquiredAt"));
    }

    [Fact]
    public async Task EnsureValidTokenAsync_WhenLoginFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.PathAndQuery.Contains("/api/v2/auth/login")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("Invalid credentials")
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://test-hub.local")
        };

        var session = new TestSession();

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(c => c.Session).Returns(session);

        var mockContextAccessor = new Mock<IHttpContextAccessor>();
        mockContextAccessor.Setup(a => a.HttpContext).Returns(mockHttpContext.Object);

        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["SlcHub:ServiceAccount:Username"]).Returns("service-user");
        mockConfiguration.Setup(c => c["SlcHub:ServiceAccount:Password"]).Returns("service-pass");

        var mockLogger = new Mock<ILogger<TokenManager>>();

        var tokenManager = new TokenManager(
            httpClient,
            mockContextAccessor.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await tokenManager.EnsureValidTokenAsync());
        
        Assert.Contains("Login failed", exception.Message);
    }

    [Fact]
    public async Task EnsureValidTokenAsync_WhenImpersonateFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        // Setup successful login response
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.PathAndQuery.Contains("/api/v2/auth/login")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(new { Token = "impersonate-token", ExpiresIn = 3600 })
            });

        // Setup failed impersonate response
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.PathAndQuery.Contains("/api/v2/auth/impersonate")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Forbidden,
                Content = new StringContent("User not authorized")
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://test-hub.local")
        };

        var session = new TestSession();

        var mockHttpContext = new Mock<HttpContext>();
        mockHttpContext.Setup(c => c.Session).Returns(session);

        var mockContextAccessor = new Mock<IHttpContextAccessor>();
        mockContextAccessor.Setup(a => a.HttpContext).Returns(mockHttpContext.Object);

        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["SlcHub:ServiceAccount:Username"]).Returns("service-user");
        mockConfiguration.Setup(c => c["SlcHub:ServiceAccount:Password"]).Returns("service-pass");
        mockConfiguration.Setup(c => c["SlcHub:UserId"]).Returns("test-user");

        var mockLogger = new Mock<ILogger<TokenManager>>();

        var tokenManager = new TokenManager(
            httpClient,
            mockContextAccessor.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await tokenManager.EnsureValidTokenAsync());
        
        Assert.Contains("Impersonate failed", exception.Message);
    }
}
