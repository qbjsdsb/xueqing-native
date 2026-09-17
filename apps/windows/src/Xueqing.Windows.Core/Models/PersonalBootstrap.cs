namespace Xueqing.Windows.Core.Models;

public sealed record PersonalOrganization(
    Guid OrganizationId,
    string Name,
    bool CanTeach);

public sealed record PersonalTeachingContext(
    Guid OrganizationId,
    Guid StudentId,
    string StudentDisplayName,
    Guid SubjectProfileId,
    string SubjectKey,
    Guid AssignmentId)
{
    public StudentObservationScope ObservationScope =>
        new(OrganizationId, StudentId, SubjectProfileId);
}

public sealed record PersonalBootstrapSnapshot(
    DateTimeOffset GeneratedAtServer,
    Guid ActorAppUserId,
    string ActorDisplayName,
    IReadOnlyList<PersonalOrganization> Organizations,
    IReadOnlyList<PersonalTeachingContext> TeachingContexts);

public enum PersonalBootstrapFailureKind
{
    AuthenticationRequired,
    AccessDenied,
    Transient,
    InvalidResponse,
}

public sealed record PersonalBootstrapFailure(
    PersonalBootstrapFailureKind Kind,
    string Code);

public sealed record PersonalBootstrapReadResult(
    PersonalBootstrapSnapshot? Snapshot,
    PersonalBootstrapFailure? Failure)
{
    public bool IsSuccess => Snapshot is not null && Failure is null;

    public static PersonalBootstrapReadResult Success(PersonalBootstrapSnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static PersonalBootstrapReadResult Failed(PersonalBootstrapFailureKind kind, string code) =>
        new(null, new PersonalBootstrapFailure(kind, code));
}
