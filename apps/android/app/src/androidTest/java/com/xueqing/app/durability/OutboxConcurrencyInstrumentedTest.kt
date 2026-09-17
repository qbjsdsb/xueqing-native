package com.xueqing.app.durability

import android.content.Context
import androidx.room.Room
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.application.observation.CreateObservationRequest
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OutboxConcurrencyInstrumentedTest {
    private lateinit var database: DraftDatabase

    @Before
    fun setUp() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        database = Room.inMemoryDatabaseBuilder(context, DraftDatabase::class.java)
            .allowMainThreadQueries()
            .build()
    }

    @After
    fun tearDown() {
        database.close()
    }

    @Test
    fun concurrentClaimersProduceExactlyOneCurrentLease() = runBlocking {
        val dao = database.durableIntentDao()
        val draftStore = DraftStore(database.draftDao()) { NOW }
        val scope = DraftScope(
            environmentId = ENVIRONMENT_ID,
            appUserId = APP_USER_ID,
            organizationId = ORGANIZATION_ID,
            studentId = STUDENT_ID,
            subjectId = SUBJECT_PROFILE_ID,
            contextId = "quick-capture",
        )
        val session = draftStore.open(scope)
        val request = CreateObservationRequest(
            operationId = UUID.randomUUID(),
            organizationId = UUID.fromString(ORGANIZATION_ID),
            studentId = UUID.fromString(STUDENT_ID),
            subjectProfileId = UUID.fromString(SUBJECT_PROFILE_ID),
            assignmentId = UUID.fromString(ASSIGNMENT_ID),
            rawText = "并发 Worker 只能有一个拿到 lease",
            clientCapturedAt = Instant.ofEpochMilli(NOW),
        )
        val outbox = ObservationOutboxEntity(
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
        requireNotNull(
            dao.submitObservation(
                scopeKey = scope.storageKey,
                expectedEpoch = session.epoch,
                finalText = request.rawText,
                updatedAtEpochMillis = NOW,
                outbox = outbox,
            ),
        )

        val claims = coroutineScope {
            (1..16).map { index ->
                async {
                    dao.claimNextReady(
                        environmentId = ENVIRONMENT_ID,
                        appUserId = APP_USER_ID,
                        nowEpochMillis = NOW,
                        leaseDurationMillis = 10_000,
                        leaseId = "lease-$index",
                    )
                }
            }.awaitAll()
        }

        assertEquals(1, claims.count { it != null })
        val persisted = requireNotNull(dao.readByOperationId(request.operationId.toString()))
        assertEquals(ObservationOutboxStatus.InFlight, persisted.queueStatus)
        assertEquals(1, persisted.attemptCount)
    }

    private companion object {
        const val NOW = 1_789_632_000_000L
        const val ENVIRONMENT_ID = "ci"
        const val APP_USER_ID = "10000000-0000-0000-0000-000000000001"
        const val ORGANIZATION_ID = "20000000-0000-0000-0000-000000000001"
        const val STUDENT_ID = "30000000-0000-0000-0000-000000000001"
        const val SUBJECT_PROFILE_ID = "40000000-0000-0000-0000-000000000001"
        const val ASSIGNMENT_ID = "50000000-0000-0000-0000-000000000001"
    }
}
