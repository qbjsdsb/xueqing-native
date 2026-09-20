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
