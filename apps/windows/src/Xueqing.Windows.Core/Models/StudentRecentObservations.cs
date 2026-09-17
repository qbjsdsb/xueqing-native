namespace Xueqing.Windows.Core.Models;

public sealed record StudentObservationScope(
    Guid OrganizationId,
    Guid StudentId,
    Guid SubjectProfileId);

public sealed record StudentRecentObservation(
    Guid ObservationId,
    Guid ActorAppUserId,
    string RawText,
    DateTimeOffset? ClientCapturedAt,
    DateTimeOffset CreatedAtServer);

public sealed record StudentRecentObservationsSnapshot(
    DateTimeOffset GeneratedAtServer,
    Guid ActorAppUserId,
    Guid OrganizationId,
    Guid StudentId,
    string StudentDisplayName,
    Guid SubjectProfileId,
    string SubjectKey,
    Guid AssignmentId,
    IReadOnlyList<StudentRecentObservation> Observations,
    bool HasMore);

public enum StudentRecentObservationsFailureKind
{
    AuthenticationRequired,
    AccessDenied,
    Transient,
    InvalidResponse,
}

public sealed record StudentRecentObservationsFailure(
    StudentRecentObservationsFailureKind Kind,
    string Code);

public sealed record StudentRecentObservationsReadResult(
    StudentRecentObservationsSnapshot? Snapshot,
    StudentRecentObservationsFailure? Failure)
{
    public bool IsSuccess => Snapshot is not null && Failure is null;

    public static StudentRecentObservationsReadResult Success(StudentRecentObservationsSnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static StudentRecentObservationsReadResult Failed(
        StudentRecentObservationsFailureKind kind,
        string code) =>
        new(null, new StudentRecentObservationsFailure(kind, code));
}
