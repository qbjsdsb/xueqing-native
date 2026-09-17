package com.xueqing.app.presentation

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.xueqing.app.durability.DraftDatabase
import com.xueqing.app.durability.DraftScope
import com.xueqing.app.durability.DraftStore
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

enum class LocalDraftStatus {
    Loading,
    Saving,
    SafeOnDevice,
    PersistenceFailed,
}

data class QuickCaptureUiState(
    val text: String = "",
    val status: LocalDraftStatus = LocalDraftStatus.Loading,
    val recoveredFromDisk: Boolean = false,
)

class QuickCaptureViewModel(
    private val store: DraftStore,
    private val scope: DraftScope = FIXTURE_SCOPE,
) : ViewModel() {
    private val saveMutex = Mutex()
    private val _uiState = MutableStateFlow(QuickCaptureUiState())
    private var session: DraftStore.Session? = null
    private var revision: Long = 0

    val uiState: StateFlow<QuickCaptureUiState> = _uiState.asStateFlow()

    init {
        viewModelScope.launch {
            try {
                val opened = store.open(scope)
                session = opened
                _uiState.value = QuickCaptureUiState(
                    text = opened.recovered?.text.orEmpty(),
                    status = LocalDraftStatus.SafeOnDevice,
                    recoveredFromDisk = opened.recovered != null,
                )
            } catch (_: Throwable) {
                _uiState.update { current -> current.copy(status = LocalDraftStatus.PersistenceFailed) }
            }
        }
    }

    fun onTextChanged(text: String) {
        if (_uiState.value.status == LocalDraftStatus.Loading) {
            return
        }

        revision = Math.addExact(revision, 1)
        val targetRevision = revision
        _uiState.update { current ->
            current.copy(
                text = text,
                status = LocalDraftStatus.Saving,
                recoveredFromDisk = false,
            )
        }
        persist(text = text, targetRevision = targetRevision)
    }

    fun flushNow() {
        val current = _uiState.value
        if (current.status == LocalDraftStatus.Loading) {
            return
        }

        revision = Math.addExact(revision, 1)
        persist(
            text = current.text,
            targetRevision = revision,
            force = true,
        )
    }

    fun discard() {
        viewModelScope.launch {
            saveMutex.withLock {
                val currentSession = session ?: return@withLock
                try {
                    val nextEpoch = store.discard(currentSession)
                    if (nextEpoch == null) {
                        _uiState.update { current -> current.copy(status = LocalDraftStatus.PersistenceFailed) }
                        return@withLock
                    }

                    session = store.open(scope)
                    revision = Math.addExact(revision, 1)
                    _uiState.value = QuickCaptureUiState(
                        text = "",
                        status = LocalDraftStatus.SafeOnDevice,
                        recoveredFromDisk = false,
                    )
                } catch (_: Throwable) {
                    _uiState.update { current -> current.copy(status = LocalDraftStatus.PersistenceFailed) }
                }
            }
        }
    }

    private fun persist(
        text: String,
        targetRevision: Long,
        force: Boolean = false,
    ) {
        viewModelScope.launch {
            saveMutex.withLock {
                if (!force && targetRevision != revision) {
                    return@withLock
                }

                val currentSession = session ?: run {
                    if (targetRevision == revision) {
                        _uiState.update { current -> current.copy(status = LocalDraftStatus.PersistenceFailed) }
                    }
                    return@withLock
                }

                try {
                    val accepted = store.save(currentSession, text)
                    if (targetRevision == revision) {
                        _uiState.update { current ->
                            current.copy(
                                status = if (accepted) {
                                    LocalDraftStatus.SafeOnDevice
                                } else {
                                    LocalDraftStatus.PersistenceFailed
                                },
                            )
                        }
                    }
                } catch (_: Throwable) {
                    if (targetRevision == revision) {
                        _uiState.update { current -> current.copy(status = LocalDraftStatus.PersistenceFailed) }
                    }
                }
            }
        }
    }

    companion object {
        val FIXTURE_SCOPE = DraftScope(
            environmentId = "native-spike",
            appUserId = "fixture-teacher-001",
            organizationId = "fixture-org-001",
            studentId = "fixture-student-lin-chen",
            subjectId = "subject-chinese",
            contextId = "quick-capture",
        )

        fun create(context: Context): QuickCaptureViewModel {
            val database = DraftDatabase.get(context.applicationContext)
            return QuickCaptureViewModel(DraftStore(database.draftDao()))
        }
    }
}
