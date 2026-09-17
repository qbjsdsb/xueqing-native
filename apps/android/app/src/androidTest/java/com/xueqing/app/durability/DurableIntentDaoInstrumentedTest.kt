package com.xueqing.app.durability

import android.content.Context
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.application.observation.CreateObservationRequest
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class DurableIntentDaoInstrumentedTest {
    private lateinit var database: DraftDatabase
    private lateinit var draftStore: DraftStore
    private lateinit var dao: DurableIntentDao

    private val scope = DraftScope(
        environmentId = "ci",
        appUserId = "11111111-1111-1111-1111-111111111111",
        organizationId = "22222222-2222-2222-2222-222222222222",
        studentId = "33333333-3333-3333-3333-333333333333",
        subjectId = "44444444-4444-4444-4444-444444444444",
        contextId = "quick-capture",
    )

    @Before
    fun setUp() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        database = Room.inMemoryDatabaseBuilder(context, DraftDatabase::class.java)
            .allowMainThreadQueries()
            .build()
        draftStore = DraftStore(database.draftDao()) { NOW }
        dao = database.durableIntentDao()
    }

    @After
    fun tearDown() {
        database.close()
    }

    @Test
    fun submitSnapshotsLatestUiTextQueuesIntentAndBlocksStaleAutosave() = runBlocking {
        val session = draftStore.open(scope)
        assertTrue(draftStore.save(session, "较早的 autosave 内容"))
        val operationId = UUID.randomUUID()
        val finalText = "最终点击提交：概括题仍容易照抄原句。"
        val request = request(operationId = operationId, rawText = finalText)

        val nextEpoch = dao.submitObservation(
            scopeKey = scope.storageKey,
            expectedEpoch = session.epoch,
            finalText = finalText,
            updatedAtEpochMillis = NOW,
            outbox = outbox(request),
        )

        assertEquals(session.epoch + 1, nextEpoch)
        assertNull(draftStore.load(scope))
        assertFalse(draftStore.save(session, "延迟到达的旧 autosave"))

        val queued = requireNotNull(dao.readByOperationId(operationId.toString()))
        assertEquals(ObservationOutboxStatus.Pending, queued.queueStatus)
        assertEquals(operationId, ObservationOutboxCodec.decodeRequest(queued.payloadJson).operationId)
        assertEquals(finalText, ObservationOutboxCodec.decodeRequest(queued.payloadJson).rawText)
    }

    @Test
    fun failedOutboxInsertRollsBackDraftRetirementAndEpochAdvance() = runBlocking {
        val existingSession = draftStore.open(scope)
        val duplicateOperationId = UUID.randomUUID()
        val existingRequest = request(duplicateOperationId, "第一条已经排队的记录")
        assertNotNull(
            dao.submitObservation(
                scopeKey = scope.storageKey,
                expectedEpoch = existingSession.epoch,
                finalText = existingRequest.rawText,
                updatedAtEpochMillis = NOW,
                outbox = outbox(existingRequest),
            ),
        )

        val secondScope = scope.copy(studentId = "55555555-5555-5555-5555-555555555555")
        val secondSession = draftStore.open(secondScope)
        assertTrue(draftStore.save(secondSession, "必须在失败事务后保留的草稿"))
        val conflictingRequest = request(duplicateOperationId, "不应覆盖第一条 operation")

        var failed = false
        try {
            dao.submitObservation(
                scopeKey = secondScope.storageKey,
                expectedEpoch = secondSession.epoch,
                finalText = conflictingRequest.rawText,
                updatedAtEpochMillis = NOW + 1,
                outbox = outbox(conflictingRequest, secondScope),
            )
        } catch (_: Throwable) {
            failed = true
        }

        assertTrue("Expected duplicate operation_id to abort the transaction", failed)
        assertEquals(secondSession.epoch, draftStore.open(secondScope).epoch)
        assertEquals("必须在失败事务后保留的草稿", draftStore.load(secondScope)?.text)
        assertEquals(existingRequest.rawText, ObservationOutboxCodec.decodeRequest(requireNotNull(dao.readByOperationId(duplicateOperationId.toString())).payloadJson).rawText)
    }

    @Test
    fun expiredLeaseCanBeRecoveredAndOldLeaseCannotAcknowledge() = runBlocking {
        val session = draftStore.open(scope)
        val request = request(UUID.randomUUID(), "lease recovery")
        dao.submitObservation(scope.storageKey, session.epoch, request.rawText, NOW, outbox(request))

        val first = requireNotNull(
            dao.claimNextReady(
                environmentId = scope.environmentId,
                appUserId = scope.appUserId,
                nowEpochMillis = NOW,
                leaseDurationMillis = 100,
                leaseId = "lease-old",
            ),
        )
        assertEquals("lease-old", first.leaseId)

        val recovered = requireNotNull(
            dao.claimNextReady(
                environmentId = scope.environmentId,
                appUserId = scope.appUserId,
                nowEpochMillis = NOW + 101,
                leaseDurationMillis = 100,
                leaseId = "lease-new",
            ),
        )
        assertEquals("lease-new", recovered.leaseId)
        assertEquals(2, recovered.attemptCount)

        assertEquals(
            0,
            dao.acknowledge(
                localSequence = recovered.localSequence,
                leaseId = "lease-old",
                acknowledgedAtEpochMillis = NOW + 110,
                serverReceiptJson = "{}",
            ),
        )
        assertEquals(
            1,
            dao.acknowledge(
                localSequence = recovered.localSequence,
                leaseId = "lease-new",
                acknowledgedAtEpochMillis = NOW + 111,
                serverReceiptJson = "{\"receipt\":true}",
            ),
        )
        assertEquals(ObservationOutboxStatus.Acknowledged, dao.readByOperationId(request.operationId.toString())?.queueStatus)
    }

    @Test
    fun claimIsBoundToEnvironmentAndApplicationUser() = runBlocking {
        val session = draftStore.open(scope)
        val request = request(UUID.randomUUID(), "scope isolation")
        dao.submitObservation(scope.storageKey, session.epoch, request.rawText, NOW, outbox(request))

        assertNull(dao.claimNextReady("other-env", scope.appUserId, NOW, 1_000, "wrong-env"))
        assertNull(dao.claimNextReady(scope.environmentId, "other-user", NOW, 1_000, "wrong-user"))

        val claimed = dao.claimNextReady(scope.environmentId, scope.appUserId, NOW, 1_000, "correct")
        assertNotNull(claimed)
        assertEquals("correct", claimed?.leaseId)
    }

    private fun request(operationId: UUID, rawText: String) = CreateObservationRequest(
        operationId = operationId,
        organizationId = UUID.fromString(scope.organizationId),
        studentId = UUID.fromString(scope.studentId),
        subjectProfileId = UUID.fromString(scope.subjectId),
        assignmentId = UUID.fromString("66666666-6666-6666-6666-666666666666"),
        rawText = rawText,
        clientCapturedAt = Instant.ofEpochMilli(NOW),
        clientCaptureMetadata = mapOf("surface" to "quick_capture"),
    )

    private fun outbox(
        request: CreateObservationRequest,
        targetScope: DraftScope = scope,
    ) = ObservationOutboxEntity(
        operationId = request.operationId.toString(),
        commandType = ObservationOutboxCodec.COMMAND_TYPE,
        scopeKey = targetScope.storageKey,
        environmentId = targetScope.environmentId,
        appUserId = targetScope.appUserId,
        organizationId = request.organizationId.toString(),
        payloadJson = ObservationOutboxCodec.encodeRequest(request),
        queueStatus = ObservationOutboxStatus.Pending,
        attemptCount = 0,
        lastErrorClass = null,
        nextAttemptAtEpochMillis = NOW,
        leaseId = null,
        leaseExpiresAtEpochMillis = null,
        acknowledgedAtEpochMillis = null,
        serverReceiptJson = null,
        createdAtEpochMillis = NOW,
    )

    private companion object {
        const val NOW = 1_789_632_000_000L
    }
}
