package com.xueqing.app.presentation

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import com.xueqing.app.application.observation.CreateObservationRequest
import com.xueqing.app.durability.DraftDatabase
import com.xueqing.app.durability.DraftScope
import com.xueqing.app.durability.DraftStore
import com.xueqing.app.durability.DurableIntentDao
import com.xueqing.app.durability.ObservationOutboxCodec
import com.xueqing.app.durability.ObservationOutboxEntity
import com.xueqing.app.durability.ObservationOutboxScheduler
import com.xueqing.app.durability.ObservationOutboxStatus
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

enum class TeachingContextStatus {
    Loading,
    Ready,
    AuthenticationRequired,
    Unavailable,
}

enum class LocalDraftStatus {
    Loading,
    Saving,
    SafeOnDevice,
    PersistenceFailed,
}

enum class ObservationSubmissionStatus {
    None,
    WaitingToSync,
    Syncing,
    WaitingToRetry,
    AuthenticationRequired,
    Accepted,
    Rejected,
}

data class QuickCaptureUiState(
    val text: String = "",
    val teachingContextStatus: TeachingContextStatus = TeachingContextStatus.Loading,
    val studentDisplayName: String = "",
    val subjectLabel: String = "",
    val draftStatus: LocalDraftStatus = LocalDraftStatus.Loading,
    val recoveredFromDisk: Boolean = false,
    val submissionStatus: ObservationSubmissionStatus = ObservationSubmissionStatus.None,
    val submissionErrorCode: String? = null,
)

class QuickCaptureViewModel(
    private val store: DraftStore,
    private val durableIntentDao: DurableIntentDao,
    private val bootstrapRemote: PersonalBootstrapRemote,
    private val environmentId: String,
    private val onOutboxCommitted: () -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
    private val operationIdFactory: () -> UUID = UUID::randomUUID,
) : ViewModel() {
    private val saveMutex = Mutex()
    private val _uiState = MutableStateFlow(QuickCaptureUiState())
    private var session: DraftStore.Session? = null
    private var scope: DraftScope? = null
    private var teachingContext: PersonalTeachingContext? = null
    private var actorAppUserId: UUID? = null
    private var revision: Long = 0

    val uiState: StateFlow<QuickCaptureUiState> = _uiState.asStateFlow()

    init {
        loadTeachingContextAndDraft()
    }

    fun onTextChanged(text: String) {
        val current = _uiState.value
        if (
            current.teachingContextStatus != TeachingContextStatus.Ready ||
            current.draftStatus == LocalDraftStatus.Loading
        ) {
            return
        }

        revision = Math.addExact(revision, 1)
        val targetRevision = revision
        _uiState.update {
            it.copy(
                text = text,
                draftStatus = LocalDraftStatus.Saving,
                recoveredFromDisk = false,
            )
        }
        persist(text = text, targetRevision = targetRevision)
    }

    fun flushNow() {
        val current = _uiState.value
        if (
            session == null ||
            current.teachingContextStatus != TeachingContextStatus.Ready ||
            current.draftStatus == LocalDraftStatus.Loading
        ) {
            return
        }

        revision = Math.addExact(revision, 1)
        persist(
            text = current.text,
            targetRevision = revision,
        )
    }

    fun discard() {
        viewModelScope.launch {
            saveMutex.withLock {
                val currentSession = session ?: return@withLock
                val currentScope = scope ?: return@withLock
                try {
                    val nextEpoch = store.discard(currentSession)
                    if (nextEpoch == null) {
                        _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                        return@withLock
                    }

                    session = store.open(currentScope)
                    revision = Math.addExact(revision, 1)
                    _uiState.update {
                        it.copy(
                            text = "",
                            draftStatus = LocalDraftStatus.SafeOnDevice,
                            recoveredFromDisk = false,
                        )
                    }
                } catch (_: Throwable) {
                    _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                }
            }
        }
    }

    fun submit() {
        viewModelScope.launch {
            saveMutex.withLock {
                val currentState = _uiState.value
                val currentSession = session ?: return@withLock
                val currentScope = scope ?: return@withLock
                val currentContext = teachingContext ?: return@withLock
                val currentActor = actorAppUserId ?: return@withLock
                val finalText = currentState.text.trim()

                if (
                    currentState.teachingContextStatus != TeachingContextStatus.Ready ||
                    currentState.draftStatus == LocalDraftStatus.PersistenceFailed ||
                    finalText.isBlank()
                ) {
                    return@withLock
                }

                // Invalidate every autosave queued before this submit barrier.
                revision = Math.addExact(revision, 1)
                val capturedAtEpochMillis = clock()
                val operationId = operationIdFactory()
                val request = CreateObservationRequest(
                    operationId = operationId,
                    organizationId = currentContext.organizationId,
                    studentId = currentContext.studentId,
                    subjectProfileId = currentContext.subjectProfileId,
                    assignmentId = currentContext.assignmentId,
                    rawText = finalText,
                    clientCapturedAt = Instant.ofEpochMilli(capturedAtEpochMillis),
                    clientCaptureMetadata = mapOf("surface" to "quick_capture"),
                )
                val outbox = ObservationOutboxEntity(
                    operationId = operationId.toString(),
                    commandType = ObservationOutboxCodec.COMMAND_TYPE,
                    scopeKey = currentScope.storageKey,
                    environmentId = currentScope.environmentId,
                    appUserId = currentActor.toString(),
                    organizationId = currentContext.organizationId.toString(),
                    payloadJson = ObservationOutboxCodec.encodeRequest(request),
                    queueStatus = ObservationOutboxStatus.Pending,
                    attemptCount = 0,
                    lastErrorClass = null,
                    nextAttemptAtEpochMillis = capturedAtEpochMillis,
                    leaseId = null,
                    leaseExpiresAtEpochMillis = null,
                    acknowledgedAtEpochMillis = null,
                    serverReceiptJson = null,
                    createdAtEpochMillis = capturedAtEpochMillis,
                )

                try {
                    val nextEpoch = durableIntentDao.submitObservation(
                        scopeKey = currentScope.storageKey,
                        expectedEpoch = currentSession.epoch,
                        finalText = finalText,
                        updatedAtEpochMillis = capturedAtEpochMillis,
                        outbox = outbox,
                    )
                    if (nextEpoch == null) {
                        _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                        return@withLock
                    }

                    session = store.open(currentScope)
                    _uiState.update {
                        it.copy(
                            text = "",
                            draftStatus = LocalDraftStatus.SafeOnDevice,
                            recoveredFromDisk = false,
                        )
                    }
                    onOutboxCommitted()
                } catch (_: Throwable) {
                    // The Room transaction rolls back both Draft retirement and
                    // Outbox insertion. Keep current UI text visible as well.
                    _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                }
            }
        }
    }

    private fun loadTeachingContextAndDraft() {
        viewModelScope.launch {
            val bootstrap = withContext(Dispatchers.IO) { bootstrapRemote.fetch() }
            when (bootstrap) {
                is PersonalBootstrapResult.Loaded -> {
                    val context = bootstrap.bootstrap.teachingContexts.firstOrNull()
                    if (context == null) {
                        _uiState.update { it.copy(teachingContextStatus = TeachingContextStatus.Unavailable) }
                        return@launch
                    }
                    val draftScope = DraftScope(
                        environmentId = environmentId,
                        appUserId = bootstrap.bootstrap.actor.appUserId.toString(),
                        organizationId = context.organizationId.toString(),
                        studentId = context.studentId.toString(),
                        subjectId = context.subjectProfileId.toString(),
                        contextId = QUICK_CAPTURE_CONTEXT_ID,
                    )

                    try {
                        val opened = store.open(draftScope)
                        actorAppUserId = bootstrap.bootstrap.actor.appUserId
                        teachingContext = context
                        scope = draftScope
                        session = opened
                        _uiState.value = QuickCaptureUiState(
                            text = opened.recovered?.text.orEmpty(),
                            teachingContextStatus = TeachingContextStatus.Ready,
                            studentDisplayName = context.studentDisplayName,
                            subjectLabel = subjectLabel(context.subjectKey),
                            draftStatus = LocalDraftStatus.SafeOnDevice,
                            recoveredFromDisk = opened.recovered != null,
                        )
                        observeSubmission(draftScope)
                    } catch (_: Throwable) {
                        _uiState.update {
                            it.copy(
                                teachingContextStatus = TeachingContextStatus.Ready,
                                studentDisplayName = context.studentDisplayName,
                                subjectLabel = subjectLabel(context.subjectKey),
                                draftStatus = LocalDraftStatus.PersistenceFailed,
                            )
                        }
                    }
                }

                PersonalBootstrapResult.AuthenticationRequired -> {
                    _uiState.update {
                        it.copy(teachingContextStatus = TeachingContextStatus.AuthenticationRequired)
                    }
                }

                is PersonalBootstrapResult.AccessUnavailable,
                is PersonalBootstrapResult.UnknownResult,
                is PersonalBootstrapResult.ProtocolFailure,
                -> {
                    _uiState.update { it.copy(teachingContextStatus = TeachingContextStatus.Unavailable) }
                }
            }
        }
    }

    private fun observeSubmission(draftScope: DraftScope) {
        viewModelScope.launch {
            durableIntentDao.observeLatestForScope(draftScope.storageKey).collectLatest { row ->
                val status = when (row?.queueStatus) {
                    null -> ObservationSubmissionStatus.None
                    ObservationOutboxStatus.Pending -> ObservationSubmissionStatus.WaitingToSync
                    ObservationOutboxStatus.InFlight -> ObservationSubmissionStatus.Syncing
                    ObservationOutboxStatus.Retry -> if (row.lastErrorClass == "AuthenticationRequired") {
                        ObservationSubmissionStatus.AuthenticationRequired
                    } else {
                        ObservationSubmissionStatus.WaitingToRetry
                    }
                    ObservationOutboxStatus.Acknowledged -> ObservationSubmissionStatus.Accepted
                    ObservationOutboxStatus.DeadLetter -> ObservationSubmissionStatus.Rejected
                    else -> ObservationSubmissionStatus.Rejected
                }
                _uiState.update {
                    it.copy(
                        submissionStatus = status,
                        submissionErrorCode = row?.lastErrorClass,
                    )
                }
            }
        }
    }

    private fun persist(
        text: String,
        targetRevision: Long,
    ) {
        val scheduledSession = session

        viewModelScope.launch {
            saveMutex.withLock {
                if (targetRevision != revision) {
                    return@withLock
                }

                val currentSession = scheduledSession ?: run {
                    if (targetRevision == revision) {
                        _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                    }
                    return@withLock
                }

                try {
                    val accepted = store.save(currentSession, text)
                    if (targetRevision == revision) {
                        _uiState.update {
                            it.copy(
                                draftStatus = if (accepted) {
                                    LocalDraftStatus.SafeOnDevice
                                } else {
                                    LocalDraftStatus.PersistenceFailed
                                },
                            )
                        }
                    }
                } catch (_: Throwable) {
                    if (targetRevision == revision) {
                        _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                    }
                }
            }
        }
    }

    private fun subjectLabel(subjectKey: String): String = when (subjectKey) {
        "chinese" -> "语文"
        else -> subjectKey
    }

    companion object {
        const val QUICK_CAPTURE_CONTEXT_ID = "quick-capture"

        fun create(
            context: Context,
            bootstrapRemote: PersonalBootstrapRemote,
            environmentId: String,
        ): QuickCaptureViewModel {
            val appContext = context.applicationContext
            val database = DraftDatabase.get(appContext)
            return QuickCaptureViewModel(
                store = DraftStore(database.draftDao()),
                durableIntentDao = database.durableIntentDao(),
                bootstrapRemote = bootstrapRemote,
                environmentId = environmentId,
                onOutboxCommitted = { ObservationOutboxScheduler.kick(appContext) },
            )
        }
    }
}
