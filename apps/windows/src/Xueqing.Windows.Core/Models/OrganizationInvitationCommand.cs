namespace Xueqing.Windows.Core.Models;

public enum OrganizationInvitationTargetRole
{
    Owner,
    Admin,
    Teacher,
}

public sealed record CreateOrganizationInvitationRequest(
    Guid OperationId,
    Guid OrganizationId,
    string InvitedEmail,
    OrganizationInvitationTargetRole TargetRole,
    bool TargetCanTeach);

public sealed record CreateOrganizationInvitationReceipt(
    Guid OperationId,
    Guid InvitationId,
    Guid ActorAppUserId,
    Guid OrganizationId,
    string InvitedEmail,
    OrganizationInvitationTargetRole TargetRole,
    bool TargetCanTeach,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ServerCommittedAt);

public enum CreateOrganizationInvitationFailureKind
{
    AuthenticationRequired,
    AuthorityChanged,
    Validation,
    OperationConflict,
    AlreadyPending,
    ResultUnknown,
    InvalidResponse,
}

public sealed record CreateOrganizationInvitationFailure(
    CreateOrganizationInvitationFailureKind Kind,
    string Code);

public sealed record CreateOrganizationInvitationResult(
    CreateOrganizationInvitationReceipt? Receipt,
    CreateOrganizationInvitationFailure? Failure)
{
    public bool IsSuccess => Receipt is not null && Failure is null;

    public bool MustRetrySameOperation =>
        Failure?.Kind == CreateOrganizationInvitationFailureKind.ResultUnknown;

    public static CreateOrganizationInvitationResult Success(
        CreateOrganizationInvitationReceipt receipt) =>
        new(receipt ?? throw new ArgumentNullException(nameof(receipt)), null);

    public static CreateOrganizationInvitationResult Failed(
        CreateOrganizationInvitationFailureKind kind,
        string code) =>
        new(null, new CreateOrganizationInvitationFailure(kind, code));
}
