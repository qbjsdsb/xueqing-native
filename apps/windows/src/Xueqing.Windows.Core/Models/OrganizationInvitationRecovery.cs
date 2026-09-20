namespace Xueqing.Windows.Core.Models;

public enum OrganizationInvitationRecoveryStage
{
    CreatePending,
    DeliveryPending,
    DeliveryFailed,
}

public sealed record OrganizationInvitationRecoveryIntent(
    Guid CreateOperationId,
    Guid DeliveryOperationId,
    Guid OrganizationId,
    string InvitedEmail,
    OrganizationInvitationTargetRole TargetRole,
    bool TargetCanTeach,
    Guid? InvitationId,
    OrganizationInvitationRecoveryStage Stage,
    string? TerminalFailureCode)
{
    public CreateOrganizationInvitationRequest CreateRequest =>
        new(
            CreateOperationId,
            OrganizationId,
            InvitedEmail,
            TargetRole,
            TargetCanTeach);

    public DeliverOrganizationInvitationRequest DeliveryRequest =>
        InvitationId is Guid invitationId && invitationId != Guid.Empty
            ? new(DeliveryOperationId, invitationId)
            : throw new InvalidOperationException(
                "Invitation delivery recovery requires an authoritative invitation id.");

    public static OrganizationInvitationRecoveryIntent Create(
        CreateOrganizationInvitationRequest request,
        Guid deliveryOperationId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (deliveryOperationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Delivery operation id must be non-empty.",
                nameof(deliveryOperationId));
        }

        return new OrganizationInvitationRecoveryIntent(
            request.OperationId,
            deliveryOperationId,
            request.OrganizationId,
            request.InvitedEmail.Trim().ToLowerInvariant(),
            request.TargetRole,
            request.TargetCanTeach,
            null,
            OrganizationInvitationRecoveryStage.CreatePending,
            null);
    }

    public OrganizationInvitationRecoveryIntent BeginDelivery(Guid invitationId)
    {
        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException("Invitation id must be non-empty.", nameof(invitationId));
        }

        return this with
        {
            InvitationId = invitationId,
            Stage = OrganizationInvitationRecoveryStage.DeliveryPending,
            TerminalFailureCode = null,
        };
    }

    public OrganizationInvitationRecoveryIntent MarkDeliveryFailed(string code)
    {
        if (InvitationId is not Guid invitationId || invitationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "A terminal delivery failure requires an authoritative invitation id.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Failure code must not be blank.", nameof(code));
        }

        return this with
        {
            Stage = OrganizationInvitationRecoveryStage.DeliveryFailed,
            TerminalFailureCode = code.Trim(),
        };
    }

    public void Validate()
    {
        if (CreateOperationId == Guid.Empty ||
            DeliveryOperationId == Guid.Empty ||
            OrganizationId == Guid.Empty)
        {
            throw new InvalidDataException(
                "Organization invitation recovery contains an empty UUID.");
        }

        var normalizedEmail = InvitedEmail.Trim().ToLowerInvariant();
        if (!string.Equals(InvitedEmail, normalizedEmail, StringComparison.Ordinal) ||
            normalizedEmail.Length is < 3 or > 320 ||
            normalizedEmail.Any(char.IsWhiteSpace) ||
            normalizedEmail.Count(ch => ch == '@') != 1 ||
            normalizedEmail.StartsWith('@') ||
            normalizedEmail.EndsWith('@'))
        {
            throw new InvalidDataException(
                "Organization invitation recovery contains an invalid normalized email.");
        }

        switch (Stage)
        {
            case OrganizationInvitationRecoveryStage.CreatePending:
                if (InvitationId is not null || TerminalFailureCode is not null)
                {
                    throw new InvalidDataException(
                        "Create-pending invitation recovery cannot contain delivery state.");
                }
                break;

            case OrganizationInvitationRecoveryStage.DeliveryPending:
                if (InvitationId is not Guid pendingId ||
                    pendingId == Guid.Empty ||
                    TerminalFailureCode is not null)
                {
                    throw new InvalidDataException(
                        "Delivery-pending invitation recovery is malformed.");
                }
                break;

            case OrganizationInvitationRecoveryStage.DeliveryFailed:
                if (InvitationId is not Guid failedId ||
                    failedId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(TerminalFailureCode))
                {
                    throw new InvalidDataException(
                        "Delivery-failed invitation recovery is malformed.");
                }
                break;

            default:
                throw new InvalidDataException(
                    "Organization invitation recovery contains an unknown stage.");
        }
    }
}

public enum OrganizationInvitationWorkflowOutcome
{
    Sent,
    CreatePendingConfirmation,
    DeliveryPendingConfirmation,
    DeliveryBlocked,
    DeliveryFailed,
    Rejected,
    LocalDurabilityFailure,
}

public sealed record OrganizationInvitationWorkflowResult(
    OrganizationInvitationWorkflowOutcome Outcome,
    OrganizationInvitationRecoveryIntent? RecoveryIntent,
    CreateOrganizationInvitationFailure? CreateFailure,
    DeliverOrganizationInvitationFailure? DeliveryFailure)
{
    public bool IsSuccess => Outcome == OrganizationInvitationWorkflowOutcome.Sent;

    public bool HasRecoverableIntent =>
        RecoveryIntent is not null &&
        Outcome is
            OrganizationInvitationWorkflowOutcome.CreatePendingConfirmation or
            OrganizationInvitationWorkflowOutcome.DeliveryPendingConfirmation or
            OrganizationInvitationWorkflowOutcome.DeliveryBlocked or
            OrganizationInvitationWorkflowOutcome.DeliveryFailed or
            OrganizationInvitationWorkflowOutcome.LocalDurabilityFailure;
}
