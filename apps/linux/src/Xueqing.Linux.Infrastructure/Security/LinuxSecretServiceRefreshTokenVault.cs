using System.Runtime.Versioning;
using Xueqing.Windows.Infrastructure.Auth;

namespace Xueqing.Linux.Infrastructure.Security;

/// <summary>
/// Linux Secret Service implementation of IRefreshTokenVault, strictly separated
/// from the local SQLite database key schema/purpose.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretServiceRefreshTokenVault : IRefreshTokenVault
{
    private const string SchemaName = "com.xueqing.app.refresh-token.v1";
    private const string PurposeValue = "refresh-token";

    private readonly string _scopeHash;

    public LinuxSecretServiceRefreshTokenVault(
        string environmentId,
        string trustDomainId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(trustDomainId);

        _scopeHash = LinuxSecretServiceNative.ComputeScopeHash(
            string.Concat("xueqing-native/linux/refresh-token/v1/", environmentId, "\n", trustDomainId));
    }

    public ValueTask<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = LinuxSecretServiceNative.LookupUtf8Secret(
            SchemaName,
            PurposeValue,
            _scopeHash,
            static msg => new RefreshTokenVaultUnavailableException(msg));

        if (token is null)
        {
            return ValueTask.FromResult<string?>(null);
        }

        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsWhiteSpace))
        {
            throw new RefreshTokenVaultUnavailableException(
                "Linux Secret Service returned a malformed refresh token.");
        }

        return ValueTask.FromResult<string?>(token);
    }

    public ValueTask StoreAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        if (refreshToken.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Refresh token must not contain whitespace.", nameof(refreshToken));
        }

        LinuxSecretServiceNative.StoreUtf8Secret(
            SchemaName,
            PurposeValue,
            _scopeHash,
            $"Xueqing Session Token ({_scopeHash[..12]})",
            refreshToken,
            static msg => new RefreshTokenVaultUnavailableException(msg));
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LinuxSecretServiceNative.ClearSecret(
            SchemaName,
            PurposeValue,
            _scopeHash,
            static msg => new RefreshTokenVaultUnavailableException(msg));
        return ValueTask.CompletedTask;
    }
}
