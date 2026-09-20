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
    ) {
        val rows = dao.readForDraft(scope.storageKey, draftEpoch)
            .filter {
                it.state == AttachmentStagingState.Staged &&
                    it.parentObservationOperationId == null
            }
        dao.deleteUnboundForDraft(scope.storageKey, draftEpoch)
        withContext(Dispatchers.IO) {
            rows.forEach { protectedFiles.delete(it.localEncryptedFileName) }
        }
    }

    suspend fun reconcileOrphanedFiles() {
        val referenced = dao.readReferencedFileNames().toSet()
        withContext(Dispatchers.IO) {
            protectedFiles.listEncryptedFileNames()
                .filterNot(referenced::contains)
                .forEach(protectedFiles::delete)
        }
    }

    companion object {
        val ALLOWED_CONTENT_TYPES = setOf(
            "image/jpeg",
            "image/png",
            "image/webp",
        )
    }
}
