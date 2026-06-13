using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace SasJobRunner.Tests;

/// <summary>
/// Tests for startup configuration validation (Requirement 1.2)
/// **Validates: Requirements 1.2**
/// </summary>
public class StartupConfigurationValidationTests
{
    [Theory]
    [InlineData("SlcHub:ServiceAccount:Username")]
    [InlineData("SlcHub:ServiceAccount:Password")]
    [InlineData("SlcHub:UserId")]
    [InlineData("SlcHub:BaseUrl")]
    [InlineData("SlcHub:Namespace")]
    [InlineData("SlcHub:ExecutionProfile")]
    public void Application_Startup_Should_Throw_InvalidOperationException_When_Required_Key_Is_Missing(string missingKey)
    {
        // Arrange
        var configuration = new Dictionary<string, string?>
        {
            ["SlcHub:ServiceAccount:Username"] = "service-user",
            ["SlcHub:ServiceAccount:Password"] = "service-pass",
            ["SlcHub:UserId"] = "test-user",
            ["SlcHub:BaseUrl"] = "https://test.example.com",
            ["SlcHub:Namespace"] = "default",
            ["SlcHub:ExecutionProfile"] = "default"
        };

        // Remove the key being tested
        configuration[missingKey] = null;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseConfiguration(new ConfigurationBuilder()
                        .AddInMemoryCollection(configuration!)
                        .Build());
                });

            // Trigger application build
            _ = factory.Server;
        });

        Assert.Contains(missingKey, exception.Message);
        Assert.Contains("missing or empty", exception.Message);
    }

    [Theory]
    [InlineData("SlcHub:ServiceAccount:Username")]
    [InlineData("SlcHub:ServiceAccount:Password")]
    [InlineData("SlcHub:UserId")]
    [InlineData("SlcHub:BaseUrl")]
    [InlineData("SlcHub:Namespace")]
    [InlineData("SlcHub:ExecutionProfile")]
    public void Application_Startup_Should_Throw_InvalidOperationException_When_Required_Key_Is_Empty(string emptyKey)
    {
        // Arrange
        var configuration = new Dictionary<string, string?>
        {
            ["SlcHub:ServiceAccount:Username"] = "service-user",
            ["SlcHub:ServiceAccount:Password"] = "service-pass",
            ["SlcHub:UserId"] = "test-user",
            ["SlcHub:BaseUrl"] = "https://test.example.com",
            ["SlcHub:Namespace"] = "default",
            ["SlcHub:ExecutionProfile"] = "default"
        };

        // Set the key to empty string
        configuration[emptyKey] = "";

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseConfiguration(new ConfigurationBuilder()
                        .AddInMemoryCollection(configuration!)
                        .Build());
                });

            // Trigger application build
            _ = factory.Server;
        });

        Assert.Contains(emptyKey, exception.Message);
        Assert.Contains("missing or empty", exception.Message);
    }

    [Fact]
    public void Application_Startup_Should_Succeed_When_All_Required_Keys_Are_Present()
    {
        // Arrange
        var configuration = new Dictionary<string, string?>
        {
            ["SlcHub:ServiceAccount:Username"] = "service-user",
            ["SlcHub:ServiceAccount:Password"] = "service-pass",
            ["SlcHub:UserId"] = "test-user",
            ["SlcHub:BaseUrl"] = "https://test.example.com",
            ["SlcHub:Namespace"] = "default",
            ["SlcHub:ExecutionProfile"] = "default"
        };

        // Act - should not throw
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseConfiguration(new ConfigurationBuilder()
                    .AddInMemoryCollection(configuration!)
                    .Build());
            });

        // Assert - access server to trigger build
        Assert.NotNull(factory.Server);
    }
}
