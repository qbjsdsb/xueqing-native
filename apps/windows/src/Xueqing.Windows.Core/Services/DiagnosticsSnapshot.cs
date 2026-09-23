namespace Xueqing.Windows.Core.Services;

public enum DiagnosticSessionState
{
    SignedOut,
    Authenticated,
    RefreshRequired,
    RevokedOrInvalid,
    ConfigurationUnavailable,
}

public enum DiagnosticSyncCategory
{
    Never,
    Recent,
    Stale,
    Unknown,
}

public enum DiagnosticCompatibilityState
{
    Supported,
    UpdateRecommended,
    UpdateRequired,
    SecurityBlocked,
    Unknown,
}

public sealed record DiagnosticDeployment(
    string ProfileId,
    string EnvironmentId,
    string TrustDomainId,
    string ProviderId);

public sealed record DiagnosticCompatibility(
    DiagnosticCompatibilityState State,
    string? ReasonCode,
    string? PolicyRevision);

public sealed record DiagnosticQueueCounts(
    int? PendingIntents,
    int? OutboxItems,
    int? AttachmentStaging);

public sealed record DiagnosticsSnapshot(
    DateTimeOffset GeneratedAt,
    string OsVersion,
    string Architecture,
    string PackageIdentity,
    string AppVersion,
    string SourceCommit,
    int ClientContractVersion,
    int LocalSchemaVersion,
    DiagnosticDeployment Deployment,
    DiagnosticSessionState SessionState,
    DiagnosticSyncCategory LastSyncCategory,
    DiagnosticCompatibility Compatibility,
    DiagnosticQueueCounts Queues,
    IReadOnlyList<string> RecentErrorCodes);
