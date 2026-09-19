using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class PersonalTodayActionsJsonParser
{
    private const string ExpectedContract = "personal_today_actions_v1";
    private const int MaximumActions = 200;

    public static PersonalTodayActionsSnapshot Parse(string json, Guid expectedActorAppUserId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        LearningProjectionJson.RequireObject(root, "root");

        if (!string.Equals(
                LearningProjectionJson.GetRequiredString(root, "contract"),
                ExpectedContract,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected Personal Today Actions contract.");
        }

        var actorId = LearningProjectionJson.GetRequiredGuid(root, "actor_app_user_id");
        if (actorId != expectedActorAppUserId)
        {
            throw new InvalidDataException("Projection actor does not match the active application identity.");
        }

        var generatedAt = LearningProjectionJson.GetRequiredTimestamp(root, "generated_at_server");
        if (!root.TryGetProperty("actions", out var actionsElement) || actionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Projection actions must be an array.");
        }

        var actions = new List<PersonalTodayAction>();
        var seenActionIds = new HashSet<Guid>();
        var seenCaseIds = new HashSet<Guid>();
        var organizationSemantics = new Dictionary<Guid, (string Name, string TimeZone, DateOnly BusinessDate)>();
        PersonalTodayAction? previous = null;

        foreach (var item in actionsElement.EnumerateArray())
        {
            if (actions.Count >= MaximumActions)
            {
                throw new InvalidDataException("Personal Today exceeded the v1 Action bound.");
            }

            LearningProjectionJson.RequireObject(item, "action");
            var organizationId = LearningProjectionJson.GetRequiredGuid(item, "organization_id");
            var organizationName = LearningProjectionJson.GetRequiredNonBlankString(item, "organization_name");
            var organizationTimeZone = LearningProjectionJson.GetRequiredNonBlankString(item, "organization_time_zone");
            var businessDate = LearningProjectionJson.GetRequiredDate(item, "organization_business_date");
            var dueOn = LearningProjectionJson.GetOptionalDate(item, "due_on");
            var dueBucket = LearningProjectionJson.GetDueBucket(item, "due_bucket");

            if (dueBucket != LearningProjectionJson.ExpectedDueBucket(dueOn, businessDate))
            {
                throw new InvalidDataException("Today Action due bucket conflicts with the Organization business date.");
            }

            var action = new PersonalTodayAction(
                organizationId,
                organizationName,
                organizationTimeZone,
                businessDate,
                LearningProjectionJson.GetRequiredGuid(item, "student_id"),
                LearningProjectionJson.GetRequiredNonBlankString(item, "student_display_name"),
                LearningProjectionJson.GetRequiredGuid(item, "subject_profile_id"),
                LearningProjectionJson.GetRequiredNonBlankString(item, "subject_key"),
                LearningProjectionJson.GetRequiredGuid(item, "assignment_id"),
                LearningProjectionJson.GetRequiredGuid(item, "case_id"),
                LearningProjectionJson.GetRequiredNonBlankString(item, "case_title"),
                LearningProjectionJson.GetOpenCaseState(item, "case_state"),
                LearningProjectionJson.GetRequiredPositiveVersion(item, "case_version"),
                LearningProjectionJson.GetRequiredGuid(item, "action_id"),
                LearningProjectionJson.GetRequiredNonBlankString(item, "action_text"),
                dueOn,
                dueBucket,
                LearningProjectionJson.GetRequiredPositiveVersion(item, "action_version"),
                LearningProjectionJson.GetRequiredTimestamp(item, "case_updated_at_server"));

            if (!seenActionIds.Add(action.ActionId) || !seenCaseIds.Add(action.CaseId))
            {
                throw new InvalidDataException("Personal Today contains duplicate Action or Case identity.");
            }

            if (organizationSemantics.TryGetValue(
                    organizationId,
                    out var knownOrganization))
            {
                if (!string.Equals(knownOrganization.Name, organizationName, StringComparison.Ordinal) ||
                    !string.Equals(knownOrganization.TimeZone, organizationTimeZone, StringComparison.Ordinal) ||
                    knownOrganization.BusinessDate != businessDate)
                {
                    throw new InvalidDataException("One Organization has inconsistent business-date semantics within the Today snapshot.");
                }
            }
            else
            {
                organizationSemantics.Add(
                    organizationId,
                    (organizationName, organizationTimeZone, businessDate));
            }

            if (previous is not null && ComesBefore(action, previous))
            {
                throw new InvalidDataException("Personal Today Actions are not in authoritative bucket order.");
            }

            actions.Add(action);
            previous = action;
        }

        var hasMore = LearningProjectionJson.GetRequiredBoolean(root, "has_more");
        if (hasMore && actions.Count != MaximumActions)
        {
            throw new InvalidDataException("A truncated Personal Today snapshot must contain the full visible Action bound.");
        }

        return new PersonalTodayActionsSnapshot(generatedAt, actorId, actions, hasMore);
    }

    private static bool ComesBefore(PersonalTodayAction current, PersonalTodayAction previous)
    {
        var currentRank = LearningProjectionJson.BucketRank(current.DueBucket);
        var previousRank = LearningProjectionJson.BucketRank(previous.DueBucket);
        if (currentRank < previousRank)
        {
            return true;
        }

        if (currentRank > previousRank)
        {
            return false;
        }

        if (current.DueOn is not null && previous.DueOn is not null)
        {
            if (current.DueOn.Value < previous.DueOn.Value)
            {
                return true;
            }

            if (current.DueOn.Value > previous.DueOn.Value)
            {
                return false;
            }
        }

        return current.CaseUpdatedAtServer > previous.CaseUpdatedAtServer;
    }
}
