using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class StudentLearningFocusJsonParser
{
    private const string ExpectedContract = "student_learning_focus_v1";
    private const int MaximumCases = 3;

    public static StudentLearningFocusSnapshot Parse(
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
            throw new InvalidDataException("Unexpected Student Learning Focus contract.");
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

        if (!root.TryGetProperty("cases", out var casesElement) || casesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Projection cases must be an array.");
        }

        var cases = new List<StudentLearningCaseFocus>();
        var seenCaseIds = new HashSet<Guid>();
        var seenActionIds = new HashSet<Guid>();
        StudentLearningCaseFocus? previous = null;

        foreach (var item in casesElement.EnumerateArray())
        {
            if (cases.Count >= MaximumCases)
            {
                throw new InvalidDataException("Student Learning Focus exceeded the v1 Case bound.");
            }

            LearningProjectionJson.RequireObject(item, "case");
            if (!item.TryGetProperty("primary_action", out var actionElement))
            {
                throw new InvalidDataException("Open Case is missing its primary_action object.");
            }

            LearningProjectionJson.RequireObject(actionElement, "primary_action");

            var caseId = LearningProjectionJson.GetRequiredGuid(item, "case_id");
            var responsibleTeacher = LearningProjectionJson.GetRequiredGuid(item, "responsible_teacher_app_user_id");
            var ownerAssignment = LearningProjectionJson.GetRequiredGuid(item, "owner_assignment_id");
            if (responsibleTeacher != expectedActorAppUserId || ownerAssignment != assignmentId)
            {
                throw new InvalidDataException("Personal Current Focus responsibility does not match the active teaching context.");
            }

            var dueOn = LearningProjectionJson.GetOptionalDate(actionElement, "due_on");
            var dueBucket = LearningProjectionJson.GetDueBucket(actionElement, "due_bucket");
            if (dueBucket != LearningProjectionJson.ExpectedDueBucket(dueOn, businessDate))
            {
                throw new InvalidDataException("Primary Action due bucket conflicts with the Organization business date.");
            }

            var focus = new StudentLearningCaseFocus(
                caseId,
                LearningProjectionJson.GetRequiredNonBlankString(item, "title"),
                LearningProjectionJson.GetOpenCaseState(item, "state"),
                LearningProjectionJson.GetRequiredPositiveVersion(item, "case_version"),
                responsibleTeacher,
                ownerAssignment,
                LearningProjectionJson.GetRequiredTimestamp(item, "created_at_server"),
                LearningProjectionJson.GetRequiredTimestamp(item, "updated_at_server"),
                new LearningPrimaryAction(
                    LearningProjectionJson.GetRequiredGuid(actionElement, "action_id"),
                    LearningProjectionJson.GetRequiredNonBlankString(actionElement, "action_text"),
                    dueOn,
                    dueBucket,
                    LearningProjectionJson.GetRequiredPositiveVersion(actionElement, "action_version")));

            if (focus.UpdatedAtServer < focus.CreatedAtServer)
            {
                throw new InvalidDataException("Case updated_at_server precedes created_at_server.");
            }

            if (!seenCaseIds.Add(focus.CaseId) || !seenActionIds.Add(focus.PrimaryAction.ActionId))
            {
                throw new InvalidDataException("Projection contains duplicate Case or Action identity.");
            }

            if (previous is not null && ComesBefore(focus, previous))
            {
                throw new InvalidDataException("Current Focus Cases are not in authoritative recent-active order.");
            }

            cases.Add(focus);
            previous = focus;
        }

        var hasMore = LearningProjectionJson.GetRequiredBoolean(root, "has_more");
        if (hasMore && cases.Count != MaximumCases)
        {
            throw new InvalidDataException("A truncated Current Focus snapshot must contain the full visible Case bound.");
        }

        return new StudentLearningFocusSnapshot(
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

    private static bool ComesBefore(StudentLearningCaseFocus current, StudentLearningCaseFocus previous)
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
