package com.xueqing.app.presentation

import android.content.Context
import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import com.xueqing.app.application.observation.CreateObservationRequest
import com.xueqing.app.durability.AttachmentStagingRecoveryScheduler
import com.xueqing.app.durability.AttachmentStagingState
import com.xueqing.app.durability.AttachmentStagingStore
import com.xueqing.app.durability.AttachmentTooLargeException
import com.xueqing.app.durability.DraftDatabase
import com.xueqing.app.durability.DraftScope
import com.xueqing.app.durability.DraftStore
import com.xueqing.app.durability.DurableIntentDao
import com.xueqing.app.durability.ObservationOutboxCodec
import com.xueqing.app.durability.ObservationOutboxEntity
import com.xueqing.app.durability.ObservationOutboxScheduler
import com.xueqing.app.durability.LocalAttachmentKeyUnavailableException
import com.xueqing.app.durability.ObservationOutboxStatus
import com.xueqing.app.durability.ProtectedAttachmentFileStore
import com.xueqing.app.infrastructure.media.ImageDerivativeFactory
import com.xueqing.app.infrastructure.media.ImageDerivativeTooLargeException
import com.xueqing.app.infrastructure.media.PhotoAttachmentStager
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

enum class AttachmentDraftStatus {
    None,
    Protecting,
    SafeOnDevice,
    Failed,
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
    val attachmentStatus: AttachmentDraftStatus = AttachmentDraftStatus.None,
    val attachmentCount: Int = 0,
    val attachmentErrorCode: String? = null,
    val submissionStatus: ObservationSubmissionStatus = ObservationSubmissionStatus.None,
    val submissionErrorCode: String? = null,
)

class QuickCaptureViewModel(
    private val store: DraftStore,
    private val durableIntentDao: DurableIntentDao,
    private val attachmentStagingStore: AttachmentStagingStore,
    private val photoAttachmentStager: PhotoAttachmentStager,
    private val bootstrapRemote: PersonalBootstrapRemote,
    private val environmentId: String,
    private val onOutboxCommitted: () -> Unit,
    private val onAttachmentCleanupNeeded: () -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
    private val operationIdFactory: () -> UUID = UUID::randomUUID,
    private val attachmentIdFactory: () -> UUID = UUID::randomUUID,
) : ViewModel() {
    private val saveMutex = Mutex()
    private val attachmentMutex = Mutex()
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
            attachmentMutex.withLock {
                saveMutex.withLock {
                    if (generation == selectionGeneration) {
                        openContextLocked(verified, generation)
                    }
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

    fun onPhotoSelected(uri: Uri) {
        val currentState = _uiState.value
        val selectedSession = session ?: return
        val selectedScope = scope ?: return
        val selectedContext = teachingContext ?: return
        val selectedGeneration = selectionGeneration

        if (
            currentState.teachingContextStatus != TeachingContextStatus.Ready ||
            currentState.draftStatus == LocalDraftStatus.Loading ||
            currentState.attachmentStatus == AttachmentDraftStatus.Protecting ||
            currentState.attachmentCount > 0
        ) {
            return
        }

        _uiState.update {
            it.copy(
                attachmentStatus = AttachmentDraftStatus.Protecting,
                attachmentErrorCode = null,
            )
        }

        viewModelScope.launch {
            attachmentMutex.withLock attachmentLock@ {
                if (
                    selectedGeneration != selectionGeneration ||
                    scope?.storageKey != selectedScope.storageKey ||
                    session?.epoch != selectedSession.epoch
                ) {
                    return@attachmentLock
                }

                try {
                    photoAttachmentStager.stage(
                        scope = selectedScope,
                        draftEpoch = selectedSession.epoch,
                        assignmentId = selectedContext.assignmentId.toString(),
                        attachmentId = attachmentIdFactory(),
                        uri = uri,
                    )

                    if (
                        selectedGeneration == selectionGeneration &&
                        scope?.storageKey == selectedScope.storageKey &&
                        session?.epoch == selectedSession.epoch
                    ) {
                        _uiState.update {
                            it.copy(
                                attachmentStatus = AttachmentDraftStatus.SafeOnDevice,
                                attachmentCount = 1,
                                attachmentErrorCode = null,
                            )
                        }
                    }
                } catch (failure: Throwable) {
                    if (
                        selectedGeneration == selectionGeneration &&
                        scope?.storageKey == selectedScope.storageKey &&
                        session?.epoch == selectedSession.epoch
                    ) {
                        _uiState.update {
                            it.copy(
                                attachmentStatus = AttachmentDraftStatus.Failed,
                                attachmentCount = 0,
                                attachmentErrorCode = attachmentErrorCode(failure),
                            )
                        }
                    }
                }
            }
        }
    }

    fun removePhoto() {
        val selectedSession = session ?: return
        val selectedScope = scope ?: return
        val selectedGeneration = selectionGeneration
        val currentState = _uiState.value
        if (
            currentState.teachingContextStatus != TeachingContextStatus.Ready ||
            currentState.attachmentStatus == AttachmentDraftStatus.Protecting ||
            currentState.attachmentCount == 0
        ) {
            return
        }

        viewModelScope.launch {
            attachmentMutex.withLock attachmentLock@ {
                if (
                    selectedGeneration != selectionGeneration ||
                    scope?.storageKey != selectedScope.storageKey ||
                    session?.epoch != selectedSession.epoch
                ) {
                    return@attachmentLock
                }

                try {
                    val filesDeleted = attachmentStagingStore.discardUnboundDraft(
                        scope = selectedScope,
                        draftEpoch = selectedSession.epoch,
                    )
                    if (!filesDeleted) {
                        onAttachmentCleanupNeeded()
                    }

                    if (
                        selectedGeneration == selectionGeneration &&
                        scope?.storageKey == selectedScope.storageKey &&
                        session?.epoch == selectedSession.epoch
                    ) {
                        _uiState.update {
                            it.copy(
                                attachmentStatus = AttachmentDraftStatus.None,
                                attachmentCount = 0,
                                attachmentErrorCode = null,
                            )
                        }
                    }
                } catch (failure: Throwable) {
                    if (
                        selectedGeneration == selectionGeneration &&
                        scope?.storageKey == selectedScope.storageKey &&
                        session?.epoch == selectedSession.epoch
                    ) {
                        _uiState.update {
                            it.copy(
                                attachmentStatus = AttachmentDraftStatus.Failed,
                                attachmentErrorCode = attachmentErrorCode(failure),
                            )
                        }
                    }
                }
            }
        }
    }

    fun discard() {
        viewModelScope.launch {
            attachmentMutex.withLock attachmentLock@ {
                saveMutex.withLock saveLock@ {
                    val currentSession = session ?: return@saveLock
                    val currentScope = scope ?: return@saveLock
                    try {
                        val result = withContext(Dispatchers.IO) {
                            durableIntentDao.discardDraft(
                                scopeKey = currentScope.storageKey,
                                expectedEpoch = currentSession.epoch,
                            )
                        }
                        if (result == null) {
                            _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                            return@saveLock
                        }

                        val filesDeleted = attachmentStagingStore.deleteProtectedFilesBestEffort(
                            result.attachmentFileNames,
                        )
                        if (!filesDeleted) {
                            onAttachmentCleanupNeeded()
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
                                attachmentStatus = AttachmentDraftStatus.None,
                                attachmentCount = 0,
                                attachmentErrorCode = null,
                            )
                        }
                    } catch (_: Throwable) {
                        _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                    }
                }
            }
        }
    }

    fun submit() {
        viewModelScope.launch {
            attachmentMutex.withLock attachmentLock@ {
                saveMutex.withLock saveLock@ {
                val currentState = _uiState.value
                val currentSession = session ?: return@saveLock
                val currentScope = scope ?: return@saveLock
                val currentContext = teachingContext ?: return@saveLock
                val currentActor = actorAppUserId ?: return@saveLock
                val finalText = currentState.text

                if (
                    currentState.teachingContextStatus != TeachingContextStatus.Ready ||
                    currentState.draftStatus == LocalDraftStatus.PersistenceFailed ||
                    finalText.isBlank()
                ) {
                    return@saveLock
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
                        return@saveLock
                    }

                    session = withContext(Dispatchers.IO) {
                        store.open(currentScope)
                    }
                    _uiState.update {
                        it.copy(
                            text = "",
                            draftStatus = LocalDraftStatus.SafeOnDevice,
                            recoveredFromDisk = false,
                            attachmentStatus = AttachmentDraftStatus.None,
                            attachmentCount = 0,
                            attachmentErrorCode = null,
                        )
                    }
                    onOutboxCommitted()
                } catch (_: Throwable) {
                    // The Room transaction rolls back Draft retirement, Outbox
                    // insertion and attachment binding together. Keep current
                    // UI text and attachment state visible as well.
                    _uiState.update { it.copy(draftStatus = LocalDraftStatus.PersistenceFailed) }
                }
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
            val (opened, stagedAttachments) = withContext(Dispatchers.IO) {
                val openedSession = store.open(draftScope)
                val attachments = attachmentStagingStore
                    .readForDraft(draftScope, openedSession.epoch)
                    .filter {
                        it.state == AttachmentStagingState.Staged &&
                            it.parentObservationOperationId == null
                    }
                openedSession to attachments
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
                attachmentStatus = if (stagedAttachments.isEmpty()) {
                    AttachmentDraftStatus.None
                } else {
                    AttachmentDraftStatus.SafeOnDevice
                },
                attachmentCount = stagedAttachments.size,
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

    private fun attachmentErrorCode(failure: Throwable): String = when (failure) {
        is ImageDerivativeTooLargeException,
        is AttachmentTooLargeException,
        -> "too_large"
        is LocalAttachmentKeyUnavailableException -> "local_key_unavailable"
        is SecurityException -> "source_unavailable"
        else -> "unavailable"
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
            val attachmentStagingStore = AttachmentStagingStore(
                dao = database.attachmentStagingDao(),
                protectedFiles = ProtectedAttachmentFileStore(appContext),
            )
            return QuickCaptureViewModel(
                store = DraftStore(database.draftDao()),
                durableIntentDao = database.durableIntentDao(),
                attachmentStagingStore = attachmentStagingStore,
                photoAttachmentStager = PhotoAttachmentStager(
                    contentResolver = appContext.contentResolver,
                    derivativeFactory = ImageDerivativeFactory(),
                    attachmentStagingStore = attachmentStagingStore,
                ),
                bootstrapRemote = bootstrapRemote,
                environmentId = environmentId,
                onOutboxCommitted = { ObservationOutboxScheduler.kick(appContext) },
                onAttachmentCleanupNeeded = {
                    AttachmentStagingRecoveryScheduler.scheduleAfterGrace(appContext)
                },
            )
        }
    }
}
