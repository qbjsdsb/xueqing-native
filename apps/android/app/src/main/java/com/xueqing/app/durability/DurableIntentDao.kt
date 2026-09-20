package com.xueqing.app.durability

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction
import java.util.UUID
import kotlinx.coroutines.flow.Flow

data class DraftDiscardResult(
    val nextEpoch: Long,
    val attachmentFileNames: List<String>,
)

@Dao
interface DurableIntentDao {
    @Insert(onConflict = OnConflictStrategy.IGNORE)
    suspend fun ensureScopeState(state: DraftScopeStateEntity): Long

    @Query("SELECT epoch FROM draft_scope_state WHERE scope_key = :scopeKey")
    suspend fun readEpoch(scopeKey: String): Long?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertDraft(entity: DraftEntity)

    @Query("DELETE FROM drafts WHERE scope_key = :scopeKey")
    suspend fun deleteDraft(scopeKey: String)

    @Query("UPDATE draft_scope_state SET epoch = :newEpoch WHERE scope_key = :scopeKey AND epoch = :expectedEpoch")
    suspend fun advanceEpoch(scopeKey: String, expectedEpoch: Long, newEpoch: Long): Int

    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insertOutbox(entity: ObservationOutboxEntity): Long

    @Query(
        """
        UPDATE attachment_staging
        SET state = :waitingState,
            parent_observation_operation_id = :operationId,
            updated_at_epoch_millis = :updatedAtEpochMillis
        WHERE scope_key = :scopeKey
          AND draft_epoch = :draftEpoch
          AND state = :stagedState
          AND parent_observation_operation_id IS NULL
        """,
    )
    suspend fun bindStagedAttachmentsToObservation(
        scopeKey: String,
        draftEpoch: Long,
        operationId: String,
        stagedState: String = AttachmentStagingState.Staged,
        waitingState: String = AttachmentStagingState.WaitingForObservation,
        updatedAtEpochMillis: Long,
    ): Int

    @Query(
        """
        SELECT local_encrypted_file_name FROM attachment_staging
        WHERE scope_key = :scopeKey
          AND draft_epoch = :draftEpoch
          AND state = :stagedState
          AND parent_observation_operation_id IS NULL
        ORDER BY attachment_id ASC
        """,
    )
    suspend fun readUnboundAttachmentFileNamesForDraft(
        scopeKey: String,
        draftEpoch: Long,
        stagedState: String = AttachmentStagingState.Staged,
    ): List<String>

    @Query(
        """
        DELETE FROM attachment_staging
        WHERE scope_key = :scopeKey
          AND draft_epoch = :draftEpoch
          AND state = :stagedState
          AND parent_observation_operation_id IS NULL
        """,
    )
    suspend fun deleteUnboundAttachmentsForDraft(
        scopeKey: String,
        draftEpoch: Long,
        stagedState: String = AttachmentStagingState.Staged,
    ): Int

    @Query("SELECT * FROM observation_outbox WHERE operation_id = :operationId LIMIT 1")
    suspend fun readByOperationId(operationId: String): ObservationOutboxEntity?

    @Query("SELECT * FROM observation_outbox WHERE local_sequence = :localSequence LIMIT 1")
    suspend fun readByLocalSequence(localSequence: Long): ObservationOutboxEntity?

    @Query(
        """
        SELECT * FROM observation_outbox
        WHERE scope_key = :scopeKey
        ORDER BY local_sequence DESC
        LIMIT 1
        """,
    )
    suspend fun readLatestForScope(scopeKey: String): ObservationOutboxEntity?

    @Query(
        """
        SELECT * FROM observation_outbox
        WHERE scope_key = :scopeKey
        ORDER BY local_sequence DESC
        LIMIT 1
        """,
    )
    fun observeLatestForScope(scopeKey: String): Flow<ObservationOutboxEntity?>

    @Query(
        """
        SELECT MIN(
            CASE
                WHEN queue_status = 'InFlight' THEN lease_expires_at_epoch_millis
                ELSE next_attempt_at_epoch_millis
            END
        )
        FROM observation_outbox
        WHERE environment_id = :environmentId
          AND app_user_id = :appUserId
          AND queue_status IN ('Pending', 'Retry', 'InFlight')
        """,
    )
    suspend fun readNextWakeAt(
        environmentId: String,
        appUserId: String,
    ): Long?

    @Query(
        """
        SELECT * FROM observation_outbox
        WHERE environment_id = :environmentId
          AND app_user_id = :appUserId
          AND (
            (queue_status IN ('Pending', 'Retry') AND next_attempt_at_epoch_millis <= :nowEpochMillis)
            OR
            (queue_status = 'InFlight'
              AND lease_expires_at_epoch_millis IS NOT NULL
              AND lease_expires_at_epoch_millis <= :nowEpochMillis)
          )
        ORDER BY local_sequence ASC
        LIMIT 1
        """,
    )
    suspend fun selectReady(
        environmentId: String,
        appUserId: String,
        nowEpochMillis: Long,
    ): ObservationOutboxEntity?

    @Query(
        """
        UPDATE observation_outbox
        SET queue_status = 'InFlight',
            attempt_count = attempt_count + 1,
            lease_id = :leaseId,
            lease_expires_at_epoch_millis = :leaseExpiresAtEpochMillis
        WHERE local_sequence = :localSequence
          AND (
            (queue_status IN ('Pending', 'Retry') AND next_attempt_at_epoch_millis <= :nowEpochMillis)
            OR
            (queue_status = 'InFlight'
              AND lease_expires_at_epoch_millis IS NOT NULL
              AND lease_expires_at_epoch_millis <= :nowEpochMillis)
          )
        """,
    )
    suspend fun markClaimed(
        localSequence: Long,
        leaseId: String,
        leaseExpiresAtEpochMillis: Long,
        nowEpochMillis: Long,
    ): Int

    @Query(
        """
        UPDATE observation_outbox
        SET queue_status = 'Acknowledged',
            acknowledged_at_epoch_millis = :acknowledgedAtEpochMillis,
            server_receipt_json = :serverReceiptJson,
            last_error_class = NULL,
            lease_id = NULL,
            lease_expires_at_epoch_millis = NULL
        WHERE local_sequence = :localSequence
          AND queue_status = 'InFlight'
          AND lease_id = :leaseId
        """,
    )
    suspend fun acknowledge(
        localSequence: Long,
        leaseId: String,
        acknowledgedAtEpochMillis: Long,
        serverReceiptJson: String,
    ): Int

    @Query(
        """
        UPDATE observation_outbox
        SET queue_status = 'Retry',
            last_error_class = :errorClass,
            next_attempt_at_epoch_millis = :nextAttemptAtEpochMillis,
            lease_id = NULL,
            lease_expires_at_epoch_millis = NULL
        WHERE local_sequence = :localSequence
          AND queue_status = 'InFlight'
          AND lease_id = :leaseId
        """,
    )
    suspend fun retry(
        localSequence: Long,
        leaseId: String,
        errorClass: String,
        nextAttemptAtEpochMillis: Long,
    ): Int

    @Query(
        """
        UPDATE observation_outbox
        SET queue_status = 'DeadLetter',
            last_error_class = :errorClass,
            lease_id = NULL,
            lease_expires_at_epoch_millis = NULL
        WHERE local_sequence = :localSequence
          AND queue_status = 'InFlight'
          AND lease_id = :leaseId
        """,
    )
    suspend fun deadLetter(
        localSequence: Long,
        leaseId: String,
        errorClass: String,
    ): Int

    @Transaction
    suspend fun discardDraft(
        scopeKey: String,
        expectedEpoch: Long,
    ): DraftDiscardResult? {
        ensureScopeState(DraftScopeStateEntity(scopeKey = scopeKey, epoch = 0))
        if (readEpoch(scopeKey) != expectedEpoch) {
            return null
        }

        val attachmentFileNames = readUnboundAttachmentFileNamesForDraft(
            scopeKey = scopeKey,
            draftEpoch = expectedEpoch,
        )
        deleteUnboundAttachmentsForDraft(
            scopeKey = scopeKey,
            draftEpoch = expectedEpoch,
        )
        deleteDraft(scopeKey)

        val nextEpoch = Math.addExact(expectedEpoch, 1)
        if (advanceEpoch(scopeKey, expectedEpoch, nextEpoch) != 1) {
            error("Draft epoch changed during discard transaction")
        }

        return DraftDiscardResult(
            nextEpoch = nextEpoch,
            attachmentFileNames = attachmentFileNames,
        )
    }

    @Transaction
    suspend fun submitObservation(
        scopeKey: String,
        expectedEpoch: Long,
        finalText: String,
        updatedAtEpochMillis: Long,
        outbox: ObservationOutboxEntity,
    ): Long? {
        require(outbox.scopeKey == scopeKey) { "Outbox scope must match the draft scope" }
        require(outbox.queueStatus == ObservationOutboxStatus.Pending) { "New outbox intent must be pending" }
        require(outbox.operationId.isNotBlank()) { "Operation ID must not be blank" }
        require(finalText.isNotBlank()) { "Observation text must not be blank" }

        ensureScopeState(DraftScopeStateEntity(scopeKey = scopeKey, epoch = 0))
        if (readEpoch(scopeKey) != expectedEpoch) {
            return null
        }

        // The submit snapshot and queue insert live in one Room transaction.
        // This deliberately overwrites a potentially older autosave with the
        // latest committed UI text before the draft is retired.
        upsertDraft(
            DraftEntity(
                scopeKey = scopeKey,
                epoch = expectedEpoch,
                text = finalText,
                updatedAtEpochMillis = updatedAtEpochMillis,
            ),
        )
        insertOutbox(outbox)
        bindStagedAttachmentsToObservation(
            scopeKey = scopeKey,
            draftEpoch = expectedEpoch,
            operationId = outbox.operationId,
            updatedAtEpochMillis = updatedAtEpochMillis,
        )
        deleteDraft(scopeKey)

        val nextEpoch = Math.addExact(expectedEpoch, 1)
        if (advanceEpoch(scopeKey, expectedEpoch, nextEpoch) != 1) {
            error("Draft epoch changed during submit transaction")
        }
        return nextEpoch
    }

    @Transaction
    suspend fun claimNextReady(
        environmentId: String,
        appUserId: String,
        nowEpochMillis: Long,
        leaseDurationMillis: Long,
        leaseId: String = UUID.randomUUID().toString(),
    ): ObservationOutboxEntity? {
        require(environmentId.isNotBlank())
        require(appUserId.isNotBlank())
        require(leaseDurationMillis > 0)
        require(leaseId.isNotBlank())

        val candidate = selectReady(environmentId, appUserId, nowEpochMillis) ?: return null
        val leaseExpiry = Math.addExact(nowEpochMillis, leaseDurationMillis)
        if (
            markClaimed(
                localSequence = candidate.localSequence,
                leaseId = leaseId,
                leaseExpiresAtEpochMillis = leaseExpiry,
                nowEpochMillis = nowEpochMillis,
            ) != 1
        ) {
            return null
        }
        return requireNotNull(readByLocalSequence(candidate.localSequence))
    }
}
