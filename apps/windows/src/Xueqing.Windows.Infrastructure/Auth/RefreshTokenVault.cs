using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Xueqing.Windows.Infrastructure.Auth;

public interface IRefreshTokenVault
{
    ValueTask<string?> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask StoreAsync(string refreshToken, CancellationToken cancellationToken = default);

    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public sealed class RefreshTokenVaultUnavailableException : InvalidOperationException
{
    public RefreshTokenVaultUnavailableException(string message)
        : base(message)
    {
    }

    public RefreshTokenVaultUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Stores exactly one provider refresh token for one deployment trust scope.
/// The durable bytes are protected with Windows DPAPI CurrentUser and never
/// share the database-key wrapping purpose.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiRefreshTokenVault : IRefreshTokenVault
{
    private static readonly byte[] EnvelopeHeader = "XQRT1"u8.ToArray();
    private const string EntropyPrefix = "xueqing-native/windows/refresh-token/v1/";

    private readonly string _tokenPath;
    private readonly byte[] _entropy;

    public WindowsDpapiRefreshTokenVault(
        string rootDirectory,
        string environmentId,
        string trustDomainId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(trustDomainId);

        var scopeMaterial = Encoding.UTF8.GetBytes(
            string.Concat(EntropyPrefix, environmentId, "\n", trustDomainId));
        byte[]? scopeHash = null;
        try
        {
            scopeHash = SHA256.HashData(scopeMaterial);
            var scopeName = Convert.ToHexString(scopeHash.AsSpan(0, 16)).ToLowerInvariant();
            _tokenPath = Path.Combine(
                Path.GetFullPath(rootDirectory),
                $"refresh-token-v1-{scopeName}.bin");
            _entropy = SHA256.HashData(scopeHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scopeMaterial);
            if (scopeHash is not null)
            {
                CryptographicOperations.ZeroMemory(scopeHash);
            }
        }
    }

    internal string TokenPath => _tokenPath;

    public async ValueTask<string?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_tokenPath))
        {
            return null;
        }

        var envelope = await File.ReadAllBytesAsync(_tokenPath, cancellationToken);
        byte[]? wrapped = null;
        byte[]? plaintext = null;
        try
        {
            if (envelope.Length <= EnvelopeHeader.Length ||
                !envelope.AsSpan(0, EnvelopeHeader.Length).SequenceEqual(EnvelopeHeader))
            {
                throw new RefreshTokenVaultUnavailableException(
                    "Refresh-token envelope has an invalid header.");
            }

            wrapped = envelope.AsSpan(EnvelopeHeader.Length).ToArray();
            try
            {
                plaintext = ProtectedData.Unprotect(
                    wrapped,
                    _entropy,
                    DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException error)
            {
                throw new RefreshTokenVaultUnavailableException(
                    "Refresh-token envelope could not be unprotected for the current Windows user/trust scope.",
                    error);
            }

            var token = Encoding.UTF8.GetString(plaintext);
            ValidateToken(token);
            return token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelope);
            if (wrapped is not null)
            {
                CryptographicOperations.ZeroMemory(wrapped);
            }

            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public async ValueTask StoreAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ValidateToken(refreshToken);

        var plaintext = Encoding.UTF8.GetBytes(refreshToken);
        byte[]? wrapped = null;
        byte[]? envelope = null;
        var temporaryPath = _tokenPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            wrapped = ProtectedData.Protect(
                plaintext,
                _entropy,
                DataProtectionScope.CurrentUser);
            envelope = new byte[EnvelopeHeader.Length + wrapped.Length];
            EnvelopeHeader.CopyTo(envelope, 0);
            wrapped.CopyTo(envelope, EnvelopeHeader.Length);

            var directory = Path.GetDirectoryName(_tokenPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (var stream = new FileStream(
                temporaryPath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                }))
            {
                await stream.WriteAsync(envelope, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _tokenPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new RefreshTokenVaultUnavailableException(
                "Refresh token could not be stored securely.",
                error);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (wrapped is not null)
            {
                CryptographicOperations.ZeroMemory(wrapped);
            }

            if (envelope is not null)
            {
                CryptographicOperations.ZeroMemory(envelope);
            }

            TryDelete(temporaryPath);
        }
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (File.Exists(_tokenPath))
            {
                File.Delete(_tokenPath);
            }

            if (File.Exists(_tokenPath))
            {
                throw new IOException("Refresh-token envelope still exists after deletion.");
            }

            return ValueTask.CompletedTask;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new RefreshTokenVaultUnavailableException(
                "Refresh token could not be cleared securely.",
                error);
        }
    }

    private static void ValidateToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Refresh token must not contain whitespace.",
                nameof(token));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
