using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class CaseLifecycleReceiptJsonParser
{
    public static TransitionLearningCaseStateReceipt ParseTransition(
        string json,
        TransitionLearningCaseStateRequest request,
        Guid actorId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckCommon(
            root,
            "transition_learning_case_state_v1",
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            request.CaseId,
            actorId,
            request.ExpectedCaseVersion);

        var previousState = LearningProjectionJson.GetCaseState(root, "previous_case_state");
        var state = LearningProjectionJson.GetCaseState(root, "case_state");
        if (previousState != ExpectedPreviousState(request.TargetState) ||
            state != request.TargetState)
        {
            throw new InvalidDataException(
                "Lifecycle transition receipt differs from submitted state intent.");
        }

        return new TransitionLearningCaseStateReceipt(
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            actorId,
            request.CaseId,
            previousState,
            state,
            request.ExpectedCaseVersion + 1,
            LearningProjectionJson.GetRequiredGuid(root, "primary_action_id"),
            LearningProjectionJson.GetRequiredGuid(root, "case_event_id"),
            LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at"));
    }

    public static CloseLearningCaseReceipt ParseClose(
        string json,
        CloseLearningCaseRequest request,
        Guid actorId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckCommon(
            root,
            "close_learning_case_v1",
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            request.CaseId,
            actorId,
            request.ExpectedCaseVersion);

        var previousState = LearningProjectionJson.GetCaseState(root, "previous_case_state");
        var state = LearningProjectionJson.GetCaseState(root, "case_state");
        var cancelledActionId =
            LearningProjectionJson.GetRequiredGuid(root, "cancelled_primary_action_id");
        var cancelledActionVersion =
            LearningProjectionJson.GetRequiredPositiveVersion(root, "cancelled_action_version");

        if (previousState != LearningCaseState.Stable ||
            state != LearningCaseState.Closed ||
            cancelledActionId != request.PrimaryActionId ||
            cancelledActionVersion != request.ExpectedActionVersion + 1)
        {
            throw new InvalidDataException(
                "Close receipt differs from submitted Case/Action intent.");
        }

        return new CloseLearningCaseReceipt(
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            actorId,
            request.CaseId,
            previousState,
            state,
            request.ExpectedCaseVersion + 1,
            cancelledActionId,
            cancelledActionVersion,
            LearningProjectionJson.GetRequiredGuid(root, "case_event_id"),
            LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at"));
    }

    public static ReopenLearningCaseReceipt ParseReopen(
        string json,
        ReopenLearningCaseRequest request,
        Guid actorId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckCommon(
            root,
            "reopen_learning_case_v1",
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            request.CaseId,
            actorId,
            request.ExpectedCaseVersion);

        var previousState = LearningProjectionJson.GetCaseState(root, "previous_case_state");
        var state = LearningProjectionJson.GetCaseState(root, "case_state");
        var newActionVersion =
            LearningProjectionJson.GetRequiredPositiveVersion(root, "new_action_version");
        var newActionText =
            LearningProjectionJson.GetRequiredNonBlankString(root, "new_action_text");
        var newActionDueOn =
            LearningProjectionJson.GetOptionalDate(root, "new_action_due_on");

        if (previousState != LearningCaseState.Closed ||
            state != LearningCaseState.Intervening ||
            newActionVersion != 1 ||
            newActionText != request.NewPrimaryActionText.Trim() ||
            newActionDueOn != request.NewPrimaryActionDueOn)
        {
            throw new InvalidDataException(
                "Reopen receipt differs from submitted next-Action intent.");
        }

        return new ReopenLearningCaseReceipt(
            request.OperationId,
            request.OrganizationId,
            request.StudentId,
            request.SubjectProfileId,
            request.OwnerAssignmentId,
            actorId,
            request.CaseId,
            previousState,
            state,
            request.ExpectedCaseVersion + 1,
            LearningProjectionJson.GetRequiredGuid(root, "new_primary_action_id"),
            newActionVersion,
            newActionText,
            newActionDueOn,
            LearningProjectionJson.GetRequiredGuid(root, "case_event_id"),
            LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at"));
    }

    public static string ToWireTargetState(LearningCaseState state) => state switch
    {
        LearningCaseState.Confirmed => "confirmed",
        LearningCaseState.Intervening => "intervening",
        LearningCaseState.PendingVerification => "pending_verification",
        LearningCaseState.Stable => "stable",
        _ => throw new ArgumentOutOfRangeException(
            nameof(state),
            "Only forward open-state lifecycle targets are valid."),
    };

    private static LearningCaseState ExpectedPreviousState(LearningCaseState targetState) =>
        targetState switch
        {
            LearningCaseState.Confirmed => LearningCaseState.New,
            LearningCaseState.Intervening => LearningCaseState.Confirmed,
            LearningCaseState.PendingVerification => LearningCaseState.Intervening,
            LearningCaseState.Stable => LearningCaseState.PendingVerification,
            _ => throw new InvalidDataException(
                "Receipt contains an unsupported lifecycle transition target."),
        };

    private static void CheckCommon(
        JsonElement root,
        string command,
        Guid operationId,
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid ownerAssignmentId,
        Guid caseId,
        Guid actorId,
        long expectedCaseVersion)
    {
        LearningProjectionJson.RequireObject(root, "Case lifecycle receipt");
        if (LearningProjectionJson.GetRequiredString(root, "command") != command ||
            LearningProjectionJson.GetRequiredGuid(root, "operation_id") != operationId ||
            LearningProjectionJson.GetRequiredGuid(root, "organization_id") != organizationId ||
            LearningProjectionJson.GetRequiredGuid(root, "student_id") != studentId ||
            LearningProjectionJson.GetRequiredGuid(root, "subject_profile_id") != subjectProfileId ||
            LearningProjectionJson.GetRequiredGuid(root, "owner_assignment_id") != ownerAssignmentId ||
            LearningProjectionJson.GetRequiredGuid(root, "case_id") != caseId ||
            LearningProjectionJson.GetRequiredGuid(
                root,
                "responsible_teacher_app_user_id") != actorId ||
            LearningProjectionJson.GetRequiredPositiveVersion(
                root,
                "case_version") != expectedCaseVersion + 1)
        {
            throw new InvalidDataException(
                "Case lifecycle receipt scope or version differs from submitted intent.");
        }
    }
}
