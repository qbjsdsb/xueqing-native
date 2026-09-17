package com.xueqing.app.durability

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.PrimaryKey

@Entity(tableName = "draft_scope_state")
data class DraftScopeStateEntity(
    @PrimaryKey
    @ColumnInfo(name = "scope_key")
    val scopeKey: String,
    @ColumnInfo(name = "epoch")
    val epoch: Long,
)

@Entity(tableName = "drafts")
data class DraftEntity(
    @PrimaryKey
    @ColumnInfo(name = "scope_key")
    val scopeKey: String,
    @ColumnInfo(name = "epoch")
    val epoch: Long,
    @ColumnInfo(name = "text")
    val text: String,
    @ColumnInfo(name = "updated_at_epoch_millis")
    val updatedAtEpochMillis: Long,
)

data class DraftSnapshot(
    val epoch: Long,
    val text: String,
    val updatedAtEpochMillis: Long,
)
