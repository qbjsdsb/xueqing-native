using Xueqing.Windows.Infrastructure.Remote;

namespace Xueqing.Windows.Infrastructure.Auth;

public sealed record ProviderAuthTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt);

public enum ProviderRefreshDisposition
{
    Success,
    Rejected,
    RetryableFailure,
    ResultUnknown,
}

public sealed record ProviderRefreshResult(
    ProviderRefreshDisposition Disposition,
    ProviderAuthTokens? Tokens = null);

public interface IProviderAuthTransport
{
    Task<ProviderAuthTokens> SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<ProviderRefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task SignOutLocalAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}

public enum ProviderRefreshOutcome
{
    Usable,
    SignedOut,
    RefreshRequired,
    Invalid,
}

public enum ProviderSignOutOutcome
{
    LocalAndRemoteConfirmed,
    LocalOnlyRemoteUnconfirmed,
}

/// <summary>
/// Serializes provider session mutation and enforces refresh-token rotation
/// durability before a rotated access token can become usable.
/// </summary>
public sealed class ProviderAuthCoordinator
{
    private readonly ProviderSessionTokenSource _tokenSource;
    private readonly IRefreshTokenVault _refreshTokenVault;
    private readonly IProviderAuthTransport _transport;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public ProviderAuthCoordinator(
        ProviderSessionTokenSource tokenSource,
        IRefreshTokenVault refreshTokenVault,
        IProviderAuthTransport transport)
    {
        _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        _refreshTokenVault = refreshTokenVault ?? throw new ArgumentNullException(nameof(refreshTokenVault));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task SignInWithPasswordAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (_tokenSource.Snapshot.Status != ProviderSessionStatus.SignedOut)
            {
                throw new InvalidOperationException(
                    "Explicit sign-in requires a signed-out session. Account switching must close the previous local scope first.");
            }

            var tokens = await _transport.SignInWithPasswordAsync(
                email,
                password,
                cancellationToken);
            ValidateTokens(tokens);

            // Durability first: if the refresh token cannot be committed, the
            // new access token never becomes visible to application adapters.
            await _refreshTokenVault.StoreAsync(tokens.RefreshToken, cancellationToken);
            _tokenSource.Establish(tokens.AccessToken, tokens.AccessTokenExpiresAt);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ProviderRefreshOutcome> RestoreAsync(
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            if (_tokenSource.Snapshot.Status == ProviderSessionStatus.Usable)
            {
                return ProviderRefreshOutcome.Usable;
            }

            return await RefreshLockedAsync(cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ProviderRefreshOutcome> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await RefreshLockedAsync(cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ProviderSignOutOutcome> SignOutAsync(
        CancellationToken cancellationToken = default)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var accessToken = await _tokenSource.GetCurrentAccessTokenAsync(cancellationToken);
            var remoteConfirmed = accessToken is null;

            if (accessToken is not null)
            {
                try
                {
                    await _transport.SignOutLocalAsync(accessToken, cancellationToken);
                    remoteConfirmed = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Local logout must still complete. The caller receives an
                    // explicit unconfirmed result instead of silently claiming
                    // provider revocation.
                    remoteConfirmed = false;
                }
            }

            try
            {
                await _refreshTokenVault.ClearAsync(cancellationToken);
                _tokenSource.SignOut();
            }
            catch
            {
                _tokenSource.Invalidate();
                throw;
            }

            return remoteConfirmed
                ? ProviderSignOutOutcome.LocalAndRemoteConfirmed
                : ProviderSignOutOutcome.LocalOnlyRemoteUnconfirmed;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<ProviderRefreshOutcome> RefreshLockedAsync(
        CancellationToken cancellationToken)
    {
        var persistedRefreshToken = await _refreshTokenVault.LoadAsync(cancellationToken);
        if (persistedRefreshToken is null)
        {
            _tokenSource.SignOut();
            return ProviderRefreshOutcome.SignedOut;
        }

        _tokenSource.MarkRefreshRequired();

        ProviderRefreshResult result;
        try
        {
            result = await _transport.RefreshAsync(
                persistedRefreshToken,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Preserve the prior refresh token on transport ambiguity. The
            // provider may have consumed it and can use its reuse/parent-token
            // rules on a later retry.
            return ProviderRefreshOutcome.RefreshRequired;
        }

        switch (result.Disposition)
        {
            case ProviderRefreshDisposition.Success:
                var tokens = result.Tokens
                    ?? throw new InvalidOperationException(
                        "Successful refresh result must include rotated tokens.");
                ValidateTokens(tokens);
                await _refreshTokenVault.StoreAsync(
                    tokens.RefreshToken,
                    cancellationToken);
                _tokenSource.Establish(
                    tokens.AccessToken,
                    tokens.AccessTokenExpiresAt);
                return ProviderRefreshOutcome.Usable;

            case ProviderRefreshDisposition.Rejected:
                await _refreshTokenVault.ClearAsync(cancellationToken);
                _tokenSource.Invalidate();
                return ProviderRefreshOutcome.Invalid;

            case ProviderRefreshDisposition.RetryableFailure:
            case ProviderRefreshDisposition.ResultUnknown:
                return ProviderRefreshOutcome.RefreshRequired;

            default:
                throw new InvalidOperationException(
                    $"Unsupported refresh disposition: {result.Disposition}.");
        }
    }

    private static void ValidateTokens(ProviderAuthTokens tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokens.AccessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokens.RefreshToken);

        if (tokens.AccessToken.Any(char.IsWhiteSpace) ||
            tokens.RefreshToken.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Provider session tokens must not contain whitespace.",
                nameof(tokens));
        }

        if (tokens.AccessTokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tokens),
                "Provider access-token expiry must be in the future.");
        }
    }
}
