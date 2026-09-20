package com.xueqing.app.durability

import com.xueqing.app.application.attachment.AttachmentCommandRemote
import com.xueqing.app.application.attachment.AttachmentCommitResult
import com.xueqing.app.application.attachment.AttachmentStorageRemote
import com.xueqing.app.application.attachment.AttachmentUploadResult
import com.xueqing.app.application.attachment.CommitObservationAttachmentRequest
import com.xueqing.app.application.attachment.ObservationAttachmentUploadRequest
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import java.io.ByteArrayOutputStream
import java.util.UUID

class AttachmentOutboxDrainer(
    private val dao: AttachmentStagingDao,
    private val stagingStore: AttachmentStagingStore,
    private val bootstrapRemote: PersonalBootstrapRemote,
    private val storageRemote: AttachmentStorageRemote,
    private val commandRemote: AttachmentCommandRemote,
    private val environmentId: String,
    private val onCleanupNeeded: () -> Unit,
    private val clock: () -> Long = System::currentTimeMillis,
    private val retryDelayMillis: Long = DEFAULT_RETRY_MILLIS,
) {
    data class Result(
        val processedCount: Int,
        val nextWakeAtEpochMillis: Long?,
        val blockedReason: BlockedReason? = null,
    )

    enum class BlockedReason {
        AuthenticationRequired,
        BootstrapTemporarilyUnavailable,
        BootstrapAccessUnavailable,
        BootstrapProtocolFailure,
    }

    suspend fun drainReady(maxItems: Int = DEFAULT_MAX_ITEMS): Result {
        require(maxItems > 0)
        require(retryDelayMillis > 0)

        val bootstrap = bootstrapRemote.fetch()
        val appUserId = when (bootstrap) {
            is PersonalBootstrapResult.Loaded -> bootstrap.bootstrap.actor.appUserId.toString()
            PersonalBootstrapResult.AuthenticationRequired -> {
                return Result(0, Math.addExact(clock(), retryDelayMillis), BlockedReason.AuthenticationRequired)
            }
            is PersonalBootstrapResult.UnknownResult -> {
                return Result(0, Math.addExact(clock(), retryDelayMillis), BlockedReason.BootstrapTemporarilyUnavailable)
            }
            is PersonalBootstrapResult.AccessUnavailable -> {
                return Result(0, null, BlockedReason.BootstrapAccessUnavailable)
            }
            is PersonalBootstrapResult.ProtocolFailure -> {
                return Result(0, null, BlockedReason.BootstrapProtocolFailure)
            }
        }

        var processed = 0
        var stopForRetry = false
        while (processed < maxItems && !stopForRetry) {
            val now = clock()
            val retryCutoff = Math.subtractExact(now, retryDelayMillis)
            val row = dao.readNextSyncCandidate(
                environmentId = environmentId,
                appUserId = appUserId,
                retryCutoffEpochMillis = retryCutoff,
            ) ?: break

            processed += 1
            when (row.state) {
                AttachmentStagingState.UploadPending,
                AttachmentStagingState.Uploading,
                -> stopForRetry = !processUpload(row)

                AttachmentStagingState.UploadedUncommitted -> {
                    dao.transitionState(
                        attachmentId = row.attachmentId,
                        expectedState = AttachmentStagingState.UploadedUncommitted,
                        newState = AttachmentStagingState.CommitPending,
                        lastErrorClass = null,
                        updatedAtEpochMillis = clock(),
                    )
                }

                AttachmentStagingState.CommitPending,
                AttachmentStagingState.CommitResultUnknown,
                -> stopForRetry = !processCommit(row, appUserId)

                AttachmentStagingState.Committed -> cleanupCommitted(row)

                else -> Unit
            }
        }

        return Result(
            processedCount = processed,
            nextWakeAtEpochMillis = dao.readNextSyncWakeAt(
                environmentId = environmentId,
                appUserId = appUserId,
                nowEpochMillis = clock(),
                retryDelayMillis = retryDelayMillis,
            ),
        )
    }

    private suspend fun processUpload(row: AttachmentStagingEntity): Boolean {
        val expectedState = row.state
        if (
            dao.transitionState(
                attachmentId = row.attachmentId,
                expectedState = expectedState,
                newState = AttachmentStagingState.Uploading,
                lastErrorClass = null,
                updatedAtEpochMillis = clock(),
            ) != 1
        ) {
            return true
        }

        val body = try {
            readProtectedBytes(row)
        } catch (failure: Throwable) {
            dao.transitionState(
                attachmentId = row.attachmentId,
                expectedState = AttachmentStagingState.Uploading,
                newState = AttachmentStagingState.UploadRejected,
                lastErrorClass = "LocalContent:${failure::class.java.simpleName}",
                updatedAtEpochMillis = clock(),
            )
            return true
        }

        val request = ObservationAttachmentUploadRequest(
            objectName = requireNotNull(row.remoteObjectName),
            contentType = row.contentType,
            byteSize = row.byteSize,
        )
        return when (val result = storageRemote.upload(request, body)) {
            AttachmentUploadResult.Uploaded,
            AttachmentUploadResult.AlreadyPresent,
            -> {
                dao.transitionState(
                    attachmentId = row.attachmentId,
                    expectedState = AttachmentStagingState.Uploading,
                    newState = AttachmentStagingState.UploadedUncommitted,
                    lastErrorClass = null,
                    updatedAtEpochMillis = clock(),
                )
                true
            }

            AttachmentUploadResult.AuthenticationRequired -> {
                markUploadRetry(row, "AuthenticationRequired")
                false
            }

            is AttachmentUploadResult.UnknownResult -> {
                markUploadRetry(row, "Unknown:${result.reason.name}")
                false
            }

            is AttachmentUploadResult.Rejected -> {
                dao.transitionState(
                    attachmentId = row.attachmentId,
                    expectedState = AttachmentStagingState.Uploading,
                    newState = AttachmentStagingState.UploadRejected,
                    lastErrorClass = "UploadRejected:${result.rejection.name}",
                    updatedAtEpochMillis = clock(),
                )
                true
            }
        }
    }

    private suspend fun markUploadRetry(
        row: AttachmentStagingEntity,
        errorClass: String,
    ) {
        dao.transitionState(
            attachmentId = row.attachmentId,
            expectedState = AttachmentStagingState.Uploading,
            newState = AttachmentStagingState.UploadPending,
            lastErrorClass = errorClass,
            updatedAtEpochMillis = clock(),
        )
    }

    private suspend fun processCommit(
        row: AttachmentStagingEntity,
        appUserId: String,
    ): Boolean {
        if (row.state == AttachmentStagingState.CommitResultUnknown) {
            if (
                dao.transitionState(
                    attachmentId = row.attachmentId,
                    expectedState = AttachmentStagingState.CommitResultUnknown,
                    newState = AttachmentStagingState.CommitPending,
                    lastErrorClass = null,
                    updatedAtEpochMillis = clock(),
                ) != 1
            ) {
                return true
            }
        }

        val request = try {
            CommitObservationAttachmentRequest(
                operationId = UUID.fromString(requireNotNull(row.attachmentCommitOperationId)),
                organizationId = UUID.fromString(row.organizationId),
                studentId = UUID.fromString(row.studentId),
                subjectProfileId = UUID.fromString(row.subjectProfileId),
                assignmentId = UUID.fromString(row.assignmentId),
                observationId = UUID.fromString(requireNotNull(row.authoritativeObservationId)),
                attachmentId = UUID.fromString(row.attachmentId),
            )
        } catch (failure: Throwable) {
            dao.transitionState(
                attachmentId = row.attachmentId,
                expectedState = AttachmentStagingState.CommitPending,
                newState = AttachmentStagingState.CommitRejected,
                lastErrorClass = "LocalProtocol:${failure::class.java.simpleName}",
                updatedAtEpochMillis = clock(),
            )
            return true
        }

        return when (val result = commandRemote.commit(request)) {
            is AttachmentCommitResult.Accepted -> {
                if (result.receipt.actorAppUserId.toString() != appUserId) {
                    dao.transitionState(
                        attachmentId = row.attachmentId,
                        expectedState = AttachmentStagingState.CommitPending,
                        newState = AttachmentStagingState.CommitRejected,
                        lastErrorClass = "Protocol:ReceiptActorMismatch",
                        updatedAtEpochMillis = clock(),
                    )
                    return true
                }

                if (
                    dao.transitionState(
                        attachmentId = row.attachmentId,
                        expectedState = AttachmentStagingState.CommitPending,
                        newState = AttachmentStagingState.Committed,
                        lastErrorClass = null,
                        updatedAtEpochMillis = clock(),
                    ) == 1
                ) {
                    cleanupCommitted(row.copy(state = AttachmentStagingState.Committed))
                }
                true
            }

            is AttachmentCommitResult.Rejected -> {
                dao.transitionState(
                    attachmentId = row.attachmentId,
                    expectedState = AttachmentStagingState.CommitPending,
                    newState = AttachmentStagingState.CommitRejected,
                    lastErrorClass = result.rejection.wireCode,
                    updatedAtEpochMillis = clock(),
                )
                true
            }

            is AttachmentCommitResult.AuthenticationRequired -> {
                markCommitUnknown(row, "AuthenticationRequired")
                false
            }

            is AttachmentCommitResult.UnknownResult -> {
                markCommitUnknown(row, "Unknown:${result.reason.name}")
                false
            }

            is AttachmentCommitResult.ProtocolFailure -> {
                dao.transitionState(
                    attachmentId = row.attachmentId,
                    expectedState = AttachmentStagingState.CommitPending,
                    newState = AttachmentStagingState.CommitRejected,
                    lastErrorClass = "Protocol:${result.failure.name}",
                    updatedAtEpochMillis = clock(),
                )
                true
            }
        }
    }

    private suspend fun markCommitUnknown(
        row: AttachmentStagingEntity,
        errorClass: String,
    ) {
        dao.transitionState(
            attachmentId = row.attachmentId,
            expectedState = AttachmentStagingState.CommitPending,
            newState = AttachmentStagingState.CommitResultUnknown,
            lastErrorClass = errorClass,
            updatedAtEpochMillis = clock(),
        )
    }

    private suspend fun cleanupCommitted(row: AttachmentStagingEntity) {
        val fileDeleted = stagingStore.deleteProtectedFilesBestEffort(
            listOf(row.localEncryptedFileName),
        )
        dao.deleteIfState(
            attachmentId = row.attachmentId,
            expectedState = AttachmentStagingState.Committed,
        )
        if (!fileDeleted) {
            onCleanupNeeded()
        }
    }

    private fun readProtectedBytes(row: AttachmentStagingEntity): ByteArray {
        require(row.byteSize in 1..ProtectedAttachmentFileStore.MAX_STAGED_PLAINTEXT_BYTES)
        val expectedSize = row.byteSize
        val output = ByteArrayOutputStream(expectedSize.toInt())
        stagingStore.openDecrypted(row).use { input ->
            val buffer = ByteArray(BUFFER_SIZE)
            var total = 0L
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read == 0) continue
                total = Math.addExact(total, read.toLong())
                require(total <= ProtectedAttachmentFileStore.MAX_STAGED_PLAINTEXT_BYTES)
                output.write(buffer, 0, read)
            }
            require(total == expectedSize) { "Protected attachment size does not match staged metadata" }
        }
        return output.toByteArray()
    }

    companion object {
        const val DEFAULT_RETRY_MILLIS = 60_000L
        const val DEFAULT_MAX_ITEMS = 8
        private const val BUFFER_SIZE = 32 * 1024
    }
}
