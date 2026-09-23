package com.xueqing.app.durability

import android.content.Context
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.application.attachment.AttachmentCommandRemote
import com.xueqing.app.application.attachment.AttachmentCommitResult
import com.xueqing.app.application.attachment.AttachmentCommitUnknownReason
import com.xueqing.app.application.attachment.AttachmentStorageRemote
import com.xueqing.app.application.attachment.AttachmentUploadResult
import com.xueqing.app.application.attachment.AttachmentUploadUnknownReason
import com.xueqing.app.application.attachment.CommitObservationAttachmentReceipt
import com.xueqing.app.application.attachment.CommitObservationAttachmentRequest
import com.xueqing.app.application.attachment.ObservationAttachmentUploadRequest
import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapActor
import com.xueqing.app.application.bootstrap.PersonalBootstrapOrganization
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.compatibility.ClientCompatibilityState
import com.xueqing.app.application.compatibility.ConsequentialWriteAuthorization
import com.xueqing.app.application.compatibility.ConsequentialWriteGate
import java.io.ByteArrayInputStream
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AttachmentOutboxDrainerInstrumentedTest {
    private lateinit var context: Context
    private lateinit var database: DraftDatabase
    private lateinit var protectedFiles: ProtectedAttachmentFileStore
    private lateinit var stagingStore: AttachmentStagingStore
    private var now = NOW

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        DraftDatabase.purgeLocalEncryptedData(context)
        database = Room.inMemoryDatabaseBuilder(context, DraftDatabase::class.java)
            .allowMainThreadQueries()
            .build()
        protectedFiles = ProtectedAttachmentFileStore(context)
        stagingStore = AttachmentStagingStore(
            dao = database.attachmentStagingDao(),
            protectedFiles = protectedFiles,
            clock = { now },
        )
    }

    @After
    fun tearDown() {
        database.close()
        DraftDatabase.purgeLocalEncryptedData(context)
    }

    @Test
    fun securityBlockKeepsStagedAttachmentAndSkipsStorageAndCommit() = runBlocking {
        insertUploadPendingAttachment()
        val storage = FakeStorageRemote(AttachmentUploadResult.Uploaded)
        val command = FakeCommandRemote(
            AttachmentCommitResult.UnknownResult(
                UUID.fromString(COMMIT_OPERATION_ID),
                AttachmentCommitUnknownReason.NetworkFailure,
            ),
        )
        val gate = ConsequentialWriteGate {
            ConsequentialWriteAuthorization.Blocked(
                ClientCompatibilityState.SecurityBlocked,
                "XQ_CLIENT_SECURITY_BLOCKED",
            )
        }

        val result = drainer(storage, command, compatibilityGate = gate).drainReady()

        assertEquals(0, result.processedCount)
        assertEquals(
            AttachmentOutboxDrainer.BlockedReason.CompatibilitySecurityBlocked,
            result.blockedReason,
        )
        assertTrue(storage.requests.isEmpty())
        assertTrue(command.requests.isEmpty())
        assertEquals(
            AttachmentStagingState.UploadPending,
            database.attachmentStagingDao().read(ATTACHMENT_ID)?.state,
        )
        assertTrue(protectedFiles.encryptedFile("$ATTACHMENT_ID.xqas").exists())
    }

    @Test
    fun uploadAndCommitUnknownResultsReuseStableIdentitiesUntilAuthoritativeReceipt() = runBlocking {
        val row = insertUploadPendingAttachment()
        val storage = FakeStorageRemote(
            AttachmentUploadResult.UnknownResult(AttachmentUploadUnknownReason.NetworkFailure),
        )
        val command = FakeCommandRemote(
            AttachmentCommitResult.UnknownResult(
                UUID.fromString(COMMIT_OPERATION_ID),
                AttachmentCommitUnknownReason.NetworkFailure,
            ),
        )
        val drainer = drainer(storage, command)

        drainer.drainReady()
        var persisted = requireNotNull(database.attachmentStagingDao().read(ATTACHMENT_ID))
        assertEquals(AttachmentStagingState.UploadPending, persisted.state)
        assertEquals(row.remoteObjectName, persisted.remoteObjectName)
        assertEquals(COMMIT_OPERATION_ID, persisted.attachmentCommitOperationId)
        assertEquals(1, storage.requests.size)
        assertTrue(command.requests.isEmpty())

        now += RETRY_DELAY + 1
        storage.result = AttachmentUploadResult.AlreadyPresent
        drainer.drainReady()

        persisted = requireNotNull(database.attachmentStagingDao().read(ATTACHMENT_ID))
        assertEquals(AttachmentStagingState.CommitResultUnknown, persisted.state)
        assertEquals(row.remoteObjectName, persisted.remoteObjectName)
        assertEquals(COMMIT_OPERATION_ID, persisted.attachmentCommitOperationId)
        assertEquals(2, storage.requests.size)
        assertEquals(1, command.requests.size)
        assertEquals(COMMIT_OPERATION_ID, command.requests.single().operationId.toString())

        now += RETRY_DELAY + 1
        command.result = AttachmentCommitResult.Accepted(
            UUID.fromString(COMMIT_OPERATION_ID),
            receipt(command.requests.single()),
        )
        drainer.drainReady()

        assertNull(database.attachmentStagingDao().read(ATTACHMENT_ID))
        assertEquals(2, command.requests.size)
        assertEquals(
            listOf(COMMIT_OPERATION_ID, COMMIT_OPERATION_ID),
            command.requests.map { it.operationId.toString() },
        )
        assertFalse(protectedFiles.encryptedFile("$ATTACHMENT_ID.xqas").exists())
    }

    private suspend fun insertUploadPendingAttachment(): AttachmentStagingEntity {
        val bytes = "encrypted attachment retry proof".toByteArray()
        val staged = protectedFiles.stage(
            attachmentId = UUID.fromString(ATTACHMENT_ID),
            source = ByteArrayInputStream(bytes),
        )
        val row = AttachmentStagingEntity(
            attachmentId = ATTACHMENT_ID,
            scopeKey = "scope",
            draftEpoch = 1,
            environmentId = ENVIRONMENT_ID,
            appUserId = APP_USER_ID,
            organizationId = ORGANIZATION_ID,
            studentId = STUDENT_ID,
            subjectProfileId = PROFILE_ID,
            assignmentId = ASSIGNMENT_ID,
            localEncryptedFileName = staged.fileName,
            contentType = "image/jpeg",
            byteSize = staged.plaintextByteSize,
            state = AttachmentStagingState.UploadPending,
            parentObservationOperationId = OBSERVATION_OPERATION_ID,
            authoritativeObservationId = OBSERVATION_ID,
            remoteObjectName = OBJECT_NAME,
            attachmentCommitOperationId = COMMIT_OPERATION_ID,
            lastErrorClass = null,
            createdAtEpochMillis = now,
            updatedAtEpochMillis = now,
        )
        database.attachmentStagingDao().insert(row)
        return row
    }

    private fun drainer(
        storage: AttachmentStorageRemote,
        command: AttachmentCommandRemote,
        compatibilityGate: ConsequentialWriteGate = ConsequentialWriteGate {
            ConsequentialWriteAuthorization.Allowed
        },
    ) = AttachmentOutboxDrainer(
        dao = database.attachmentStagingDao(),
        stagingStore = stagingStore,
        bootstrapRemote = bootstrap(),
        compatibilityGate = compatibilityGate,
        storageRemote = storage,
        commandRemote = command,
        environmentId = ENVIRONMENT_ID,
        onCleanupNeeded = {},
        clock = { now },
        retryDelayMillis = RETRY_DELAY,
    )

    private fun bootstrap() = PersonalBootstrapRemote {
        PersonalBootstrapResult.Loaded(
            PersonalBootstrap(
                generatedAtServer = Instant.ofEpochMilli(now),
                actor = PersonalBootstrapActor(UUID.fromString(APP_USER_ID), "虚构教师"),
                organizations = listOf(
                    PersonalBootstrapOrganization(
                        UUID.fromString(ORGANIZATION_ID),
                        "虚构机构",
                        true,
                    ),
                ),
                teachingContexts = emptyList(),
            ),
        )
    }

    private fun receipt(request: CommitObservationAttachmentRequest) =
        CommitObservationAttachmentReceipt(
            command = "commit_observation_attachment_v1",
            operationId = request.operationId,
            attachmentId = request.attachmentId,
            observationId = request.observationId,
            actorAppUserId = UUID.fromString(APP_USER_ID),
            organizationId = request.organizationId,
            studentId = request.studentId,
            subjectProfileId = request.subjectProfileId,
            subjectKey = "chinese",
            assignmentId = request.assignmentId,
            bucketId = "teaching-attachments-v1",
            objectName = OBJECT_NAME,
            contentType = "image/jpeg",
            byteSize = "encrypted attachment retry proof".toByteArray().size.toLong(),
            serverCommittedAt = Instant.ofEpochMilli(now),
        )

    private class FakeStorageRemote(
        var result: AttachmentUploadResult,
    ) : AttachmentStorageRemote {
        val requests = mutableListOf<ObservationAttachmentUploadRequest>()

        override fun upload(
            request: ObservationAttachmentUploadRequest,
            body: ByteArray,
        ): AttachmentUploadResult {
            requests += request
            return result
        }
    }

    private class FakeCommandRemote(
        var result: AttachmentCommitResult,
    ) : AttachmentCommandRemote {
        val requests = mutableListOf<CommitObservationAttachmentRequest>()

        override fun commit(request: CommitObservationAttachmentRequest): AttachmentCommitResult {
            requests += request
            return result
        }
    }

    private companion object {
        const val NOW = 1_789_632_000_000L
        const val RETRY_DELAY = 100L
        const val ENVIRONMENT_ID = "attachment-sync-ci"
        const val APP_USER_ID = "10000000-0000-0000-0000-000000000001"
        const val ORGANIZATION_ID = "20000000-0000-0000-0000-000000000001"
        const val STUDENT_ID = "30000000-0000-0000-0000-000000000001"
        const val PROFILE_ID = "40000000-0000-0000-0000-000000000001"
        const val ASSIGNMENT_ID = "50000000-0000-0000-0000-000000000001"
        const val OBSERVATION_OPERATION_ID = "70000000-0000-0000-0000-000000000001"
        const val OBSERVATION_ID = "72000000-0000-0000-0000-000000000001"
        const val ATTACHMENT_ID = "71000000-0000-0000-0000-000000000001"
        const val COMMIT_OPERATION_ID = "73000000-0000-0000-0000-000000000001"
        const val OBJECT_NAME =
            "v1/org/$ORGANIZATION_ID/student/$STUDENT_ID/profile/$PROFILE_ID/" +
                "observation/$OBSERVATION_ID/attachment/$ATTACHMENT_ID"
    }
}
