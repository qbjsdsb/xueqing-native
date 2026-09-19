namespace Xueqing.Windows.Core.Models;

public sealed record ActionProgressionTarget(
    Guid OrganizationId,
    Guid StudentId,
    string StudentDisplayName,
    Guid SubjectProfileId,
    string SubjectKey,
    Guid OwnerAssignmentId,
    Guid CaseId,
    string CaseTitle,
    long CaseVersion,
    Guid PrimaryActionId,
    string PrimaryActionText,
    DateOnly? CurrentDueOn,
    long ActionVersion)
{
    public ReschedulePrimaryActionRequest CreateRescheduleRequest(
        Guid operationId,
        DateOnly? newDueOn) =>
        new(
            operationId,
            OrganizationId,
            StudentId,
            SubjectProfileId,
            OwnerAssignmentId,
            CaseId,
            PrimaryActionId,
            CaseVersion,
            ActionVersion,
            newDueOn);

    public RecordVerificationAndNextActionRequest CreateVerificationRequest(
        Guid operationId,
        VerificationOutcome outcome,
        string verificationSummary,
        string nextActionText,
        DateOnly? nextActionDueOn) =>
        new(
            operationId,
            OrganizationId,
            StudentId,
            SubjectProfileId,
            OwnerAssignmentId,
            CaseId,
            PrimaryActionId,
            CaseVersion,
            ActionVersion,
            outcome,
            verificationSummary,
            nextActionText,
            nextActionDueOn);
}

public static class ActionProgressionTargetResolver
{
    public static ActionProgressionTarget FromToday(PersonalTodayAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ValidateCommon(
            action.OrganizationId,
            action.StudentId,
            action.SubjectProfileId,
            action.AssignmentId,
            action.CaseId,
            action.CaseVersion,
            action.ActionId,
            action.ActionVersion);

        return new ActionProgressionTarget(
            action.OrganizationId,
            action.StudentId,
            action.StudentDisplayName,
            action.SubjectProfileId,
            action.SubjectKey,
            action.AssignmentId,
            action.CaseId,
            action.CaseTitle,
            action.CaseVersion,
            action.ActionId,
            action.ActionText,
            action.DueOn,
            action.ActionVersion);
    }

    public static ActionProgressionTarget FromFocus(
        StudentLearningFocusSnapshot snapshot,
        StudentLearningCaseFocus learningCase)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(learningCase);

        if (!snapshot.Cases.Any(candidate => candidate.CaseId == learningCase.CaseId))
        {
            throw new InvalidDataException(
                "Learning Case does not belong to the supplied authoritative focus snapshot.");
        }

        if (learningCase.OwnerAssignmentId != snapshot.AssignmentId)
        {
            throw new InvalidDataException(
                "Learning Case owner assignment does not match the authoritative teaching context.");
        }

        ValidateCommon(
            snapshot.OrganizationId,
            snapshot.StudentId,
            snapshot.SubjectProfileId,
            snapshot.AssignmentId,
            learningCase.CaseId,
            learningCase.Version,
            learningCase.PrimaryAction.ActionId,
            learningCase.PrimaryAction.Version);

        return new ActionProgressionTarget(
            snapshot.OrganizationId,
            snapshot.StudentId,
            snapshot.StudentDisplayName,
            snapshot.SubjectProfileId,
            snapshot.SubjectKey,
            snapshot.AssignmentId,
            learningCase.CaseId,
            learningCase.Title,
            learningCase.Version,
            learningCase.PrimaryAction.ActionId,
            learningCase.PrimaryAction.ActionText,
            learningCase.PrimaryAction.DueOn,
            learningCase.PrimaryAction.Version);
    }

    private static void ValidateCommon(
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid assignmentId,
        Guid caseId,
        long caseVersion,
        Guid actionId,
        long actionVersion)
    {
        if (organizationId == Guid.Empty ||
            studentId == Guid.Empty ||
            subjectProfileId == Guid.Empty ||
            assignmentId == Guid.Empty ||
            caseId == Guid.Empty ||
            actionId == Guid.Empty)
        {
            throw new InvalidDataException(
                "Authoritative Action progression target contains an empty identity.");
        }

        if (caseVersion <= 0 || actionVersion <= 0)
        {
            throw new InvalidDataException(
                "Authoritative Action progression target contains a non-positive version.");
        }
    }
}
