using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class StudentLearningCasesJsonParser
{
    private const string ExpectedContract = "student_learning_cases_v1";
    private const int MaximumCases = 50;

    public static StudentLearningCasesSnapshot Parse(
        string json,
        StudentLearningScope requestedScope,
        Guid expectedActorAppUserId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        LearningProjectionJson.RequireObject(root, "root");

        if (!string.Equals(
                LearningProjectionJson.GetRequiredString(root, "contract"),
                ExpectedContract,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected Student Learning Cases contract.");
        }

        var actorId = LearningProjectionJson.GetRequiredGuid(root, "actor_app_user_id");
        if (actorId != expectedActorAppUserId)
        {
            throw new InvalidDataException("Projection actor does not match the active application identity.");
        }

        var organizationId = LearningProjectionJson.GetRequiredGuid(root, "organization_id");
        var studentId = LearningProjectionJson.GetRequiredGuid(root, "student_id");
        var subjectProfileId = LearningProjectionJson.GetRequiredGuid(root, "subject_profile_id");
        if (organizationId != requestedScope.OrganizationId ||
            studentId != requestedScope.StudentId ||
            subjectProfileId != requestedScope.SubjectProfileId)
        {
            throw new InvalidDataException("Projection scope does not match the requested teaching context.");
        }

        var generatedAt = LearningProjectionJson.GetRequiredTimestamp(root, "generated_at_server");
        var organizationName = LearningProjectionJson.GetRequiredNonBlankString(root, "organization_name");
        var organizationTimeZone = LearningProjectionJson.GetRequiredNonBlankString(root, "organization_time_zone");
        var businessDate = LearningProjectionJson.GetRequiredDate(root, "organization_business_date");
        var studentDisplayName = LearningProjectionJson.GetRequiredNonBlankString(root, "student_display_name");
        var subjectKey = LearningProjectionJson.GetRequiredNonBlankString(root, "subject_key");
        var assignmentId = LearningProjectionJson.GetRequiredGuid(root, "assignment_id");

        if (!root.TryGetProperty("cases", out var casesElement) ||
            casesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Projection cases must be an array.");
        }

        var cases = new List<StudentLearningCaseSummary>();
        var seenCaseIds = new HashSet<Guid>();
        var seenActionIds = new HashSet<Guid>();
        StudentLearningCaseSummary? previous = null;

        foreach (var item in casesElement.EnumerateArray())
        {
            if (cases.Count >= MaximumCases)
            {
                throw new InvalidDataException("Student Learning Cases exceeded the v1 Case bound.");
            }

            LearningProjectionJson.RequireObject(item, "case");

            var caseId = LearningProjectionJson.GetRequiredGuid(item, "case_id");
            var state = LearningProjectionJson.GetCaseState(item, "state");
            var responsibleTeacher = LearningProjectionJson.GetRequiredGuid(
                item,
                "responsible_teacher_app_user_id");
            var ownerAssignment = LearningProjectionJson.GetRequiredGuid(item, "owner_assignment_id");
            var responsibility = LearningProjectionJson.GetRequiredBoolean(
                item,
                "is_current_actor_responsibility");
            var expectedResponsibility =
                responsibleTeacher == expectedActorAppUserId &&
                ownerAssignment == assignmentId;
            if (responsibility != expectedResponsibility)
            {
                throw new InvalidDataException(
                    "Case responsibility marker conflicts with authoritative actor/assignment identity.");
            }

            if (!item.TryGetProperty("primary_action", out var actionElement))
            {
                throw new InvalidDataException("Case is missing its primary_action property.");
            }

            LearningPrimaryAction? primaryAction;
            if (state == LearningCaseState.Closed)
            {
                if (actionElement.ValueKind != JsonValueKind.Null)
                {
                    throw new InvalidDataException("Closed Case must not expose a current primary Action.");
                }

                primaryAction = null;
            }
            else
            {
                LearningProjectionJson.RequireObject(actionElement, "primary_action");
                var dueOn = LearningProjectionJson.GetOptionalDate(actionElement, "due_on");
                var dueBucket = LearningProjectionJson.GetDueBucket(actionElement, "due_bucket");
                if (dueBucket != LearningProjectionJson.ExpectedDueBucket(dueOn, businessDate))
                {
                    throw new InvalidDataException(
                        "Primary Action due bucket conflicts with the Organization business date.");
                }

                var actionId = LearningProjectionJson.GetRequiredGuid(actionElement, "action_id");
                if (!seenActionIds.Add(actionId))
                {
                    throw new InvalidDataException("Projection contains duplicate primary Action identity.");
                }

                primaryAction = new LearningPrimaryAction(
                    actionId,
                    LearningProjectionJson.GetRequiredNonBlankString(actionElement, "action_text"),
                    dueOn,
                    dueBucket,
                    LearningProjectionJson.GetRequiredPositiveVersion(actionElement, "action_version"));
            }

            var summary = new StudentLearningCaseSummary(
                caseId,
                LearningProjectionJson.GetRequiredNonBlankString(item, "title"),
                state,
                LearningProjectionJson.GetRequiredPositiveVersion(item, "case_version"),
                responsibleTeacher,
                ownerAssignment,
                responsibility,
                LearningProjectionJson.GetRequiredTimestamp(item, "created_at_server"),
                LearningProjectionJson.GetRequiredTimestamp(item, "updated_at_server"),
                primaryAction);

            if (summary.UpdatedAtServer < summary.CreatedAtServer)
            {
                throw new InvalidDataException("Case updated_at_server precedes created_at_server.");
            }

            if (!seenCaseIds.Add(summary.CaseId))
            {
                throw new InvalidDataException("Projection contains duplicate Case identity.");
            }

            if (previous is not null && ComesBefore(summary, previous))
            {
                throw new InvalidDataException("Case history is not in authoritative recent-history order.");
            }

            cases.Add(summary);
            previous = summary;
        }

        var hasMore = LearningProjectionJson.GetRequiredBoolean(root, "has_more");
        if (hasMore && cases.Count != MaximumCases)
        {
            throw new InvalidDataException("A truncated Case history must contain the full visible Case bound.");
        }

        return new StudentLearningCasesSnapshot(
            generatedAt,
            actorId,
            organizationId,
            organizationName,
            organizationTimeZone,
            businessDate,
            studentId,
            studentDisplayName,
            subjectProfileId,
            subjectKey,
            assignmentId,
            cases,
            hasMore);
    }

    private static bool ComesBefore(
        StudentLearningCaseSummary current,
        StudentLearningCaseSummary previous)
    {
        if (current.UpdatedAtServer > previous.UpdatedAtServer)
        {
            return true;
        }

        if (current.UpdatedAtServer < previous.UpdatedAtServer)
        {
            return false;
        }

        return string.CompareOrdinal(
            current.CaseId.ToString("D"),
            previous.CaseId.ToString("D")) > 0;
    }
}
