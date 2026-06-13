namespace SasJobRunner.Services;

/// <summary>
/// Service responsible for obtaining and refreshing Bearer tokens via service-account-based authentication.
/// </summary>
public interface ITokenManager
{
    /// <summary>
    /// Ensures a valid user token is present in session.
    /// If absent or expired, acquires a new token via login + impersonate flow.
    /// Returns the valid user token.
    /// </summary>
    Task<string> EnsureValidTokenAsync(CancellationToken ct = default);
}
