package com.xueqing.app.presentation

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import com.xueqing.app.application.learning.LearningReadFailureKind
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.PersonalTodayActionsResult
import com.xueqing.app.application.learning.PersonalTodayActionsSnapshot
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.learning.StudentLearningFocusResult
import com.xueqing.app.application.learning.StudentLearningFocusSnapshot
import com.xueqing.app.application.learning.StudentLearningScope
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

enum class LearningReadStatus {
    Idle,
    Loading,
    Empty,
    Data,
    AuthenticationRequired,
    AccessDenied,
    ServerInvariant,
    TransientFailure,
    ProtocolFailure,
}

data class TodayLearningUiState(
    val generation: Long = 0,
    val actorAppUserId: UUID? = null,
    val status: LearningReadStatus = LearningReadStatus.Idle,
    val snapshot: PersonalTodayActionsSnapshot? = null,
    val failureCode: String? = null,
)

data class FocusLearningUiState(
    val generation: Long = 0,
    val actorAppUserId: UUID? = null,
    val scope: StudentLearningScope? = null,
    val ownerAssignmentId: UUID? = null,
    val status: LearningReadStatus = LearningReadStatus.Idle,
    val snapshot: StudentLearningFocusSnapshot? = null,
    val failureCode: String? = null,
)

data class LearningReadUiState(
    val today: TodayLearningUiState = TodayLearningUiState(),
    val focus: FocusLearningUiState = FocusLearningUiState(),
)

class LearningReadViewModel(
    private val todayRemote: PersonalTodayActionsRemote,
    private val focusRemote: StudentLearningFocusRemote,
) : ViewModel() {
    private val _uiState = MutableStateFlow(LearningReadUiState())
    val uiState: StateFlow<LearningReadUiState> = _uiState.asStateFlow()

    private var todayGeneration = 0L
    private var focusGeneration = 0L

    fun refreshToday(
        expectedActorAppUserId: UUID,
        force: Boolean = false,
    ) {
        val current = _uiState.value.today
        if (
            !force &&
            current.actorAppUserId == expectedActorAppUserId &&
            current.status in setOf(
                LearningReadStatus.Loading,
                LearningReadStatus.Empty,
                LearningReadStatus.Data,
            )
        ) {
            return
        }

        val generation = ++todayGeneration
        _uiState.value = _uiState.value.copy(
            today = TodayLearningUiState(
                generation = generation,
                actorAppUserId = expectedActorAppUserId,
                status = LearningReadStatus.Loading,
            ),
        )

        viewModelScope.launch {
            val result = withContext(Dispatchers.IO) {
                runCatching { todayRemote.fetch(expectedActorAppUserId) }.getOrNull()
            }

            if (generation != todayGeneration) return@launch
            _uiState.value = _uiState.value.copy(
                today = mapTodayResult(
                    generation = generation,
                    actorAppUserId = expectedActorAppUserId,
                    result = result,
                ),
            )
        }
    }

    fun loadFocus(
        context: PersonalTeachingContext,
        expectedActorAppUserId: UUID,
        force: Boolean = false,
    ) {
        val scope = StudentLearningScope(
            organizationId = context.organizationId,
            studentId = context.studentId,
            subjectProfileId = context.subjectProfileId,
        )
        val current = _uiState.value.focus
        if (
            !force &&
            current.actorAppUserId == expectedActorAppUserId &&
            current.scope == scope &&
            current.ownerAssignmentId == context.assignmentId &&
            current.status in setOf(
                LearningReadStatus.Loading,
                LearningReadStatus.Empty,
                LearningReadStatus.Data,
            )
        ) {
            return
        }

        val generation = ++focusGeneration
        _uiState.value = _uiState.value.copy(
            focus = FocusLearningUiState(
                generation = generation,
                actorAppUserId = expectedActorAppUserId,
                scope = scope,
                ownerAssignmentId = context.assignmentId,
                status = LearningReadStatus.Loading,
            ),
        )

        viewModelScope.launch {
            val result = withContext(Dispatchers.IO) {
                runCatching { focusRemote.fetch(scope, expectedActorAppUserId) }.getOrNull()
            }

            if (generation != focusGeneration) return@launch
            _uiState.value = _uiState.value.copy(
                focus = mapFocusResult(
                    generation = generation,
                    expectedActorAppUserId = expectedActorAppUserId,
                    context = context,
                    result = result,
                ),
            )
        }
    }

    fun clearFocus() {
        focusGeneration++
        _uiState.value = _uiState.value.copy(
            focus = FocusLearningUiState(generation = focusGeneration),
        )
    }

    fun clearAll() {
        todayGeneration++
        focusGeneration++
        _uiState.value = LearningReadUiState(
            today = TodayLearningUiState(generation = todayGeneration),
            focus = FocusLearningUiState(generation = focusGeneration),
        )
    }

    private fun mapTodayResult(
        generation: Long,
        actorAppUserId: UUID,
        result: PersonalTodayActionsResult?,
    ): TodayLearningUiState = when (result) {
        is PersonalTodayActionsResult.Loaded -> TodayLearningUiState(
            generation = generation,
            actorAppUserId = actorAppUserId,
            status = if (result.snapshot.actions.isEmpty()) {
                LearningReadStatus.Empty
            } else {
                LearningReadStatus.Data
            },
            snapshot = result.snapshot,
        )

        is PersonalTodayActionsResult.Failed -> TodayLearningUiState(
            generation = generation,
            actorAppUserId = actorAppUserId,
            status = result.failure.kind.toUiStatus(),
            failureCode = result.failure.code,
        )

        null -> TodayLearningUiState(
            generation = generation,
            actorAppUserId = actorAppUserId,
            status = LearningReadStatus.TransientFailure,
            failureCode = "XQ_CLIENT_LEARNING_READ_FAILED",
        )
    }

    private fun mapFocusResult(
        generation: Long,
        expectedActorAppUserId: UUID,
        context: PersonalTeachingContext,
        result: StudentLearningFocusResult?,
    ): FocusLearningUiState {
        val scope = StudentLearningScope(
            context.organizationId,
            context.studentId,
            context.subjectProfileId,
        )

        if (result is StudentLearningFocusResult.Loaded) {
            if (result.snapshot.assignmentId != context.assignmentId) {
                return FocusLearningUiState(
                    generation = generation,
                    actorAppUserId = expectedActorAppUserId,
                    scope = scope,
                    ownerAssignmentId = context.assignmentId,
                    status = LearningReadStatus.AccessDenied,
                    failureCode = "XQ_CLIENT_ASSIGNMENT_STALE",
                )
            }

            return FocusLearningUiState(
                generation = generation,
                actorAppUserId = expectedActorAppUserId,
                scope = scope,
                ownerAssignmentId = context.assignmentId,
                status = if (result.snapshot.cases.isEmpty()) {
                    LearningReadStatus.Empty
                } else {
                    LearningReadStatus.Data
                },
                snapshot = result.snapshot,
            )
        }

        if (result is StudentLearningFocusResult.Failed) {
            return FocusLearningUiState(
                generation = generation,
                actorAppUserId = expectedActorAppUserId,
                scope = scope,
                ownerAssignmentId = context.assignmentId,
                status = result.failure.kind.toUiStatus(),
                failureCode = result.failure.code,
            )
        }

        return FocusLearningUiState(
            generation = generation,
            actorAppUserId = expectedActorAppUserId,
            scope = scope,
            ownerAssignmentId = context.assignmentId,
            status = LearningReadStatus.TransientFailure,
            failureCode = "XQ_CLIENT_LEARNING_READ_FAILED",
        )
    }

    private fun LearningReadFailureKind.toUiStatus(): LearningReadStatus = when (this) {
        LearningReadFailureKind.AuthenticationRequired ->
            LearningReadStatus.AuthenticationRequired

        LearningReadFailureKind.AccessDenied ->
            LearningReadStatus.AccessDenied

        LearningReadFailureKind.ServerInvariant ->
            LearningReadStatus.ServerInvariant

        LearningReadFailureKind.Transient ->
            LearningReadStatus.TransientFailure

        LearningReadFailureKind.InvalidResponse ->
            LearningReadStatus.ProtocolFailure
    }
}
