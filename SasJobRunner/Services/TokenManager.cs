using System.Net.Http.Headers;

namespace SasJobRunner.Services;

/// <summary>
/// Singleton service responsible for obtaining and refreshing Bearer tokens
/// via the service-account-based authentication flow.
/// Stores the impersonate token, user token, and expiry metadata in ASP.NET Session.
/// </summary>
public sealed class TokenManager(
    HttpClient httpClient,
    IHttpContextAccessor contextAccessor,
    IConfiguration configuration,
    ILogger<TokenManager> logger) : ITokenManager
{
    public async Task<string> EnsureValidTokenAsync(CancellationToken ct = default)
    {
        var session = contextAccessor.HttpContext!.Session;
        var userToken = session.GetString("BearerToken");
        var expiresIn = session.GetInt32("BearerTokenExpiresIn");
        var acquiredAt = session.GetString("BearerTokenAcquiredAt");

        if (userToken is not null && expiresIn is not null && acquiredAt is not null)
        {
            var acquired = DateTime.Parse(acquiredAt);
            var elapsed = (DateTime.UtcNow - acquired).TotalSeconds;
            if (elapsed < expiresIn.Value)
                return userToken;
        }

        // Token expired or absent — acquire new token
        var impersonateToken = await LoginAsync(ct);
        var userId = configuration["SlcHub:UserId"]!;
        userToken = await ImpersonateAsync(impersonateToken, userId, ct);

        session.SetString("BearerToken", userToken);
        session.SetInt32("BearerTokenExpiresIn", 3600); // assume 1 hour
        session.SetString("BearerTokenAcquiredAt", DateTime.UtcNow.ToString("O"));

        return userToken;
    }

    private async Task<string> LoginAsync(CancellationToken ct)
    {
        var username = configuration["SlcHub:ServiceAccount:Username"]!;
        var password = configuration["SlcHub:ServiceAccount:Password"]!;
        var response = await httpClient.PostAsJsonAsync(
            "/api/v2/auth/login",
            new { username, password },
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Login failed: {Body}", body);
            throw new InvalidOperationException($"Login failed: {body}");
        }
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return result!.Token;
    }

    private async Task<string> ImpersonateAsync(string impersonateToken, string userId, CancellationToken ct)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", impersonateToken);
        var response = await httpClient.PostAsJsonAsync(
            "/api/v2/auth/impersonate",
            new { userId },
            ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Impersonate failed: {Body}", body);
            throw new InvalidOperationException($"Impersonate failed: {body}");
        }
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return result!.Token;
    }

    private record TokenResponse(string Token, int ExpiresIn);
}
