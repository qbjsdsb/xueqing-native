package com.xueqing.app.durability

import androidx.room.ColumnInfo
import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

object AttachmentStagingState {
    const val Staged = "Staged"
    const val WaitingForObservation = "WaitingForObservation"
    const val UploadPending = "UploadPending"
    const val Uploading = "Uploading"
    const val UploadedUncommitted = "UploadedUncommitted"
    const val UploadRejected = "UploadRejected"
    const val CommitPending = "CommitPending"
    const val CommitResultUnknown = "CommitResultUnknown"
    const val CommitRejected = "CommitRejected"
    const val Committed = "Committed"
}

@Entity(
    tableName = "attachment_staging",
    indices = [
        Index(value = ["scope_key", "draft_epoch"]),
        Index(value = ["state", "updated_at_epoch_millis"]),
        Index(value = ["parent_observation_operation_id"]),
        Index(value = ["environment_id", "app_user_id", "state"]),
        Index(value = ["local_encrypted_file_name"], unique = true),
    ],
)
data class AttachmentStagingEntity(
    @PrimaryKey
    @ColumnInfo(name = "attachment_id")
    val attachmentId: String,
    @ColumnInfo(name = "scope_key")
    val scopeKey: String,
    @ColumnInfo(name = "draft_epoch")
    val draftEpoch: Long,
    @ColumnInfo(name = "environment_id")
    val environmentId: String,
    @ColumnInfo(name = "app_user_id")
    val appUserId: String,
    @ColumnInfo(name = "organization_id")
    val organizationId: String,
    @ColumnInfo(name = "student_id")
    val studentId: String,
    @ColumnInfo(name = "subject_profile_id")
    val subjectProfileId: String,
    @ColumnInfo(name = "assignment_id")
    val assignmentId: String,
    @ColumnInfo(name = "local_encrypted_file_name")
    val localEncryptedFileName: String,
    @ColumnInfo(name = "content_type")
    val contentType: String,
    @ColumnInfo(name = "byte_size")
    val byteSize: Long,
    @ColumnInfo(name = "state")
    val state: String,
    @ColumnInfo(name = "parent_observation_operation_id")
    val parentObservationOperationId: String?,
    @ColumnInfo(name = "authoritative_observation_id")
    val authoritativeObservationId: String?,
    @ColumnInfo(name = "remote_object_name")
    val remoteObjectName: String?,
    @ColumnInfo(name = "attachment_commit_operation_id")
    val attachmentCommitOperationId: String?,
    @ColumnInfo(name = "last_error_class")
    val lastErrorClass: String?,
    @ColumnInfo(name = "created_at_epoch_millis")
    val createdAtEpochMillis: Long,
    @ColumnInfo(name = "updated_at_epoch_millis")
    val updatedAtEpochMillis: Long,
)
