package com.xueqing.app.durability

import java.io.InputStream
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class AttachmentStagingStore(
    private val dao: AttachmentStagingDao,
    private val protectedFiles: ProtectedAttachmentFileStore,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    suspend fun stageDerivative(
        scope: DraftScope,
        draftEpoch: Long,
        assignmentId: String,
        attachmentId: UUID,
        contentType: String,
        source: InputStream,
    ): AttachmentStagingEntity {
        require(draftEpoch >= 0)
        require(assignmentId.isNotBlank())
        require(contentType in ALLOWED_CONTENT_TYPES) { "Unsupported attachment content type" }

        val stagedFile = withContext(Dispatchers.IO) {
            protectedFiles.stage(
                attachmentId = attachmentId,
                source = source,
            )
        }
        val now = clock()
        val entity = AttachmentStagingEntity(
            attachmentId = attachmentId.toString(),
            scopeKey = scope.storageKey,
            draftEpoch = draftEpoch,
            environmentId = scope.environmentId,
            appUserId = scope.appUserId,
            organizationId = scope.organizationId,
            studentId = scope.studentId,
            subjectProfileId = scope.subjectId,
            assignmentId = assignmentId,
            localEncryptedFileName = stagedFile.fileName,
            contentType = contentType,
            byteSize = stagedFile.plaintextByteSize,
            state = AttachmentStagingState.Staged,
            parentObservationOperationId = null,
            authoritativeObservationId = null,
            remoteObjectName = null,
            attachmentCommitOperationId = null,
            lastErrorClass = null,
            createdAtEpochMillis = now,
            updatedAtEpochMillis = now,
        )

        try {
            dao.insert(entity)
            return entity
        } catch (failure: Throwable) {
            withContext(Dispatchers.IO) {
                protectedFiles.delete(stagedFile.fileName)
            }
            throw failure
        }
    }

    suspend fun readForDraft(
        scope: DraftScope,
        draftEpoch: Long,
    ): List<AttachmentStagingEntity> =
        dao.readForDraft(scope.storageKey, draftEpoch)

    fun openDecrypted(entity: AttachmentStagingEntity): InputStream =
        protectedFiles.openDecrypted(entity.localEncryptedFileName)

    suspend fun discardUnboundDraft(
        scope: DraftScope,
        draftEpoch: Long,
    ): Boolean {
        val rows = dao.readForDraft(scope.storageKey, draftEpoch)
            .filter {
                it.state == AttachmentStagingState.Staged &&
                    it.parentObservationOperationId == null
            }
        dao.deleteUnboundForDraft(scope.storageKey, draftEpoch)
        return deleteProtectedFilesBestEffort(rows.map { it.localEncryptedFileName })
    }

    suspend fun deleteProtectedFilesBestEffort(
        fileNames: Collection<String>,
    ): Boolean = withContext(Dispatchers.IO) {
        var allDeleted = true
        fileNames.forEach { fileName ->
            val deleted = runCatching { protectedFiles.delete(fileName) }.getOrDefault(false)
            allDeleted = allDeleted && deleted
        }
        allDeleted
    }

    suspend fun reconcileOrphanedFiles(
        gracePeriodMillis: Long = DEFAULT_RECONCILIATION_GRACE_MILLIS,
    ) {
        require(gracePeriodMillis >= 0)
        // Read the authoritative local metadata first. If SQLCipher/Room cannot
        // be opened, no file deletion occurs.
        val referenced = dao.readReferencedFileNames().toSet()
        val cutoff = Math.subtractExact(clock(), gracePeriodMillis)
        withContext(Dispatchers.IO) {
            // A grace window prevents startup reconciliation racing a newly
            // staging attachment between file publish and Room metadata insert.
            protectedFiles.deleteTemporaryFilesOlderThan(cutoff)
            protectedFiles.listEncryptedFileNamesOlderThan(cutoff)
                .filterNot(referenced::contains)
                .forEach(protectedFiles::delete)
        }
    }

    companion object {
        const val DEFAULT_RECONCILIATION_GRACE_MILLIS = 15 * 60 * 1000L

        val ALLOWED_CONTENT_TYPES = setOf(
            "image/jpeg",
            "image/png",
            "image/webp",
        )
    }
}
