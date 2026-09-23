namespace Xueqing.Windows.Core.Services;

public enum ClientCompatibilityState
{
    Supported,
    UpdateRecommended,
    UpdateRequired,
    SecurityBlocked,
}

public static class ClientCompatibilityStateExtensions
{
    public static bool AllowsConsequentialWrite(this ClientCompatibilityState state) =>
        state is ClientCompatibilityState.Supported or ClientCompatibilityState.UpdateRecommended;
}

public sealed record ClientCompatibilityRequest(
    string Platform,
    string AppVersion,
    int ContractVersion)
{
    public ClientCompatibilityRequest Validate()
    {
        if (Platform is not ("android" or "windows"))
        {
            throw new ArgumentException("Unsupported client platform.", nameof(Platform));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(AppVersion);
        if (AppVersion.Length > 64 || !string.Equals(AppVersion, AppVersion.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Client application version is invalid.", nameof(AppVersion));
        }
        if (ContractVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ContractVersion));
        }
        return this;
    }
}

public sealed record ClientCompatibilityDecision(
    DateTimeOffset GeneratedAtServer,
    string PolicyRevision,
    string Platform,
    string AppVersion,
    int ClientContractVersion,
    ClientCompatibilityState State,
    string ReasonCode,
    string MinimumSupportedAppVersion,
    string RecommendedAppVersion,
    int MinimumSupportedContractVersion,
    int ServerContractVersion,
    Uri? UpdateUri);

public enum ClientCompatibilityFailureKind
{
    AuthenticationRequired,
    Transient,
    PolicyUnavailable,
    InvalidResponse,
}

public sealed record ClientCompatibilityFailure(
    ClientCompatibilityFailureKind Kind,
    string Code);

public sealed record ClientCompatibilityReadResult(
    ClientCompatibilityDecision? Decision,
    ClientCompatibilityFailure? Failure)
{
    public bool IsSuccess => Decision is not null && Failure is null;

    public static ClientCompatibilityReadResult Success(ClientCompatibilityDecision decision) =>
        new(decision, null);

    public static ClientCompatibilityReadResult Failed(
        ClientCompatibilityFailureKind kind,
        string code) =>
        new(null, new ClientCompatibilityFailure(kind, code));
}

public interface IClientCompatibilityReader
{
    Task<ClientCompatibilityReadResult> ReadAsync(
        ClientCompatibilityRequest request,
        CancellationToken cancellationToken = default);
}


public enum ConsequentialWriteCompatibilityKind
{
    Allowed,
    AuthenticationRequired,
    TemporarilyUnavailable,
    Blocked,
    ProtocolFailure,
}

public sealed record ConsequentialWriteCompatibilityResult(
    ConsequentialWriteCompatibilityKind Kind,
    string? Code = null)
{
    public static ConsequentialWriteCompatibilityResult Allowed() =>
        new(ConsequentialWriteCompatibilityKind.Allowed);
}

public interface IConsequentialWriteCompatibilityGate
{
    Task<ConsequentialWriteCompatibilityResult> CheckAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ServerClientCompatibilityWriteGate(
    IClientCompatibilityReader reader,
    ClientCompatibilityRequest request) : IConsequentialWriteCompatibilityGate
{
    private readonly IClientCompatibilityReader _reader =
        reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly ClientCompatibilityRequest _request =
        (request ?? throw new ArgumentNullException(nameof(request))).Validate();

    public async Task<ConsequentialWriteCompatibilityResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _reader.ReadAsync(_request, cancellationToken).ConfigureAwait(false);
        if (result.Decision is { } decision)
        {
            return decision.State.AllowsConsequentialWrite()
                ? ConsequentialWriteCompatibilityResult.Allowed()
                : new ConsequentialWriteCompatibilityResult(
                    ConsequentialWriteCompatibilityKind.Blocked,
                    decision.ReasonCode);
        }

        return result.Failure?.Kind switch
        {
            ClientCompatibilityFailureKind.AuthenticationRequired =>
                new(ConsequentialWriteCompatibilityKind.AuthenticationRequired, result.Failure.Code),
            ClientCompatibilityFailureKind.Transient or
            ClientCompatibilityFailureKind.PolicyUnavailable =>
                new(ConsequentialWriteCompatibilityKind.TemporarilyUnavailable, result.Failure.Code),
            ClientCompatibilityFailureKind.InvalidResponse =>
                new(ConsequentialWriteCompatibilityKind.ProtocolFailure, result.Failure.Code),
            _ =>
                new(ConsequentialWriteCompatibilityKind.ProtocolFailure, "XQ_COMPATIBILITY_RESULT_INVALID"),
        };
    }
}

public sealed class DevelopmentAllowAllConsequentialWriteCompatibilityGate :
    IConsequentialWriteCompatibilityGate
{
    public static DevelopmentAllowAllConsequentialWriteCompatibilityGate Instance { get; } = new();

    private DevelopmentAllowAllConsequentialWriteCompatibilityGate()
    {
    }

    public Task<ConsequentialWriteCompatibilityResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ConsequentialWriteCompatibilityResult.Allowed());
    }
}
