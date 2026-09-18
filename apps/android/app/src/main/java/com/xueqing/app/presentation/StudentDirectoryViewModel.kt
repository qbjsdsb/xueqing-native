package com.xueqing.app.presentation

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

enum class StudentDirectoryStatus {
    Loading,
    Ready,
    AuthenticationRequired,
    Unavailable,
}

data class StudentDirectoryItem(
    val key: String,
    val organizationName: String,
    val studentDisplayName: String,
    val contexts: List<PersonalTeachingContext>,
) {
    val subjectSummary: String
        get() = contexts
            .map { subjectLabelForKey(it.subjectKey) }
            .distinct()
            .joinToString(" · ")
}

data class StudentDirectoryUiState(
    val status: StudentDirectoryStatus = StudentDirectoryStatus.Loading,
    val students: List<StudentDirectoryItem> = emptyList(),
)

internal fun subjectLabelForKey(subjectKey: String): String = when (subjectKey) {
    "chinese" -> "语文"
    "math" -> "数学"
    "english" -> "英语"
    "physics" -> "物理"
    "chemistry" -> "化学"
    else -> subjectKey
}

/**
 * Builds the personal teaching roster from the server-authoritative bootstrap.
 *
 * The organization is part of the key on purpose: the same display name in
 * two organizations must never be presented as one student scope.
 */
internal fun buildStudentDirectory(
    bootstrap: PersonalBootstrap,
): List<StudentDirectoryItem> {
    val organizationNames = bootstrap.organizations.associate { it.organizationId to it.name }

    return bootstrap.teachingContexts
        .groupBy { TeachingStudentKey(it.organizationId, it.studentId) }
        .map { (key, contexts) ->
            val orderedContexts = contexts.sortedWith(
                compareBy<PersonalTeachingContext>(
                    { it.subjectKey },
                    { it.subjectProfileId.toString() },
                ),
            )
            StudentDirectoryItem(
                key = key.storageKey,
                organizationName = organizationNames[key.organizationId].orEmpty()
                    .ifBlank { "未命名机构" },
                studentDisplayName = orderedContexts.first().studentDisplayName,
                contexts = orderedContexts,
            )
        }
        .sortedWith(
            compareBy<StudentDirectoryItem>(
                { it.studentDisplayName },
                { it.organizationName },
                { it.key },
            ),
        )
}

private data class TeachingStudentKey(
    val organizationId: UUID,
    val studentId: UUID,
) {
    val storageKey: String
        get() = "$organizationId:$studentId"
}

class StudentDirectoryViewModel(
    private val bootstrapRemote: PersonalBootstrapRemote,
) : ViewModel() {
    private val _uiState = MutableStateFlow(StudentDirectoryUiState())
    val uiState: StateFlow<StudentDirectoryUiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch {
            val result = withContext(Dispatchers.IO) {
                runCatching { bootstrapRemote.fetch() }.getOrNull()
            }
            _uiState.value = when (result) {
                is PersonalBootstrapResult.Loaded -> StudentDirectoryUiState(
                    status = StudentDirectoryStatus.Ready,
                    students = buildStudentDirectory(result.bootstrap),
                )

                PersonalBootstrapResult.AuthenticationRequired ->
                    StudentDirectoryUiState(status = StudentDirectoryStatus.AuthenticationRequired)

                is PersonalBootstrapResult.AccessUnavailable,
                is PersonalBootstrapResult.UnknownResult,
                is PersonalBootstrapResult.ProtocolFailure,
                null,
                -> StudentDirectoryUiState(status = StudentDirectoryStatus.Unavailable)
            }
        }
    }
}
