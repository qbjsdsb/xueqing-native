package com.xueqing.app.durability

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import kotlinx.coroutines.flow.Flow

@Dao
interface AttachmentStagingDao {
    @Insert(onConflict = OnConflictStrategy.ABORT)
    suspend fun insert(entity: AttachmentStagingEntity)

    @Query("SELECT * FROM attachment_staging WHERE attachment_id = :attachmentId LIMIT 1")
    suspend fun read(attachmentId: String): AttachmentStagingEntity?

    @Query(
        """
        SELECT * FROM attachment_staging
        WHERE scope_key = :scopeKey
          AND draft_epoch = :draftEpoch
        ORDER BY created_at_epoch_millis ASC, attachment_id ASC
        """,
    )
    suspend fun readForDraft(scopeKey: String, draftEpoch: Long): List<AttachmentStagingEntity>

    @Query(
        """
        SELECT * FROM attachment_staging
        WHERE scope_key = :scopeKey
          AND draft_epoch = :draftEpoch
        ORDER BY created_at_epoch_millis ASC, attachment_id ASC
        """,
    )
    fun observeForDraft(scopeKey: String, draftEpoch: Long): Flow<List<AttachmentStagingEntity>>

    @Query(
        """
        SELECT * FROM attachment_staging
        WHERE environment_id = :environmentId
          AND app_user_id = :appUserId
          AND (
            (state = :uploadPendingState AND (last_error_class IS NULL OR updated_at_epoch_millis <= :retryCutoffEpochMillis))
            OR (state = :uploadingState AND updated_at_epoch_millis <= :retryCutoffEpochMillis)
            OR state = :uploadedUncommittedState
            OR state = :commitPendingState
            OR (state = :commitUnknownState AND updated_at_epoch_millis <= :retryCutoffEpochMillis)
            OR state = :committedState
          )
        ORDER BY created_at_epoch_millis ASC, attachment_id ASC
        LIMIT 1
        """,
    )
    suspend fun readNextSyncCandidate(
        environmentId: String,
        appUserId: String,
        retryCutoffEpochMillis: Long,
        uploadPendingState: String = AttachmentStagingState.UploadPending,
        uploadingState: String = AttachmentStagingState.Uploading,
        uploadedUncommittedState: String = AttachmentStagingState.UploadedUncommitted,
        commitPendingState: String = AttachmentStagingState.CommitPending,
        commitUnknownState: String = AttachmentStagingState.CommitResultUnknown,
        committedState: String = AttachmentStagingState.Committed,
    ): AttachmentStagingEntity?

    @Query(
        """
        SELECT MIN(
            CASE
                WHEN state = :uploadedUncommittedState
                     OR state = :commitPendingState
                     OR state = :committedState
                     OR (state = :uploadPendingState AND last_error_class IS NULL)
                    THEN :nowEpochMillis
                ELSE updated_at_epoch_millis + :retryDelayMillis
            END
        )
        FROM attachment_staging
        WHERE environment_id = :environmentId
          AND app_user_id = :appUserId
          AND (
            state = :uploadPendingState
            OR state = :uploadingState
            OR state = :uploadedUncommittedState
            OR state = :commitPendingState
            OR state = :commitUnknownState
            OR state = :committedState
          )
        """,
    )
    suspend fun readNextSyncWakeAt(
        environmentId: String,
        appUserId: String,
        nowEpochMillis: Long,
        retryDelayMillis: Long,
        uploadPendingState: String = AttachmentStagingState.UploadPending,
        uploadingState: String = AttachmentStagingState.Uploading,
        uploadedUncommittedState: String = AttachmentStagingState.UploadedUncommitted,
        commitPendingState: String = AttachmentStagingState.CommitPending,
        commitUnknownState: String = AttachmentStagingState.CommitResultUnknown,
        committedState: String = AttachmentStagingState.Committed,
    ): Long?

    @Query(
        """
        UPDATE attachment_staging
        SET state = :newState,
            last_error_class = :lastErrorClass,
            updated_at_epoch_millis = :updatedAtEpochMillis
        WHERE attachment_id = :attachmentId
          AND state = :expectedState
        """,
    )
    suspend fun transitionState(
        attachmentId: String,
        expectedState: String,
        newState: String,
        lastErrorClass: String?,
        updatedAtEpochMillis: Long,
    ): Int

    @Query(
        """
        DELETE FROM attachment_staging
        WHERE attachment_id = :attachmentId
          AND state = :expectedState
        """,
    )
    suspend fun deleteIfState(
        attachmentId: String,
        expectedState: String,
    ): Int

    @Query("SELECT local_encrypted_file_name FROM attachment_staging")
    suspend fun readReferencedFileNames(): List<String>

    @Query(
        """
        DELETE FROM attachment_staging
        WHERE scope_key = :scopeKey
          AND draft_epoch = :draftEpoch
          AND state = :stagedState
          AND parent_observation_operation_id IS NULL
        """,
    )
    suspend fun deleteUnboundForDraft(
        scopeKey: String,
        draftEpoch: Long,
        stagedState: String = AttachmentStagingState.Staged,
    ): Int
}
