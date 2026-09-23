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
