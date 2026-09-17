package com.xueqing.app.durability

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.application.observation.CreateObservationRequest
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class EncryptedOutboxReopenInstrumentedTest {
    private lateinit var context: Context

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        DraftDatabase.purgeLocalEncryptedData(context)
    }

    @After
    fun tearDown() {
        DraftDatabase.purgeLocalEncryptedData(context)
    }

    @Test
    fun deterministicRejectionAndOriginalPayloadSurviveEncryptedDatabaseReopen() = runBlocking {
        val scope = scope()
        val operationId = UUID.randomUUID()
        val request = request(operationId, SENTINEL)

        val first = DraftDatabase.create(context)
        val store = DraftStore(first.draftDao()) { NOW }
        val session = store.open(scope)
        val dao = first.durableIntentDao()
        requireNotNull(
            dao.submitObservation(
                scopeKey = scope.storageKey,
                expectedEpoch = session.epoch,
                finalText = request.rawText,
                updatedAtEpochMillis = NOW,
                outbox = outbox(scope, request),
            ),
        )
        val claimed = requireNotNull(
            dao.claimNextReady(
                environmentId = ENVIRONMENT_ID,
                appUserId = APP_USER_ID,
                nowEpochMillis = NOW,
                leaseDurationMillis = 10_000,
                leaseId = "rejection-lease",
            ),
        )
        assertEquals(
            1,
            dao.deadLetter(
                localSequence = claimed.localSequence,
                leaseId = "rejection-lease",
                errorClass = "XQ_TEACHER_ASSIGNMENT_REQUIRED",
            ),
        )
        first.close()

        val reopened = DraftDatabase.create(context)
        val persisted = reopened.durableIntentDao().readByOperationId(operationId.toString())
        assertNotNull(persisted)
        assertEquals(ObservationOutboxStatus.DeadLetter, persisted?.queueStatus)
        assertEquals("XQ_TEACHER_ASSIGNMENT_REQUIRED", persisted?.lastErrorClass)
        assertEquals(SENTINEL, ObservationOutboxCodec.decodeRequest(requireNotNull(persisted).payloadJson).rawText)
        reopened.close()
    }

    @Test
    fun pendingAndExpiredInFlightIntentSurviveEncryptedDatabaseReopen() = runBlocking {
        val scope = scope()
        val operationId = UUID.randomUUID()
        val request = request(operationId, PENDING_SENTINEL)

        val first = DraftDatabase.create(context)
        val firstStore = DraftStore(first.draftDao()) { NOW }
        val firstSession = firstStore.open(scope)
        requireNotNull(
            first.durableIntentDao().submitObservation(
                scopeKey = scope.storageKey,
                expectedEpoch = firstSession.epoch,
                finalText = request.rawText,
                updatedAtEpochMillis = NOW,
                outbox = outbox(scope, request),
            ),
        )
        first.close()

        val pendingReopen = DraftDatabase.create(context)
        val pendingDao = pendingReopen.durableIntentDao()
        val pending = requireNotNull(pendingDao.readByOperationId(operationId.toString()))
        assertEquals(ObservationOutboxStatus.Pending, pending.queueStatus)
        assertEquals(PENDING_SENTINEL, ObservationOutboxCodec.decodeRequest(pending.payloadJson).rawText)

        val firstClaim = requireNotNull(
            pendingDao.claimNextReady(
                environmentId = ENVIRONMENT_ID,
                appUserId = APP_USER_ID,
                nowEpochMillis = NOW,
                leaseDurationMillis = 100,
                leaseId = "lease-before-death",
            ),
        )
        assertEquals(ObservationOutboxStatus.InFlight, firstClaim.queueStatus)
        assertEquals(1, firstClaim.attemptCount)
        pendingReopen.close()

        val recoveredReopen = DraftDatabase.create(context)
        val recovered = requireNotNull(
            recoveredReopen.durableIntentDao().claimNextReady(
                environmentId = ENVIRONMENT_ID,
                appUserId = APP_USER_ID,
                nowEpochMillis = NOW + 101,
                leaseDurationMillis = 100,
                leaseId = "lease-after-restart",
            ),
        )
        assertEquals(operationId.toString(), recovered.operationId)
        assertEquals(ObservationOutboxStatus.InFlight, recovered.queueStatus)
        assertEquals("lease-after-restart", recovered.leaseId)
        assertEquals(2, recovered.attemptCount)
        assertEquals(PENDING_SENTINEL, ObservationOutboxCodec.decodeRequest(recovered.payloadJson).rawText)
        recoveredReopen.close()
    }

    private fun scope() = DraftScope(
        environmentId = ENVIRONMENT_ID,
        appUserId = APP_USER_ID,
        organizationId = ORGANIZATION_ID,
        studentId = STUDENT_ID,
        subjectId = SUBJECT_PROFILE_ID,
        contextId = "quick-capture",
    )

    private fun request(operationId: UUID, rawText: String) = CreateObservationRequest(
        operationId = operationId,
        organizationId = UUID.fromString(ORGANIZATION_ID),
        studentId = UUID.fromString(STUDENT_ID),
        subjectProfileId = UUID.fromString(SUBJECT_PROFILE_ID),
        assignmentId = UUID.fromString(ASSIGNMENT_ID),
        rawText = rawText,
        clientCapturedAt = Instant.ofEpochMilli(NOW),
    )

    private fun outbox(
        scope: DraftScope,
        request: CreateObservationRequest,
    ) = ObservationOutboxEntity(
        operationId = request.operationId.toString(),
        commandType = ObservationOutboxCodec.COMMAND_TYPE,
        scopeKey = scope.storageKey,
        environmentId = ENVIRONMENT_ID,
        appUserId = APP_USER_ID,
        organizationId = ORGANIZATION_ID,
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
        const val ENVIRONMENT_ID = "ci-encrypted-outbox"
        const val APP_USER_ID = "10000000-0000-0000-0000-000000000001"
        const val ORGANIZATION_ID = "20000000-0000-0000-0000-000000000001"
        const val STUDENT_ID = "30000000-0000-0000-0000-000000000001"
        const val SUBJECT_PROFILE_ID = "40000000-0000-0000-0000-000000000001"
        const val ASSIGNMENT_ID = "50000000-0000-0000-0000-000000000001"
        const val SENTINEL = "确定性拒绝后重启：原始课堂观察必须仍然保留。"
        const val PENDING_SENTINEL = "待同步记录在进程死亡后必须继续使用同一个 operation。"
    }
}
