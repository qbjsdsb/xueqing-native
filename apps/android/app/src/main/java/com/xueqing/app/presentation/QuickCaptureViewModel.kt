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
import kotlinx.coroutines.Job
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
    SelectionRequired,
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
    private var availableTeachingContexts: List<PersonalTeachingContext> = emptyList()
    private var pendingSelection: PersonalTeachingContext? = null
    private var bootstrapLoaded = false
    private var submissionObservationJob: Job? = null
    private var selectionGeneration: Long = 0
    private var revision: Long = 0

    val uiState: StateFlow<QuickCaptureUiState> = _uiState.asStateFlow()

    init {
        loadTeachingContextAndDraft()
    }

    /**
     * Opens the only available teaching context for an unscoped Record action.
     * If there is more than one context, the UI must send the teacher to an
     * explicit Student/subject selection instead of guessing.
     */
    fun prepareForUnscopedCapture() {
        pendingSelection = null
        if (!bootstrapLoaded) {
            return
        }

        // Preserve login/unavailable states: an empty roster cannot be selected.
        if (availableTeachingContexts.isEmpty()) {
            return
        }
        val onlyContext = availableTeachingContexts.singleOrNull()
        if (onlyContext == null) {
            showSelectionRequired()
        } else {
            selectTeachingContext(onlyContext)
        }
    }

    /**
     * The selected context originated from the personal bootstrap, but this
     * ViewModel re-checks it against its own live bootstrap before opening the
     * draft/outbox scope. UI selection is never authorization.
     */
    fun selectTeachingContext(requested: PersonalTeachingContext) {
        pendingSelection = requested
        selectionGeneration = Math.addExact(selectionGeneration, 1)
        val generation = selectionGeneration
        if (!bootstrapLoaded) {
            return
        }

        val verified = resolveCaptureContext(requested, availableTeachingContexts)
        if (verified == null) {
            pendingSelection = null
            showSelectionRequired()
            return
        }

        // Disable editing synchronously, before a prior save/submit releases
        // the mutex. Keep the old text intact until its queued save completes.
        _uiState.update { it.copy(teachingContextStatus = TeachingContextStatus.Loading) }
        viewModelScope.launch {
            saveMutex.withLock {
                if (generation == selectionGeneration) {
                    openContextLocked(verified, generation)
                }
            }
        }
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
                    val nextEpoch = withContext(Dispatchers.IO) {
                        store.discard(currentSession)
                    }
                    if (nextEpoch == null) {
                        _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                        return@withLock
                    }

                    session = withContext(Dispatchers.IO) {
                        store.open(currentScope)
                    }
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
                val finalText = currentState.text

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
                    val nextEpoch = withContext(Dispatchers.IO) {
                        durableIntentDao.submitObservation(
                            scopeKey = currentScope.storageKey,
                            expectedEpoch = currentSession.epoch,
                            finalText = finalText,
                            updatedAtEpochMillis = capturedAtEpochMillis,
                            outbox = outbox,
                        )
                    }
                    if (nextEpoch == null) {
                        _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                        return@withLock
                    }

                    session = withContext(Dispatchers.IO) {
                        store.open(currentScope)
                    }
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
            val bootstrap = withContext(Dispatchers.IO) {
                runCatching { bootstrapRemote.fetch() }.getOrNull()
            }
            when (bootstrap) {
                is PersonalBootstrapResult.Loaded -> {
                    availableTeachingContexts = bootstrap.bootstrap.teachingContexts
                    actorAppUserId = bootstrap.bootstrap.actor.appUserId
                    bootstrapLoaded = true

                    val target = resolveCaptureContext(
                        requested = pendingSelection,
                        available = availableTeachingContexts,
                    )

                    if (target == null) {
                        if (availableTeachingContexts.isEmpty()) {
                            _uiState.update {
                                it.copy(teachingContextStatus = TeachingContextStatus.Unavailable)
                            }
                        } else {
                            showSelectionRequired()
                        }
                    } else {
                        saveMutex.withLock {
                            openContextLocked(target, selectionGeneration)
                        }
                    }
                }

                PersonalBootstrapResult.AuthenticationRequired -> {
                    bootstrapLoaded = true
                    _uiState.update {
                        it.copy(teachingContextStatus = TeachingContextStatus.AuthenticationRequired)
                    }
                }

                is PersonalBootstrapResult.AccessUnavailable,
                is PersonalBootstrapResult.UnknownResult,
                is PersonalBootstrapResult.ProtocolFailure,
                null,
                -> {
                    bootstrapLoaded = true
                    _uiState.update { it.copy(teachingContextStatus = TeachingContextStatus.Unavailable) }
                }
            }
        }
    }

    private suspend fun openContextLocked(
        context: PersonalTeachingContext,
        generation: Long,
    ) {
        val actor = actorAppUserId ?: run {
            _uiState.update { it.copy(teachingContextStatus = TeachingContextStatus.AuthenticationRequired) }
            return
        }

        submissionObservationJob?.cancel()
        session = null
        scope = null
        teachingContext = context
        pendingSelection = context
        val draftScope = DraftScope(
            environmentId = environmentId,
            appUserId = actor.toString(),
            organizationId = context.organizationId.toString(),
            studentId = context.studentId.toString(),
            subjectId = context.subjectProfileId.toString(),
            contextId = QUICK_CAPTURE_CONTEXT_ID,
        )
        scope = draftScope
        _uiState.value = QuickCaptureUiState(
            teachingContextStatus = TeachingContextStatus.Loading,
            studentDisplayName = context.studentDisplayName,
            subjectLabel = subjectLabel(context.subjectKey),
        )

        try {
            val opened = withContext(Dispatchers.IO) {
                store.open(draftScope)
            }
            // Another selection may arrive while Room opens this draft.
            if (generation != selectionGeneration) return
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
            if (generation != selectionGeneration) return
            _uiState.value = QuickCaptureUiState(
                teachingContextStatus = TeachingContextStatus.Ready,
                studentDisplayName = context.studentDisplayName,
                subjectLabel = subjectLabel(context.subjectKey),
                draftStatus = LocalDraftStatus.PersistenceFailed,
            )
        }
    }

    private fun showSelectionRequired() {
        selectionGeneration = Math.addExact(selectionGeneration, 1)
        submissionObservationJob?.cancel()
        session = null
        scope = null
        teachingContext = null
        _uiState.value = QuickCaptureUiState(
            teachingContextStatus = TeachingContextStatus.SelectionRequired,
        )
    }

    private fun observeSubmission(draftScope: DraftScope) {
        submissionObservationJob?.cancel()
        submissionObservationJob = viewModelScope.launch {
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
                    val accepted = withContext(Dispatchers.IO) {
                        store.save(currentSession, text)
                    }
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

    private fun subjectLabel(subjectKey: String): String = subjectLabelForKey(subjectKey)

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
