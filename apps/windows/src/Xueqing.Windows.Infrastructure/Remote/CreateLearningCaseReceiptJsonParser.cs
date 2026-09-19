using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class CreateLearningCaseReceiptJsonParser
{
    private const string ExpectedCommand = "create_learning_case_v1";

    public static CreateLearningCaseReceipt Parse(
        string json,
        CreateLearningCaseRequest request,
        Guid expectedActorAppUserId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        LearningProjectionJson.RequireObject(root, "CreateLearningCase receipt");

        if (!string.Equals(
                LearningProjectionJson.GetRequiredString(root, "command"),
                ExpectedCommand,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected CreateLearningCase command receipt.");
        }

        var operationId = LearningProjectionJson.GetRequiredGuid(root, "operation_id");
        var caseId = LearningProjectionJson.GetRequiredGuid(root, "case_id");
        var primaryActionId = LearningProjectionJson.GetRequiredGuid(root, "primary_action_id");
        var caseEventId = LearningProjectionJson.GetRequiredGuid(root, "case_event_id");
        var responsibleTeacher = LearningProjectionJson.GetRequiredGuid(
            root,
            "responsible_teacher_app_user_id");
        var ownerAssignmentId = LearningProjectionJson.GetRequiredGuid(root, "owner_assignment_id");
        var organizationId = LearningProjectionJson.GetRequiredGuid(root, "organization_id");
        var studentId = LearningProjectionJson.GetRequiredGuid(root, "student_id");
        var subjectProfileId = LearningProjectionJson.GetRequiredGuid(root, "subject_profile_id");
        var subjectKey = LearningProjectionJson.GetRequiredNonBlankString(root, "subject_key");
        var sourceObservationId = GetOptionalGuid(root, "source_observation_id");
        var serverCommittedAt = LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at");

        if (operationId != request.OperationId ||
            ownerAssignmentId != request.OwnerAssignmentId ||
            organizationId != request.OrganizationId ||
            studentId != request.StudentId ||
            subjectProfileId != request.SubjectProfileId)
        {
            throw new InvalidDataException("CreateLearningCase receipt scope does not match the submitted intent.");
        }

        if (responsibleTeacher != expectedActorAppUserId)
        {
            throw new InvalidDataException("CreateLearningCase receipt responsibility does not match the active application actor.");
        }

        if (sourceObservationId != request.SourceObservationId)
        {
            throw new InvalidDataException("CreateLearningCase receipt source Observation does not match the submitted intent.");
        }

        var caseState = LearningProjectionJson.GetRequiredString(root, "case_state");
        if (!string.Equals(caseState, "new", StringComparison.Ordinal))
        {
            throw new InvalidDataException("A newly created Learning Case must be returned in new state.");
        }

        var caseVersion = LearningProjectionJson.GetRequiredPositiveVersion(root, "case_version");
        if (caseVersion != 1)
        {
            throw new InvalidDataException("A newly created Learning Case must begin at version 1.");
        }

        return new CreateLearningCaseReceipt(
            operationId,
            caseId,
            LearningCaseState.New,
            caseVersion,
            primaryActionId,
            caseEventId,
            responsibleTeacher,
            ownerAssignmentId,
            organizationId,
            studentId,
            subjectProfileId,
            subjectKey,
            sourceObservationId,
            serverCommittedAt);
    }

    private static Guid? GetOptionalGuid(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(property.GetString(), "D", out var value) ||
            value == Guid.Empty)
        {
            throw new InvalidDataException(
                $"CreateLearningCase receipt property '{propertyName}' must be null or a non-empty canonical UUID.");
        }

        return value;
    }
}
