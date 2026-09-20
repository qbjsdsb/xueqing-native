package com.xueqing.app.durability

import android.content.Context
import android.util.Base64
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.work.testing.TestListenableWorkerBuilder
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.attachment.AttachmentCommitResult
import com.xueqing.app.application.attachment.AttachmentUploadResult
import com.xueqing.app.application.attachment.CommitObservationAttachmentRequest
import com.xueqing.app.application.attachment.ObservationAttachmentUploadRequest
import com.xueqing.app.application.observation.CreateObservationRequest
import com.xueqing.app.application.observation.ObservationCommandResult
import com.xueqing.app.infrastructure.remote.HttpRpcTransport
import com.xueqing.app.infrastructure.remote.HttpStorageTransport
import com.xueqing.app.infrastructure.remote.SessionTokenSource
import com.xueqing.app.infrastructure.remote.SupabaseCommitObservationAttachmentAdapter
import com.xueqing.app.infrastructure.remote.SupabaseCreateObservationAdapter
import com.xueqing.app.infrastructure.remote.SupabaseObservationAttachmentStorageAdapter
import com.xueqing.app.infrastructure.remote.SupabasePersonalBootstrapAdapter
import java.io.ByteArrayInputStream
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ObservationReferenceProviderE2ETest {
    private lateinit var context: Context
    private lateinit var rpcBaseUrl: String
    private lateinit var publishableKey: String
    private lateinit var accessToken: String

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        val arguments = InstrumentationRegistry.getArguments()
        rpcBaseUrl = arguments.getString(ARG_RPC_BASE_URL).orEmpty()
        publishableKey = arguments.getString(ARG_PUBLISHABLE_KEY).orEmpty()
        accessToken = arguments.getString(ARG_ACCESS_TOKEN).orEmpty()

        assumeTrue(
            "Reference-provider arguments are supplied only by the dedicated E2E gate.",
            rpcBaseUrl.isNotBlank() && publishableKey.isNotBlank() && accessToken.isNotBlank(),
        )

        ObservationSyncRuntime.clear()
        AttachmentSyncRuntime.clear()
        DraftDatabase.purgeLocalEncryptedData(context)
    }

    @After
    fun tearDown() {
        ObservationSyncRuntime.clear()
        AttachmentSyncRuntime.clear()
        if (::context.isInitialized) {
            DraftDatabase.purgeLocalEncryptedData(context)
        }
    }

    @Test
    fun encryptedOutboxDrainsThroughWorkerToAuthoritativeReferenceProvider() = runBlocking {
        val sessionTokenSource = SessionTokenSource { accessToken }
        val transport = HttpRpcTransport(
            baseUrl = rpcBaseUrl,
            publishableKey = publishableKey,
            allowInsecureLoopbackForDevelopment = true,
        )
        val bootstrapRemote = SupabasePersonalBootstrapAdapter(sessionTokenSource, transport)
        val observationRemote = SupabaseCreateObservationAdapter(sessionTokenSource, transport)

        val bootstrapResult = withContext(Dispatchers.IO) { bootstrapRemote.fetch() }
        assertTrue(bootstrapResult is PersonalBootstrapResult.Loaded)
        val bootstrap = (bootstrapResult as PersonalBootstrapResult.Loaded).bootstrap
        assertEquals(EXPECTED_APP_USER_ID, bootstrap.actor.appUserId.toString())
        val teachingContext = bootstrap.teachingContexts.single()

        val database = DraftDatabase.get(context)
        val dao = database.durableIntentDao()
        val scope = DraftScope(
            environmentId = ENVIRONMENT_ID,
            appUserId = bootstrap.actor.appUserId.toString(),
            organizationId = teachingContext.organizationId.toString(),
            studentId = teachingContext.studentId.toString(),
            subjectId = teachingContext.subjectProfileId.toString(),
            contextId = "quick-capture",
        )
        val session = DraftStore(database.draftDao()) { CAPTURED_AT.toEpochMilli() }.open(scope)
        val protectedFiles = ProtectedAttachmentFileStore(context)
        val attachmentStore = AttachmentStagingStore(
            dao = database.attachmentStagingDao(),
            protectedFiles = protectedFiles,
            clock = { CAPTURED_AT.toEpochMilli() },
        )
        val pngBytes = Base64.decode(PNG_BASE64, Base64.DEFAULT)
        val stagedAttachment = attachmentStore.stageDerivative(
            scope = scope,
            draftEpoch = session.epoch,
            assignmentId = teachingContext.assignmentId.toString(),
            attachmentId = UUID.fromString(ATTACHMENT_ID),
            contentType = "image/png",
            source = ByteArrayInputStream(pngBytes),
        )
        assertTrue(protectedFiles.encryptedFile(stagedAttachment.localEncryptedFileName).exists())

        val operationId = UUID.fromString(OPERATION_ID)
        val request = CreateObservationRequest(
            operationId = operationId,
            organizationId = teachingContext.organizationId,
            studentId = teachingContext.studentId,
            subjectProfileId = teachingContext.subjectProfileId,
            assignmentId = teachingContext.assignmentId,
            rawText = SENTINEL,
            clientCapturedAt = CAPTURED_AT,
            clientCaptureMetadata = mapOf(
                "surface" to "quick_capture",
                "fixture" to "reference_provider_e2e",
            ),
        )
        val outbox = ObservationOutboxEntity(
            operationId = operationId.toString(),
            commandType = ObservationOutboxCodec.COMMAND_TYPE,
            scopeKey = scope.storageKey,
            environmentId = ENVIRONMENT_ID,
            appUserId = bootstrap.actor.appUserId.toString(),
            organizationId = teachingContext.organizationId.toString(),
            payloadJson = ObservationOutboxCodec.encodeRequest(request),
            queueStatus = ObservationOutboxStatus.Pending,
            attemptCount = 0,
            lastErrorClass = null,
            nextAttemptAtEpochMillis = CAPTURED_AT.toEpochMilli(),
            leaseId = null,
            leaseExpiresAtEpochMillis = null,
            acknowledgedAtEpochMillis = null,
            serverReceiptJson = null,
            createdAtEpochMillis = CAPTURED_AT.toEpochMilli(),
        )

        requireNotNull(
            dao.submitObservation(
                scopeKey = scope.storageKey,
                expectedEpoch = session.epoch,
                finalText = request.rawText,
                updatedAtEpochMillis = CAPTURED_AT.toEpochMilli(),
                outbox = outbox,
            ),
        )
        assertEquals(
            ObservationOutboxStatus.Pending,
            dao.readByOperationId(OPERATION_ID)?.queueStatus,
        )

        ObservationSyncRuntime.install { appContext ->
            ObservationOutboxDrainer(
                dao = DraftDatabase.get(appContext).durableIntentDao(),
                bootstrapRemote = bootstrapRemote,
                observationRemote = observationRemote,
                environmentId = ENVIRONMENT_ID,
                attachmentCommitOperationIdFactory = {
                    UUID.fromString(ATTACHMENT_COMMIT_OPERATION_ID)
                },
            )
        }

        val worker = TestListenableWorkerBuilder<ObservationDrainWorker>(context).build()
        withContext(Dispatchers.IO) { worker.doWork() }

        val acknowledged = requireNotNull(dao.readByOperationId(OPERATION_ID))
        assertEquals(ObservationOutboxStatus.Acknowledged, acknowledged.queueStatus)
        assertEquals(1, acknowledged.attemptCount)
        assertNotNull(acknowledged.acknowledgedAtEpochMillis)
        val receiptJson = requireNotNull(acknowledged.serverReceiptJson)
        val receipt = Json.parseToJsonElement(receiptJson) as JsonObject
        assertEquals(OPERATION_ID, receipt.requiredString("operation_id"))
        assertEquals(EXPECTED_APP_USER_ID, receipt.requiredString("actor_app_user_id"))
        assertEquals(teachingContext.organizationId.toString(), receipt.requiredString("organization_id"))
        assertEquals(teachingContext.studentId.toString(), receipt.requiredString("student_id"))
        assertEquals(teachingContext.subjectProfileId.toString(), receipt.requiredString("subject_profile_id"))
        val firstObservationId = receipt.requiredString("observation_id")

        val promoted = requireNotNull(database.attachmentStagingDao().read(ATTACHMENT_ID))
        assertEquals(AttachmentStagingState.UploadPending, promoted.state)
        assertEquals(firstObservationId, promoted.authoritativeObservationId)
        assertEquals(ATTACHMENT_COMMIT_OPERATION_ID, promoted.attachmentCommitOperationId)
        assertEquals(
            "v1/org/${teachingContext.organizationId}/student/${teachingContext.studentId}/" +
                "profile/${teachingContext.subjectProfileId}/observation/$firstObservationId/" +
                "attachment/$ATTACHMENT_ID",
            promoted.remoteObjectName,
        )

        val storageRemote = SupabaseObservationAttachmentStorageAdapter(
            sessionTokenSource = sessionTokenSource,
            transport = HttpStorageTransport(
                baseUrl = rpcBaseUrl,
                publishableKey = publishableKey,
                allowInsecureLoopbackForDevelopment = true,
            ),
        )
        val attachmentCommandRemote = SupabaseCommitObservationAttachmentAdapter(
            sessionTokenSource = sessionTokenSource,
            transport = transport,
        )
        AttachmentSyncRuntime.install { appContext ->
            val appDatabase = DraftDatabase.get(appContext)
            AttachmentOutboxDrainer(
                dao = appDatabase.attachmentStagingDao(),
                stagingStore = AttachmentStagingStore(
                    dao = appDatabase.attachmentStagingDao(),
                    protectedFiles = ProtectedAttachmentFileStore(appContext),
                ),
                bootstrapRemote = bootstrapRemote,
                storageRemote = storageRemote,
                commandRemote = attachmentCommandRemote,
                environmentId = ENVIRONMENT_ID,
                onCleanupNeeded = {},
            )
        }

        val attachmentWorker = TestListenableWorkerBuilder<AttachmentDrainWorker>(context).build()
        withContext(Dispatchers.IO) { attachmentWorker.doWork() }

        assertNull(database.attachmentStagingDao().read(ATTACHMENT_ID))
        assertTrue(!protectedFiles.encryptedFile(stagedAttachment.localEncryptedFileName).exists())

        val duplicateUpload = withContext(Dispatchers.IO) {
            storageRemote.upload(
                ObservationAttachmentUploadRequest(
                    objectName = requireNotNull(promoted.remoteObjectName),
                    contentType = "image/png",
                    byteSize = pngBytes.size.toLong(),
                ),
                pngBytes,
            )
        }
        assertTrue(duplicateUpload is AttachmentUploadResult.AlreadyPresent)

        val attachmentCommitRequest = CommitObservationAttachmentRequest(
            operationId = UUID.fromString(ATTACHMENT_COMMIT_OPERATION_ID),
            organizationId = teachingContext.organizationId,
            studentId = teachingContext.studentId,
            subjectProfileId = teachingContext.subjectProfileId,
            assignmentId = teachingContext.assignmentId,
            observationId = UUID.fromString(firstObservationId),
            attachmentId = UUID.fromString(ATTACHMENT_ID),
        )
        val replay = withContext(Dispatchers.IO) {
            attachmentCommandRemote.commit(attachmentCommitRequest)
        }
        assertTrue(replay is AttachmentCommitResult.Accepted)
        replay as AttachmentCommitResult.Accepted
        assertEquals(UUID.fromString(ATTACHMENT_COMMIT_OPERATION_ID), replay.receipt.operationId)
        assertEquals(UUID.fromString(ATTACHMENT_ID), replay.receipt.attachmentId)
        assertEquals(UUID.fromString(firstObservationId), replay.receipt.observationId)

        // Hit the real command boundary again with the exact same operation and
        // payload. Server idempotency must return the same authoritative receipt.
        val duplicateResult = withContext(Dispatchers.IO) {
            observationRemote.createObservation(request)
        }
        assertTrue(duplicateResult is ObservationCommandResult.Accepted)
        val duplicateReceipt = (duplicateResult as ObservationCommandResult.Accepted).receipt
        assertEquals(operationId, duplicateReceipt.operationId)
        assertEquals(firstObservationId, duplicateReceipt.observationId.toString())
    }

    private fun JsonObject.requiredString(name: String): String =
        (this[name] as? JsonPrimitive)?.content
            ?.takeIf(String::isNotBlank)
            ?: error("Missing receipt field: $name")

    private companion object {
        const val ARG_RPC_BASE_URL = "xueqingRpcBaseUrl"
        const val ARG_PUBLISHABLE_KEY = "xueqingPublishableKey"
        const val ARG_ACCESS_TOKEN = "xueqingAccessToken"
        const val ENVIRONMENT_ID = "reference-provider-ci"
        const val EXPECTED_APP_USER_ID = "10000000-0000-0000-0000-000000000001"
        const val OPERATION_ID = "70000000-0000-0000-0000-000000000001"
        const val SENTINEL = "Android reference-provider E2E：提交必须只生成一条权威课堂观察。"
        const val ATTACHMENT_ID = "71000000-0000-0000-0000-00000000e201"
        const val ATTACHMENT_COMMIT_OPERATION_ID = "73000000-0000-0000-0000-00000000e201"
        const val PNG_BASE64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z0xkAAAAASUVORK5CYII="
        val CAPTURED_AT: Instant = Instant.parse("2026-09-17T18:00:00Z")
    }
}
