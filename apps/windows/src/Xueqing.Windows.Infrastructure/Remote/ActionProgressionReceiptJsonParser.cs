using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class ActionProgressionReceiptJsonParser
{
    public static ReschedulePrimaryActionReceipt ParseReschedule(
        string json, ReschedulePrimaryActionRequest request, Guid actorId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckCommon(root, "reschedule_primary_action_v1", request.OperationId,
            request.OrganizationId, request.StudentId, request.SubjectProfileId,
            request.OwnerAssignmentId, request.CaseId, actorId, request.ExpectedCaseVersion);
        var actionId = LearningProjectionJson.GetRequiredGuid(root, "primary_action_id");
        var actionVersion = LearningProjectionJson.GetRequiredPositiveVersion(root, "action_version");
        var dueOn = LearningProjectionJson.GetOptionalDate(root, "due_on");
        if (actionId != request.PrimaryActionId ||
            actionVersion != request.ExpectedActionVersion + 1 ||
            dueOn != request.NewDueOn)
        {
            throw new InvalidDataException("Reschedule receipt differs from submitted Action intent.");
        }
        return new ReschedulePrimaryActionReceipt(
            request.OperationId, request.OrganizationId, request.StudentId,
            request.SubjectProfileId, request.OwnerAssignmentId, actorId,
            request.CaseId, LearningProjectionJson.GetOpenCaseState(root, "case_state"),
            request.ExpectedCaseVersion + 1, actionId, actionVersion,
            LearningProjectionJson.GetOptionalDate(root, "previous_due_on"), dueOn,
            LearningProjectionJson.GetRequiredGuid(root, "case_event_id"),
            LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at"));
    }

    public static RecordVerificationAndNextActionReceipt ParseVerification(
        string json, RecordVerificationAndNextActionRequest request, Guid actorId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        CheckCommon(root, "record_verification_and_next_action_v1", request.OperationId,
            request.OrganizationId, request.StudentId, request.SubjectProfileId,
            request.OwnerAssignmentId, request.CaseId, actorId, request.ExpectedCaseVersion);
        var oldActionId = LearningProjectionJson.GetRequiredGuid(root, "completed_primary_action_id");
        var oldActionVersion = LearningProjectionJson.GetRequiredPositiveVersion(root, "completed_action_version");
        var nextActionVersion = LearningProjectionJson.GetRequiredPositiveVersion(root, "next_action_version");
        var outcome = LearningProjectionJson.GetRequiredString(root, "verification_outcome");
        var summary = LearningProjectionJson.GetRequiredNonBlankString(root, "verification_summary");
        var nextText = LearningProjectionJson.GetRequiredNonBlankString(root, "next_action_text");
        var dueOn = LearningProjectionJson.GetOptionalDate(root, "next_action_due_on");
        if (oldActionId != request.CurrentPrimaryActionId ||
            oldActionVersion != request.ExpectedActionVersion + 1 ||
            nextActionVersion != 1 ||
            outcome != ToWireOutcome(request.Outcome) ||
            summary != request.VerificationSummary.Trim() ||
            nextText != request.NextActionText.Trim() ||
            dueOn != request.NextActionDueOn)
        {
            throw new InvalidDataException("Verification receipt differs from submitted Action intent.");
        }
        var nextActionId = LearningProjectionJson.GetRequiredGuid(root, "next_primary_action_id");
        if (nextActionId == oldActionId)
        {
            throw new InvalidDataException("Replacement Action must differ from completed Action.");
        }
        return new RecordVerificationAndNextActionReceipt(
            request.OperationId, request.OrganizationId, request.StudentId,
            request.SubjectProfileId, request.OwnerAssignmentId, actorId,
            request.CaseId, LearningProjectionJson.GetOpenCaseState(root, "case_state"),
            request.ExpectedCaseVersion + 1, oldActionId, oldActionVersion,
            LearningProjectionJson.GetRequiredGuid(root, "verification_id"),
            request.Outcome, summary, nextActionId, nextActionVersion, nextText, dueOn,
            LearningProjectionJson.GetRequiredGuid(root, "case_event_id"),
            LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at"));
    }

    public static string ToWireOutcome(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Met => "met",
        VerificationOutcome.PartiallyMet => "partially_met",
        VerificationOutcome.NotMet => "not_met",
        VerificationOutcome.Uncertain => "uncertain",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static void CheckCommon(
        JsonElement root, string command, Guid operationId, Guid organizationId,
        Guid studentId, Guid profileId, Guid assignmentId, Guid caseId,
        Guid actorId, long expectedCaseVersion)
    {
        LearningProjectionJson.RequireObject(root, "Action progression receipt");
        if (LearningProjectionJson.GetRequiredString(root, "command") != command ||
            LearningProjectionJson.GetRequiredGuid(root, "operation_id") != operationId ||
            LearningProjectionJson.GetRequiredGuid(root, "organization_id") != organizationId ||
            LearningProjectionJson.GetRequiredGuid(root, "student_id") != studentId ||
            LearningProjectionJson.GetRequiredGuid(root, "subject_profile_id") != profileId ||
            LearningProjectionJson.GetRequiredGuid(root, "owner_assignment_id") != assignmentId ||
            LearningProjectionJson.GetRequiredGuid(root, "case_id") != caseId ||
            LearningProjectionJson.GetRequiredGuid(root, "responsible_teacher_app_user_id") != actorId ||
            LearningProjectionJson.GetRequiredPositiveVersion(root, "case_version") != expectedCaseVersion + 1)
        {
            throw new InvalidDataException("Action progression receipt scope or version differs from submitted intent.");
        }
    }
}
