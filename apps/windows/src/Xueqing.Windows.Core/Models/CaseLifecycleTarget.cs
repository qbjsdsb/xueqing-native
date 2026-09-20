namespace Xueqing.Windows.Core.Models;

public sealed record CaseLifecycleTarget(
    Guid OrganizationId,
    Guid StudentId,
    string StudentDisplayName,
    Guid SubjectProfileId,
    string SubjectKey,
    Guid OwnerAssignmentId,
    Guid ResponsibleTeacherAppUserId,
    Guid CaseId,
    string CaseTitle,
    LearningCaseState CaseState,
    long CaseVersion,
    LearningPrimaryAction? PrimaryAction)
{
    public TransitionLearningCaseStateRequest CreateTransitionRequest(
        Guid operationId,
        LearningCaseState targetState) =>
        new(
            operationId,
            OrganizationId,
            StudentId,
            SubjectProfileId,
            OwnerAssignmentId,
            CaseId,
            CaseVersion,
            targetState);

    public CloseLearningCaseRequest CreateCloseRequest(Guid operationId)
    {
        var action = PrimaryAction ?? throw new InvalidOperationException(
            "Closing a stable Case requires the authoritative pending primary Action.");

        return new CloseLearningCaseRequest(
            operationId,
            OrganizationId,
            StudentId,
            SubjectProfileId,
            OwnerAssignmentId,
            CaseId,
            action.ActionId,
            CaseVersion,
            action.Version);
    }

    public ReopenLearningCaseRequest CreateReopenRequest(
        Guid operationId,
        string newPrimaryActionText,
        DateOnly? newPrimaryActionDueOn) =>
        new(
            operationId,
            OrganizationId,
            StudentId,
            SubjectProfileId,
            OwnerAssignmentId,
            CaseId,
            CaseVersion,
            newPrimaryActionText,
            newPrimaryActionDueOn);
}

public static class CaseLifecycleTargetResolver
{
    public static CaseLifecycleTarget FromHistory(
        StudentLearningCasesSnapshot snapshot,
        StudentLearningCaseSummary learningCase)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(learningCase);

        if (!snapshot.Cases.Any(candidate => candidate.CaseId == learningCase.CaseId))
        {
            throw new InvalidDataException(
                "Learning Case does not belong to the supplied authoritative history snapshot.");
        }

        if (!learningCase.IsCurrentActorResponsibility ||
            learningCase.OwnerAssignmentId != snapshot.AssignmentId ||
            learningCase.ResponsibleTeacherAppUserId != snapshot.ActorAppUserId)
        {
            throw new InvalidDataException(
                "Historical Case is readable but is not current-actor lifecycle responsibility.");
        }

        ValidateIdentityAndVersion(
            snapshot.OrganizationId,
            snapshot.StudentId,
            snapshot.SubjectProfileId,
            snapshot.AssignmentId,
            snapshot.ActorAppUserId,
            learningCase.CaseId,
            learningCase.Version);

        if (learningCase.State == LearningCaseState.Closed)
        {
            if (learningCase.PrimaryAction is not null)
            {
                throw new InvalidDataException(
                    "Closed Learning Case cannot expose a pending primary Action.");
            }
        }
        else
        {
            var action = learningCase.PrimaryAction ?? throw new InvalidDataException(
                "Open Learning Case must expose one pending primary Action.");

            if (action.ActionId == Guid.Empty || action.Version <= 0)
            {
                throw new InvalidDataException(
                    "Authoritative Learning Case primary Action is invalid.");
            }
        }

        return new CaseLifecycleTarget(
            snapshot.OrganizationId,
            snapshot.StudentId,
            snapshot.StudentDisplayName,
            snapshot.SubjectProfileId,
            snapshot.SubjectKey,
            snapshot.AssignmentId,
            learningCase.ResponsibleTeacherAppUserId,
            learningCase.CaseId,
            learningCase.Title,
            learningCase.State,
            learningCase.Version,
            learningCase.PrimaryAction);
    }

    private static void ValidateIdentityAndVersion(
        Guid organizationId,
        Guid studentId,
        Guid subjectProfileId,
        Guid assignmentId,
        Guid actorAppUserId,
        Guid caseId,
        long caseVersion)
    {
        if (organizationId == Guid.Empty ||
            studentId == Guid.Empty ||
            subjectProfileId == Guid.Empty ||
            assignmentId == Guid.Empty ||
            actorAppUserId == Guid.Empty ||
            caseId == Guid.Empty)
        {
            throw new InvalidDataException(
                "Authoritative Case lifecycle target contains an empty identity.");
        }

        if (caseVersion <= 0)
        {
            throw new InvalidDataException(
                "Authoritative Case lifecycle target contains a non-positive Case version.");
        }
    }
}
