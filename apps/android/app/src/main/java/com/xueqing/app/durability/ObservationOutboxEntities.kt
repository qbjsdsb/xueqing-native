package com.xueqing.app.durability

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

object ObservationOutboxStatus {
    const val Pending = "Pending"
    const val Retry = "Retry"
    const val InFlight = "InFlight"
    const val Acknowledged = "Acknowledged"
    const val DeadLetter = "DeadLetter"
}

@Entity(
    tableName = "observation_outbox",
    indices = [
        Index(value = ["operation_id"], unique = true),
        Index(value = ["queue_status", "next_attempt_at_epoch_millis"]),
        Index(value = ["environment_id", "app_user_id", "local_sequence"]),
        Index(value = ["scope_key"]),
    ],
)
data class ObservationOutboxEntity(
    @PrimaryKey(autoGenerate = true)
    @ColumnInfo(name = "local_sequence")
    val localSequence: Long = 0,
    @ColumnInfo(name = "operation_id")
    val operationId: String,
    @ColumnInfo(name = "command_type")
    val commandType: String,
    @ColumnInfo(name = "scope_key")
    val scopeKey: String,
    @ColumnInfo(name = "environment_id")
    val environmentId: String,
    @ColumnInfo(name = "app_user_id")
    val appUserId: String,
    @ColumnInfo(name = "organization_id")
    val organizationId: String,
    @ColumnInfo(name = "payload_json")
    val payloadJson: String,
    @ColumnInfo(name = "queue_status")
    val queueStatus: String,
    @ColumnInfo(name = "attempt_count")
    val attemptCount: Int,
    @ColumnInfo(name = "last_error_class")
    val lastErrorClass: String?,
    @ColumnInfo(name = "next_attempt_at_epoch_millis")
    val nextAttemptAtEpochMillis: Long,
    @ColumnInfo(name = "lease_id")
    val leaseId: String?,
    @ColumnInfo(name = "lease_expires_at_epoch_millis")
    val leaseExpiresAtEpochMillis: Long?,
    @ColumnInfo(name = "acknowledged_at_epoch_millis")
    val acknowledgedAtEpochMillis: Long?,
    @ColumnInfo(name = "server_receipt_json")
    val serverReceiptJson: String?,
    @ColumnInfo(name = "created_at_epoch_millis")
    val createdAtEpochMillis: Long,
)
