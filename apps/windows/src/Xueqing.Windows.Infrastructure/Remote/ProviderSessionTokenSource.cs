namespace Xueqing.Windows.Infrastructure.Remote;

public enum ProviderSessionStatus
{
    SignedOut,
    Usable,
    RefreshRequired,
    Invalid,
}

public sealed record ProviderSessionSnapshot(
    string EnvironmentId,
    string TrustDomainId,
    ProviderSessionStatus Status,
    DateTimeOffset? AccessTokenExpiresAt);

/// <summary>
/// Process-local access-token boundary for production provider adapters.
///
/// Refresh-token persistence and provider login are deliberately owned by the
/// later Auth coordinator. This type never persists tokens and never exposes
/// provider subject as Xueqing business identity.
/// </summary>
public sealed class ProviderSessionTokenSource
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private string? _accessToken;
    private DateTimeOffset? _accessTokenExpiresAt;
    private ProviderSessionStatus _status = ProviderSessionStatus.SignedOut;

    public ProviderSessionTokenSource(
        string environmentId,
        string trustDomainId,
        TimeProvider? timeProvider = null)
    {
        EnvironmentId = RequireIdentifier(environmentId, nameof(environmentId));
        TrustDomainId = RequireIdentifier(trustDomainId, nameof(trustDomainId));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string EnvironmentId { get; }

    public string TrustDomainId { get; }

    public ProviderSessionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                ExpireIfNeeded();
                return new ProviderSessionSnapshot(
                    EnvironmentId,
                    TrustDomainId,
                    _status,
                    _accessTokenExpiresAt);
            }
        }
    }

    public void Establish(string accessToken, DateTimeOffset accessTokenExpiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (accessToken.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Access token must not contain whitespace.",
                nameof(accessToken));
        }

        var now = _timeProvider.GetUtcNow();
        if (accessTokenExpiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessTokenExpiresAt),
                "Access token expiry must be in the future.");
        }

        lock (_gate)
        {
            _accessToken = accessToken;
            _accessTokenExpiresAt = accessTokenExpiresAt;
            _status = ProviderSessionStatus.Usable;
        }
    }

    public void MarkRefreshRequired()
    {
        lock (_gate)
        {
            ClearToken();
            _status = ProviderSessionStatus.RefreshRequired;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            ClearToken();
            _status = ProviderSessionStatus.Invalid;
        }
    }

    public void SignOut()
    {
        lock (_gate)
        {
            ClearToken();
            _status = ProviderSessionStatus.SignedOut;
        }
    }

    public ValueTask<string?> GetCurrentAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ExpireIfNeeded();
            return ValueTask.FromResult(
                _status == ProviderSessionStatus.Usable
                    ? _accessToken
                    : null);
        }
    }

    private void ExpireIfNeeded()
    {
        if (_status != ProviderSessionStatus.Usable ||
            _accessTokenExpiresAt is null)
        {
            return;
        }

        if (_accessTokenExpiresAt <= _timeProvider.GetUtcNow())
        {
            ClearToken();
            _status = ProviderSessionStatus.RefreshRequired;
        }
    }

    private void ClearToken()
    {
        _accessToken = null;
        _accessTokenExpiresAt = null;
    }

    private static string RequireIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (!string.Equals(value, trimmed, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Session scope identifiers must not contain surrounding whitespace.",
                parameterName);
        }

        return trimmed;
    }
}
