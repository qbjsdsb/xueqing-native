namespace Xueqing.Windows.Core.Models;

public enum OrganizationMembershipRole
{
    Owner,
    Admin,
    Teacher,
}

public enum OrganizationMembershipStatus
{
    Active,
    Disabled,
}

public sealed record OrganizationManagementCapabilities(
    bool CanInviteOwner,
    bool CanInviteAdmin,
    bool CanInviteTeacher);

public sealed record OrganizationManagementMember(
    Guid AppUserId,
    string DisplayName,
    bool AppUserEnabled,
    OrganizationMembershipRole MembershipRole,
    OrganizationMembershipStatus MembershipStatus,
    bool CanTeach);

public sealed record OrganizationManagementSnapshot(
    DateTimeOffset GeneratedAtServer,
    Guid ActorAppUserId,
    string ActorDisplayName,
    OrganizationMembershipRole ActorMembershipRole,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationTimeZone,
    OrganizationManagementCapabilities Capabilities,
    IReadOnlyList<OrganizationManagementMember> Members);

public enum OrganizationManagementFailureKind
{
    AuthenticationRequired,
    AccessDenied,
    Transient,
    InvalidResponse,
}

public sealed record OrganizationManagementFailure(
    OrganizationManagementFailureKind Kind,
    string Code);

public sealed record OrganizationManagementReadResult(
    OrganizationManagementSnapshot? Snapshot,
    OrganizationManagementFailure? Failure)
{
    public bool IsSuccess => Snapshot is not null && Failure is null;

    public static OrganizationManagementReadResult Success(OrganizationManagementSnapshot snapshot) =>
        new(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), null);

    public static OrganizationManagementReadResult Failed(
        OrganizationManagementFailureKind kind,
        string code) =>
        new(null, new OrganizationManagementFailure(kind, code));
}
