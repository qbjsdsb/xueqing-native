package com.xueqing.app.application.learning

import java.time.Instant
import java.time.LocalDate
import java.util.UUID

data class StudentLearningScope(
    val organizationId: UUID,
    val studentId: UUID,
    val subjectProfileId: UUID,
)

enum class LearningCaseState {
    New,
    Confirmed,
    Intervening,
    PendingVerification,
    Stable,
}

enum class ActionDueBucket {
    Overdue,
    Today,
    Undated,
    Future,
}

data class LearningPrimaryAction(
    val actionId: UUID,
    val actionText: String,
    val dueOn: LocalDate?,
    val dueBucket: ActionDueBucket,
    val version: Long,
)

data class StudentLearningCaseFocus(
    val caseId: UUID,
    val title: String,
    val state: LearningCaseState,
    val version: Long,
    val responsibleTeacherAppUserId: UUID,
    val ownerAssignmentId: UUID,
    val createdAtServer: Instant,
    val updatedAtServer: Instant,
    val primaryAction: LearningPrimaryAction,
)

data class StudentLearningFocusSnapshot(
    val generatedAtServer: Instant,
    val actorAppUserId: UUID,
    val organizationId: UUID,
    val organizationName: String,
    val organizationTimeZone: String,
    val organizationBusinessDate: LocalDate,
    val studentId: UUID,
    val studentDisplayName: String,
    val subjectProfileId: UUID,
    val subjectKey: String,
    val assignmentId: UUID,
    val cases: List<StudentLearningCaseFocus>,
    val hasMore: Boolean,
)

data class PersonalTodayAction(
    val organizationId: UUID,
    val organizationName: String,
    val organizationTimeZone: String,
    val organizationBusinessDate: LocalDate,
    val studentId: UUID,
    val studentDisplayName: String,
    val subjectProfileId: UUID,
    val subjectKey: String,
    val assignmentId: UUID,
    val caseId: UUID,
    val caseTitle: String,
    val caseState: LearningCaseState,
    val caseVersion: Long,
    val actionId: UUID,
    val actionText: String,
    val dueOn: LocalDate?,
    val dueBucket: ActionDueBucket,
    val actionVersion: Long,
    val caseUpdatedAtServer: Instant,
)

data class PersonalTodayActionsSnapshot(
    val generatedAtServer: Instant,
    val actorAppUserId: UUID,
    val actions: List<PersonalTodayAction>,
    val hasMore: Boolean,
)

enum class LearningReadFailureKind {
    AuthenticationRequired,
    AccessDenied,
    ServerInvariant,
    Transient,
    InvalidResponse,
}

data class LearningReadFailure(
    val kind: LearningReadFailureKind,
    val code: String,
)

sealed interface StudentLearningFocusResult {
    data class Loaded(val snapshot: StudentLearningFocusSnapshot) : StudentLearningFocusResult
    data class Failed(val failure: LearningReadFailure) : StudentLearningFocusResult
}

sealed interface PersonalTodayActionsResult {
    data class Loaded(val snapshot: PersonalTodayActionsSnapshot) : PersonalTodayActionsResult
    data class Failed(val failure: LearningReadFailure) : PersonalTodayActionsResult
}

fun interface StudentLearningFocusRemote {
    fun fetch(
        scope: StudentLearningScope,
        expectedActorAppUserId: UUID,
    ): StudentLearningFocusResult
}

fun interface PersonalTodayActionsRemote {
    fun fetch(expectedActorAppUserId: UUID): PersonalTodayActionsResult
}
