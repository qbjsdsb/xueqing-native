namespace Xueqing.Windows.Core.Models;

public sealed record AcceptOrganizationInvitationRequest(
    Guid OperationId,
    Guid InvitationId,
    string DisplayName);

public sealed record AcceptOrganizationInvitationReceipt(
    Guid OperationId,
    Guid InvitationId,
    Guid ActorAppUserId,
    Guid OrganizationId,
    OrganizationMembershipRole MembershipRole,
    bool CanTeach,
    DateTimeOffset ServerCommittedAt);

public enum AcceptOrganizationInvitationFailureKind
{
    AuthenticationRequired,
    InvitationUnavailable,
    IdentityUnavailable,
    MembershipConflict,
    Validation,
    OperationConflict,
    ResultUnknown,
    InvalidResponse,
}

public sealed record AcceptOrganizationInvitationFailure(
    AcceptOrganizationInvitationFailureKind Kind,
    string Code);

public sealed record AcceptOrganizationInvitationResult(
    AcceptOrganizationInvitationReceipt? Receipt,
    AcceptOrganizationInvitationFailure? Failure)
{
    public bool IsSuccess => Receipt is not null && Failure is null;

    public bool MustRetrySameOperation =>
        Failure?.Kind == AcceptOrganizationInvitationFailureKind.ResultUnknown;

    public static AcceptOrganizationInvitationResult Success(
        AcceptOrganizationInvitationReceipt receipt) =>
        new(receipt ?? throw new ArgumentNullException(nameof(receipt)), null);

    public static AcceptOrganizationInvitationResult Failed(
        AcceptOrganizationInvitationFailureKind kind,
        string code) =>
        new(null, new AcceptOrganizationInvitationFailure(kind, code));
}
