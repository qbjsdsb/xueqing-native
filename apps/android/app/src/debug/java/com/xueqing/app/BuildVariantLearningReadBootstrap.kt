package com.xueqing.app

import com.xueqing.app.application.learning.ActionDueBucket
import com.xueqing.app.application.learning.LearningCaseState
import com.xueqing.app.application.learning.LearningPrimaryAction
import com.xueqing.app.application.learning.LearningReadFailure
import com.xueqing.app.application.learning.LearningReadFailureKind
import com.xueqing.app.application.learning.PersonalTodayAction
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.PersonalTodayActionsResult
import com.xueqing.app.application.learning.PersonalTodayActionsSnapshot
import com.xueqing.app.application.learning.StudentLearningCaseFocus
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.learning.StudentLearningFocusResult
import com.xueqing.app.application.learning.StudentLearningFocusSnapshot
import java.time.Instant
import java.time.LocalDate
import java.util.UUID

/**
 * Debug-only fictional Learning read composition. It exercises the real Android
 * presentation path without embedding credentials or claiming production auth.
 */
object BuildVariantLearningReadBootstrap {
    private val actorId = uuid("10000000-0000-0000-0000-000000000001")
    private val organizationId = uuid("20000000-0000-0000-0000-000000000001")
    private val studentId = uuid("30000000-0000-0000-0000-000000000001")
    private val subjectProfileId = uuid("40000000-0000-0000-0000-000000000001")
    private val assignmentId = uuid("50000000-0000-0000-0000-000000000001")
    private val caseId = uuid("60000000-0000-0000-0000-000000000001")
    private val actionId = uuid("70000000-0000-0000-0000-000000000001")
    private val businessDate = LocalDate.parse("2026-09-20")

    fun todayRemote(): PersonalTodayActionsRemote = PersonalTodayActionsRemote { expectedActor ->
        if (expectedActor != actorId) {
            return@PersonalTodayActionsRemote PersonalTodayActionsResult.Failed(
                LearningReadFailure(
                    LearningReadFailureKind.AuthenticationRequired,
                    "XQ_CLIENT_ACTOR_REQUIRED",
                ),
            )
        }

        PersonalTodayActionsResult.Loaded(
            PersonalTodayActionsSnapshot(
                generatedAtServer = Instant.parse("2026-09-20T10:30:00Z"),
                actorAppUserId = actorId,
                actions = listOf(
                    PersonalTodayAction(
                        organizationId = organizationId,
                        organizationName = "虚构机构甲",
                        organizationTimeZone = "Asia/Shanghai",
                        organizationBusinessDate = businessDate,
                        studentId = studentId,
                        studentDisplayName = "虚构学生甲",
                        subjectProfileId = subjectProfileId,
                        subjectKey = "chinese",
                        assignmentId = assignmentId,
                        caseId = caseId,
                        caseTitle = "跨段概括仍会漏掉限制条件",
                        caseState = LearningCaseState.Intervening,
                        caseVersion = 3,
                        actionId = actionId,
                        actionText = "复核陌生材料中的限制条件",
                        dueOn = businessDate,
                        dueBucket = ActionDueBucket.Today,
                        actionVersion = 2,
                        caseUpdatedAtServer = Instant.parse("2026-09-20T10:00:00Z"),
                    ),
                ),
                hasMore = false,
            ),
        )
    }

    fun focusRemote(): StudentLearningFocusRemote = StudentLearningFocusRemote {
            scope,
            expectedActor,
        ->
        if (
            expectedActor != actorId ||
            scope.organizationId != organizationId ||
            scope.studentId != studentId ||
            scope.subjectProfileId != subjectProfileId
        ) {
            return@StudentLearningFocusRemote StudentLearningFocusResult.Failed(
                LearningReadFailure(
                    LearningReadFailureKind.AccessDenied,
                    "XQ_TEACHING_CONTEXT_UNAVAILABLE",
                ),
            )
        }

        StudentLearningFocusResult.Loaded(
            StudentLearningFocusSnapshot(
                generatedAtServer = Instant.parse("2026-09-20T10:30:00Z"),
                actorAppUserId = actorId,
                organizationId = organizationId,
                organizationName = "虚构机构甲",
                organizationTimeZone = "Asia/Shanghai",
                organizationBusinessDate = businessDate,
                studentId = studentId,
                studentDisplayName = "虚构学生甲",
                subjectProfileId = subjectProfileId,
                subjectKey = "chinese",
                assignmentId = assignmentId,
                cases = listOf(
                    StudentLearningCaseFocus(
                        caseId = caseId,
                        title = "跨段概括仍会漏掉限制条件",
                        state = LearningCaseState.Intervening,
                        version = 3,
                        responsibleTeacherAppUserId = actorId,
                        ownerAssignmentId = assignmentId,
                        createdAtServer = Instant.parse("2026-09-18T08:00:00Z"),
                        updatedAtServer = Instant.parse("2026-09-20T10:00:00Z"),
                        primaryAction = LearningPrimaryAction(
                            actionId = actionId,
                            actionText = "复核陌生材料中的限制条件",
                            dueOn = businessDate,
                            dueBucket = ActionDueBucket.Today,
                            version = 2,
                        ),
                    ),
                ),
                hasMore = false,
            ),
        )
    }

    private fun uuid(value: String): UUID = UUID.fromString(value)
}
