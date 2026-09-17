package com.xueqing.app.durability

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Transaction

@Dao
interface DraftDao {
    @Insert(onConflict = OnConflictStrategy.IGNORE)
    suspend fun ensureScopeState(state: DraftScopeStateEntity): Long

    @Query("SELECT epoch FROM draft_scope_state WHERE scope_key = :scopeKey")
    suspend fun readEpoch(scopeKey: String): Long?

    @Query("SELECT * FROM drafts WHERE scope_key = :scopeKey")
    suspend fun readDraft(scopeKey: String): DraftEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertDraft(entity: DraftEntity)

    @Query("DELETE FROM drafts WHERE scope_key = :scopeKey")
    suspend fun deleteDraft(scopeKey: String)

    @Query("UPDATE draft_scope_state SET epoch = :newEpoch WHERE scope_key = :scopeKey AND epoch = :expectedEpoch")
    suspend fun advanceEpoch(scopeKey: String, expectedEpoch: Long, newEpoch: Long): Int

    @Transaction
    suspend fun open(scopeKey: String): Pair<Long, DraftEntity?> {
        ensureScopeState(DraftScopeStateEntity(scopeKey = scopeKey, epoch = 0))
        val epoch = requireNotNull(readEpoch(scopeKey))
        val draft = readDraft(scopeKey)?.takeIf { it.epoch == epoch }
        return epoch to draft
    }

    @Transaction
    suspend fun save(
        scopeKey: String,
        expectedEpoch: Long,
        text: String,
        updatedAtEpochMillis: Long,
    ): Boolean {
        ensureScopeState(DraftScopeStateEntity(scopeKey = scopeKey, epoch = 0))
        if (readEpoch(scopeKey) != expectedEpoch) {
            return false
        }

        upsertDraft(
            DraftEntity(
                scopeKey = scopeKey,
                epoch = expectedEpoch,
                text = text,
                updatedAtEpochMillis = updatedAtEpochMillis,
            ),
        )
        return true
    }

    @Transaction
    suspend fun discard(scopeKey: String, expectedEpoch: Long): Long? {
        ensureScopeState(DraftScopeStateEntity(scopeKey = scopeKey, epoch = 0))
        if (readEpoch(scopeKey) != expectedEpoch) {
            return null
        }

        deleteDraft(scopeKey)
        val nextEpoch = Math.addExact(expectedEpoch, 1)
        if (advanceEpoch(scopeKey, expectedEpoch, nextEpoch) != 1) {
            error("Draft epoch changed during discard transaction")
        }
        return nextEpoch
    }
}
