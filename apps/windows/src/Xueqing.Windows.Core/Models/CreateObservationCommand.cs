namespace Xueqing.Windows.Core.Models;

public sealed record CreateObservationRequest(
    Guid OperationId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    Guid AssignmentId,
    string RawText,
    DateTimeOffset? ClientCapturedAt = null,
    IReadOnlyDictionary<string, string>? ClientCaptureMetadata = null);

public sealed record CreateObservationReceipt(
    Guid OperationId,
    Guid ObservationId,
    Guid ActorAppUserId,
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId,
    string SubjectKey,
    DateTimeOffset ServerCommittedAt);

public enum CreateObservationFailureKind
{
    AuthenticationRequired,
    AuthorityChanged,
    Validation,
    OperationConflict,
    LocalDurabilityFailure,
    ResultUnknown,
    Transient,
    InvalidResponse,
}

public sealed record CreateObservationFailure(
    CreateObservationFailureKind Kind,
    string Code);

public sealed record CreateObservationResult(
    CreateObservationReceipt? Receipt,
    CreateObservationFailure? Failure)
{
    public bool IsSuccess => Receipt is not null && Failure is null;

    public bool MustRetrySameOperation =>
        Failure?.Kind is
            CreateObservationFailureKind.ResultUnknown or
            CreateObservationFailureKind.Transient;

    public static CreateObservationResult Success(CreateObservationReceipt receipt) =>
        new(receipt ?? throw new ArgumentNullException(nameof(receipt)), null);

    public static CreateObservationResult Failed(
        CreateObservationFailureKind kind,
        string code) =>
        new(null, new CreateObservationFailure(kind, code));
}
