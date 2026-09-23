using System.Text.Json;
using Xueqing.Windows.Core.Models;

namespace Xueqing.Windows.Infrastructure.Remote;

internal static class CreateObservationReceiptJsonParser
{
    private const string ExpectedCommand = "create_observation_v1";

    public static CreateObservationReceipt Parse(
        string json,
        CreateObservationRequest request,
        Guid expectedActorAppUserId)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        LearningProjectionJson.RequireObject(root, "CreateObservation receipt");

        if (!string.Equals(
                LearningProjectionJson.GetRequiredString(root, "command"),
                ExpectedCommand,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Unexpected CreateObservation command receipt.");
        }

        var operationId = LearningProjectionJson.GetRequiredGuid(root, "operation_id");
        var observationId = LearningProjectionJson.GetRequiredGuid(root, "observation_id");
        var actorAppUserId = LearningProjectionJson.GetRequiredGuid(root, "actor_app_user_id");
        var organizationId = LearningProjectionJson.GetRequiredGuid(root, "organization_id");
        var studentId = LearningProjectionJson.GetRequiredGuid(root, "student_id");
        var subjectProfileId = LearningProjectionJson.GetRequiredGuid(root, "subject_profile_id");
        var subjectKey = LearningProjectionJson.GetRequiredNonBlankString(root, "subject_key");
        var serverCommittedAt = LearningProjectionJson.GetRequiredTimestamp(root, "server_committed_at");

        if (operationId != request.OperationId ||
            organizationId != request.OrganizationId ||
            studentId != request.StudentId ||
            subjectProfileId != request.SubjectProfileId)
        {
            throw new InvalidDataException(
                "CreateObservation receipt scope does not match the submitted intent.");
        }

        if (actorAppUserId != expectedActorAppUserId)
        {
            throw new InvalidDataException(
                "CreateObservation receipt actor does not match the active application actor.");
        }

        return new CreateObservationReceipt(
            operationId,
            observationId,
            actorAppUserId,
            organizationId,
            studentId,
            subjectProfileId,
            subjectKey,
            serverCommittedAt);
    }
}
