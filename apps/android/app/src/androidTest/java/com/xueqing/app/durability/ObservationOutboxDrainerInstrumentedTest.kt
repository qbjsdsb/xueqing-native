package com.xueqing.app.durability

import android.content.Context
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapActor
import com.xueqing.app.application.bootstrap.PersonalBootstrapOrganization
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.observation.CreateObservationReceipt
import com.xueqing.app.application.observation.CreateObservationRejection
import com.xueqing.app.application.observation.CreateObservationRequest
import com.xueqing.app.application.observation.ObservationCommandRemote
import com.xueqing.app.application.observation.ObservationCommandResult
import com.xueqing.app.application.observation.ObservationUnknownReason
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ObservationOutboxDrainerInstrumentedTest {
    private lateinit var database: DraftDatabase
    private lateinit var dao: DurableIntentDao
    private lateinit var draftStore: DraftStore
    private var now = NOW

    private val appUserA = UUID.fromString("10000000-0000-0000-0000-000000000001")
    private val appUserB = UUID.fromString("10000000-0000-0000-0000-000000000002")
    private val organizationId = UUID.fromString("20000000-0000-0000-0000-000000000001")
    private val studentId = UUID.fromString("30000000-0000-0000-0000-000000000001")
    private val subjectProfileId = UUID.fromString("40000000-0000-0000-0000-000000000001")
    private val assignmentId = UUID.fromString("50000000-0000-0000-0000-000000000001")

    @Before
    fun setUp() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        database = Room.inMemoryDatabaseBuilder(context, DraftDatabase::class.java)
            .allowMainThreadQueries()
            .build()
        dao = database.durableIntentDao()
        draftStore = DraftStore(database.draftDao()) { now }
    }

    @After
    fun tearDown() {
        database.close()
    }

    @Test
    fun unknownResultRetriesSameOperationAndLaterAcknowledgesSingleIntent() = runBlocking {
        val operationId = UUID.randomUUID()
        enqueue(appUserA, request(operationId, "响应丢失后必须复用 operation id"))
        val seenOperations = mutableListOf<UUID>()
        var callCount = 0
        val remote = ObservationCommandRemote { request ->
            seenOperations += request.operationId
            callCount += 1
            if (callCount == 1) {
                ObservationCommandResult.UnknownResult(request.operationId, ObservationUnknownReason.NetworkFailure)
            } else {
                ObservationCommandResult.Accepted(request.operationId, receipt(request))
            }
        }
        val drainer = drainer(appUserA, remote)

        drainer.drainReady()
        val retry = requireNotNull(dao.readByOperationId(operationId.toString()))
        assertEquals(ObservationOutboxStatus.Retry, retry.queueStatus)
        assertEquals(operationId.toString(), retry.operationId)
        assertTrue(retry.nextAttemptAtEpochMillis > now)

        now = retry.nextAttemptAtEpochMillis
        drainer.drainReady()

        val acknowledged = requireNotNull(dao.readByOperationId(operationId.toString()))
        assertEquals(ObservationOutboxStatus.Acknowledged, acknowledged.queueStatus)
        assertEquals(2, acknowledged.attemptCount)
        assertEquals(listOf(operationId, operationId), seenOperations)
    }

    @Test
    fun deterministicAssignmentRejectionDeadLettersAndKeepsOriginalPayload() = runBlocking {
        val request = request(UUID.randomUUID(), "任课关系变化时原文仍必须保留")
        enqueue(appUserA, request)
        val remote = ObservationCommandRemote {
            ObservationCommandResult.Rejected(
                it.operationId,
                CreateObservationRejection.TeacherAssignmentRequired,
            )
        }

        drainer(appUserA, remote).drainReady()

        val rejected = requireNotNull(dao.readByOperationId(request.operationId.toString()))
        assertEquals(ObservationOutboxStatus.DeadLetter, rejected.queueStatus)
        assertEquals("XQ_TEACHER_ASSIGNMENT_REQUIRED", rejected.lastErrorClass)
        assertEquals(request.rawText, ObservationOutboxCodec.decodeRequest(rejected.payloadJson).rawText)
    }

    @Test
    fun currentSessionActorCannotClaimAnotherAccountsIntent() = runBlocking {
        val request = request(UUID.randomUUID(), "账号 A 的本地意图")
        enqueue(appUserA, request)
        var remoteCalls = 0
        val remote = ObservationCommandRemote {
            remoteCalls += 1
            ObservationCommandResult.Accepted(it.operationId, receipt(it))
        }

        val wrongActorResult = drainer(appUserB, remote).drainReady()
        assertEquals(0, wrongActorResult.processedCount)
        assertEquals(0, remoteCalls)
        assertEquals(
            ObservationOutboxStatus.Pending,
            dao.readByOperationId(request.operationId.toString())?.queueStatus,
        )

        drainer(appUserA, remote).drainReady()
        assertEquals(1, remoteCalls)
        assertEquals(
            ObservationOutboxStatus.Acknowledged,
            dao.readByOperationId(request.operationId.toString())?.queueStatus,
        )
    }

    @Test
    fun acceptedReceiptPromotesWaitingAttachmentWithStableRemoteIdentity() = runBlocking {
        val operationId = UUID.fromString("70000000-0000-0000-0000-000000000111")
        val attachmentId = "71000000-0000-0000-0000-000000000111"
        val observationId = UUID.fromString("72000000-0000-0000-0000-000000000111")
        val commitOperationId = UUID.fromString("73000000-0000-0000-0000-000000000111")
        val scope = DraftScope(
            environmentId = ENVIRONMENT_ID,
            appUserId = appUserA.toString(),
            organizationId = organizationId.toString(),
            studentId = studentId.toString(),
            subjectId = subjectProfileId.toString(),
            contextId = "quick-capture",
        )
        val session = draftStore.open(scope)
        database.attachmentStagingDao().insert(
            AttachmentStagingEntity(
                attachmentId = attachmentId,
                scopeKey = scope.storageKey,
                draftEpoch = session.epoch,
                environmentId = scope.environmentId,
                appUserId = scope.appUserId,
                organizationId = scope.organizationId,
                studentId = scope.studentId,
                subjectProfileId = scope.subjectId,
                assignmentId = assignmentId.toString(),
                localEncryptedFileName = "$attachmentId.xqas",
                contentType = "image/jpeg",
                byteSize = 256,
                state = AttachmentStagingState.Staged,
                parentObservationOperationId = null,
                authoritativeObservationId = null,
                remoteObjectName = null,
                attachmentCommitOperationId = null,
                lastErrorClass = null,
                createdAtEpochMillis = now,
                updatedAtEpochMillis = now,
            ),
        )
        val request = request(operationId, "Observation receipt 必须稳定提升附件远端身份")
        enqueue(appUserA, request)

        val remote = ObservationCommandRemote {
            ObservationCommandResult.Accepted(
                it.operationId,
                receipt(it).copy(observationId = observationId),
            )
        }
        drainer(
            actorAppUserId = appUserA,
            remote = remote,
            attachmentCommitOperationIdFactory = { commitOperationId },
        ).drainReady()

        val row = requireNotNull(database.attachmentStagingDao().read(attachmentId))
        assertEquals(AttachmentStagingState.UploadPending, row.state)
        assertEquals(observationId.toString(), row.authoritativeObservationId)
        assertEquals(
            "v1/org/$organizationId/student/$studentId/profile/$subjectProfileId/" +
                "observation/$observationId/attachment/$attachmentId",
            row.remoteObjectName,
        )
        assertEquals(commitOperationId.toString(), row.attachmentCommitOperationId)
        assertEquals(
            ObservationOutboxStatus.Acknowledged,
            dao.readByOperationId(operationId.toString())?.queueStatus,
        )
    }

    @Test
    fun mismatchedReceiptActorDeadLettersObservationWithoutPromotingAttachment() = runBlocking {
        val operationId = UUID.fromString("70000000-0000-0000-0000-000000000112")
        val request = request(operationId, "错误 actor receipt 不能推进附件")
        enqueue(appUserA, request)
        val remote = ObservationCommandRemote {
            ObservationCommandResult.Accepted(
                it.operationId,
                receipt(it).copy(actorAppUserId = appUserB),
            )
        }

        drainer(appUserA, remote).drainReady()

        val rejected = requireNotNull(dao.readByOperationId(operationId.toString()))
        assertEquals(ObservationOutboxStatus.DeadLetter, rejected.queueStatus)
        assertEquals("Protocol:ReceiptActorMismatch", rejected.lastErrorClass)
        assertEquals(request.rawText, ObservationOutboxCodec.decodeRequest(rejected.payloadJson).rawText)
    }

    @Test
    fun authenticationFailureKeepsIntentRetryableWithSameOperation() = runBlocking {
        val request = request(UUID.randomUUID(), "401 后不能生成新 operation")
        enqueue(appUserA, request)
        val remote = ObservationCommandRemote {
            ObservationCommandResult.AuthenticationRequired(it.operationId)
        }

        drainer(appUserA, remote).drainReady()

        val pendingAuth = requireNotNull(dao.readByOperationId(request.operationId.toString()))
        assertEquals(ObservationOutboxStatus.Retry, pendingAuth.queueStatus)
        assertEquals("AuthenticationRequired", pendingAuth.lastErrorClass)
        assertEquals(request.operationId.toString(), pendingAuth.operationId)
        assertNull(pendingAuth.leaseId)
    }

    private suspend fun enqueue(appUserId: UUID, request: CreateObservationRequest) {
        val scope = DraftScope(
            environmentId = ENVIRONMENT_ID,
            appUserId = appUserId.toString(),
            organizationId = organizationId.toString(),
            studentId = studentId.toString(),
            subjectId = subjectProfileId.toString(),
            contextId = "quick-capture",
        )
        val session = draftStore.open(scope)
        val entity = ObservationOutboxEntity(
            operationId = request.operationId.toString(),
            commandType = ObservationOutboxCodec.COMMAND_TYPE,
            scopeKey = scope.storageKey,
            environmentId = scope.environmentId,
            appUserId = scope.appUserId,
            organizationId = request.organizationId.toString(),
            payloadJson = ObservationOutboxCodec.encodeRequest(request),
            queueStatus = ObservationOutboxStatus.Pending,
            attemptCount = 0,
            lastErrorClass = null,
            nextAttemptAtEpochMillis = now,
            leaseId = null,
            leaseExpiresAtEpochMillis = null,
            acknowledgedAtEpochMillis = null,
            serverReceiptJson = null,
            createdAtEpochMillis = now,
        )
        requireNotNull(
            dao.submitObservation(
                scopeKey = scope.storageKey,
                expectedEpoch = session.epoch,
                finalText = request.rawText,
                updatedAtEpochMillis = now,
                outbox = entity,
            ),
        )
    }

    private fun drainer(
        actorAppUserId: UUID,
        remote: ObservationCommandRemote,
        attachmentCommitOperationIdFactory: () -> UUID = UUID::randomUUID,
    ): ObservationOutboxDrainer = ObservationOutboxDrainer(
        dao = dao,
        bootstrapRemote = bootstrapRemote(actorAppUserId),
        observationRemote = remote,
        environmentId = ENVIRONMENT_ID,
        clock = { now },
        leaseDurationMillis = 1_000,
        attachmentCommitOperationIdFactory = attachmentCommitOperationIdFactory,
    )

    private fun bootstrapRemote(actorAppUserId: UUID) = PersonalBootstrapRemote {
        PersonalBootstrapResult.Loaded(
            PersonalBootstrap(
                generatedAtServer = Instant.ofEpochMilli(now),
                actor = PersonalBootstrapActor(actorAppUserId, "虚构教师"),
                organizations = listOf(
                    PersonalBootstrapOrganization(organizationId, "虚构机构", true),
                ),
                teachingContexts = emptyList(),
            ),
        )
    }

    private fun request(operationId: UUID, rawText: String) = CreateObservationRequest(
        operationId = operationId,
        organizationId = organizationId,
        studentId = studentId,
        subjectProfileId = subjectProfileId,
        assignmentId = assignmentId,
        rawText = rawText,
        clientCapturedAt = Instant.ofEpochMilli(now),
        clientCaptureMetadata = mapOf("surface" to "quick_capture"),
    )

    private fun receipt(request: CreateObservationRequest) = CreateObservationReceipt(
        command = ObservationOutboxCodec.COMMAND_TYPE,
        operationId = request.operationId,
        observationId = UUID.randomUUID(),
        actorAppUserId = appUserA,
        organizationId = request.organizationId,
        studentId = request.studentId,
        subjectProfileId = request.subjectProfileId,
        subjectKey = "chinese",
        serverCommittedAt = Instant.ofEpochMilli(now),
    )

    private companion object {
        const val ENVIRONMENT_ID = "ci"
        const val NOW = 1_789_632_000_000L
    }
}
