namespace Xueqing.Windows.Core.Models;

public sealed record DeliverOrganizationInvitationRequest(
    Guid OperationId,
    Guid InvitationId);

public sealed record DeliverOrganizationInvitationReceipt(
    Guid OperationId,
    Guid DeliveryId,
    Guid InvitationId);

public enum DeliverOrganizationInvitationFailureKind
{
    AuthenticationRequired,
    AuthorityChanged,
    Validation,
    OperationConflict,
    AlreadyDelivered,
    ProviderRejected,
    ResultUnknown,
    CompatibilityBlocked,
    CompatibilityUnavailable,
    InvalidResponse,
}

public sealed record DeliverOrganizationInvitationFailure(
    DeliverOrganizationInvitationFailureKind Kind,
    string Code);

public sealed record DeliverOrganizationInvitationResult(
    DeliverOrganizationInvitationReceipt? Receipt,
    DeliverOrganizationInvitationFailure? Failure)
{
    public bool IsSuccess => Receipt is not null && Failure is null;

    public bool MustRetrySameOperation =>
        Failure?.Kind == DeliverOrganizationInvitationFailureKind.ResultUnknown;

    public static DeliverOrganizationInvitationResult Success(
        DeliverOrganizationInvitationReceipt receipt) =>
        new(receipt ?? throw new ArgumentNullException(nameof(receipt)), null);

    public static DeliverOrganizationInvitationResult Failed(
        DeliverOrganizationInvitationFailureKind kind,
        string code) =>
        new(null, new DeliverOrganizationInvitationFailure(kind, code));
}
