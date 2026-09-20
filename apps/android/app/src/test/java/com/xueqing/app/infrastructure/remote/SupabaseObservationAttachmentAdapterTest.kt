package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.attachment.AttachmentCommitResult
import com.xueqing.app.application.attachment.AttachmentCommitUnknownReason
import com.xueqing.app.application.attachment.AttachmentUploadRejection
import com.xueqing.app.application.attachment.AttachmentUploadResult
import com.xueqing.app.application.attachment.CommitObservationAttachmentRejection
import com.xueqing.app.application.attachment.CommitObservationAttachmentRequest
import com.xueqing.app.application.attachment.ObservationAttachmentUploadRequest
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SupabaseObservationAttachmentAdapterTest {
    @Test
    fun storage_upload_forwards_exact_private_object_without_upsert_semantics_in_application() {
        val request = uploadRequest()
        val body = byteArrayOf(1, 2, 3, 4)
        val transport = RecordingStorageTransport(StorageTransportResult.Response(200, "{}"))
        val adapter = SupabaseObservationAttachmentStorageAdapter(
            SessionTokenSource { "session-token" },
            transport,
        )

        val result = adapter.upload(request, body)

        assertTrue(result is AttachmentUploadResult.Uploaded)
        val call = transport.calls.single()
        assertEquals("teaching-attachments-v1", call.bucketId)
        assertEquals(request.objectName, call.objectName)
        assertEquals(request.contentType, call.contentType)
        assertEquals("session-token", call.accessToken)
        assertTrue(body.contentEquals(call.body))
    }

    @Test
    fun storage_conflict_is_recoverable_as_already_present_and_size_mismatch_fails_locally() {
        val request = uploadRequest()
        val conflictTransport = RecordingStorageTransport(StorageTransportResult.Response(409, "duplicate"))
        val adapter = SupabaseObservationAttachmentStorageAdapter(
            SessionTokenSource { "session-token" },
            conflictTransport,
        )

        assertTrue(adapter.upload(request, byteArrayOf(1, 2, 3, 4)) is AttachmentUploadResult.AlreadyPresent)

        val structuredDuplicate = SupabaseObservationAttachmentStorageAdapter(
            SessionTokenSource { "session-token" },
            RecordingStorageTransport(
                StorageTransportResult.Response(
                    400,
                    """{"statusCode":"409","error":"Duplicate","message":"The resource already exists"}""",
                ),
            ),
        ).upload(request, byteArrayOf(1, 2, 3, 4))
        assertTrue(structuredDuplicate is AttachmentUploadResult.AlreadyPresent)

        val mismatchTransport = RecordingStorageTransport(StorageTransportResult.Response(200, "{}"))
        val mismatchAdapter = SupabaseObservationAttachmentStorageAdapter(
            SessionTokenSource { "session-token" },
            mismatchTransport,
        )
        val mismatch = mismatchAdapter.upload(request, byteArrayOf(1, 2, 3))
        assertTrue(mismatch is AttachmentUploadResult.Rejected)
        assertEquals(
            AttachmentUploadRejection.LocalContentMismatch,
            (mismatch as AttachmentUploadResult.Rejected).rejection,
        )
        assertTrue(mismatchTransport.calls.isEmpty())
    }

    @Test
    fun commit_uses_exact_rpc_shape_and_validates_authoritative_receipt() {
        val request = commitRequest()
        val transport = RecordingRpcTransport(successResponse(request))
        val adapter = commitAdapter(transport)

        val result = adapter.commit(request)

        assertTrue(result is AttachmentCommitResult.Accepted)
        val call = transport.calls.single()
        assertEquals("commit_observation_attachment", call.functionName)
        val body = Json.parseToJsonElement(call.bodyJson) as JsonObject
        assertEquals(
            setOf(
                "p_operation_id",
                "p_organization_id",
                "p_student_id",
                "p_subject_profile_id",
                "p_assignment_id",
                "p_observation_id",
                "p_attachment_id",
            ),
            body.keys,
        )
        assertEquals(request.operationId.toString(), body.string("p_operation_id"))
        assertEquals(request.attachmentId.toString(), body.string("p_attachment_id"))

        val accepted = result as AttachmentCommitResult.Accepted
        assertEquals(request.operationId, accepted.receipt.operationId)
        assertEquals(request.attachmentId, accepted.receipt.attachmentId)
        assertEquals(request.observationId, accepted.receipt.observationId)
        assertEquals(canonicalObjectName(request), accepted.receipt.objectName)
    }

    @Test
    fun every_frozen_attachment_error_maps_to_typed_rejection() {
        val request = commitRequest()
        CommitObservationAttachmentRejection.entries.forEach { rejection ->
            val adapter = commitAdapter(
                RecordingRpcTransport(
                    RpcTransportResult.Response(
                        400,
                        """{"code":"P0001","message":"${rejection.wireCode}"}""",
                    ),
                ),
            )
            val result = adapter.commit(request)
            assertTrue(result is AttachmentCommitResult.Rejected)
            assertEquals(rejection, (result as AttachmentCommitResult.Rejected).rejection)
            assertEquals(request.operationId, result.operationId)
        }
    }

    @Test
    fun transient_rate_limit_remains_retryable_for_upload_and_commit() {
        val upload = SupabaseObservationAttachmentStorageAdapter(
            SessionTokenSource { "session-token" },
            RecordingStorageTransport(StorageTransportResult.Response(429, """{"error":"TooManyRequests"}""")),
        ).upload(uploadRequest(), byteArrayOf(1, 2, 3, 4))
        assertTrue(upload is AttachmentUploadResult.UnknownResult)

        val request = commitRequest()
        val commit = commitAdapter(
            RecordingRpcTransport(RpcTransportResult.Response(429, """{"message":"rate limited"}""")),
        ).commit(request)
        assertTrue(commit is AttachmentCommitResult.UnknownResult)
        assertEquals(request.operationId, (commit as AttachmentCommitResult.UnknownResult).operationId)
    }

    @Test
    fun malformed_success_is_unknown_because_commit_may_have_happened() {
        val request = commitRequest()
        val result = commitAdapter(
            RecordingRpcTransport(RpcTransportResult.Response(200, "{}")),
        ).commit(request)

        assertTrue(result is AttachmentCommitResult.UnknownResult)
        result as AttachmentCommitResult.UnknownResult
        assertEquals(AttachmentCommitUnknownReason.InvalidSuccessPayload, result.reason)
        assertEquals(request.operationId, result.operationId)
    }

    private fun uploadRequest() = ObservationAttachmentUploadRequest(
        objectName = "v1/org/20000000-0000-0000-0000-000000000001/" +
            "student/30000000-0000-0000-0000-000000000001/" +
            "profile/40000000-0000-0000-0000-000000000001/" +
            "observation/72000000-0000-0000-0000-000000000001/" +
            "attachment/71000000-0000-0000-0000-000000000001",
        contentType = "image/png",
        byteSize = 4,
    )

    private fun commitRequest() = CommitObservationAttachmentRequest(
        operationId = UUID.fromString("73000000-0000-0000-0000-000000000001"),
        organizationId = UUID.fromString("20000000-0000-0000-0000-000000000001"),
        studentId = UUID.fromString("30000000-0000-0000-0000-000000000001"),
        subjectProfileId = UUID.fromString("40000000-0000-0000-0000-000000000001"),
        assignmentId = UUID.fromString("50000000-0000-0000-0000-000000000001"),
        observationId = UUID.fromString("72000000-0000-0000-0000-000000000001"),
        attachmentId = UUID.fromString("71000000-0000-0000-0000-000000000001"),
    )

    private fun commitAdapter(transport: RpcTransport) =
        SupabaseCommitObservationAttachmentAdapter(
            sessionTokenSource = SessionTokenSource { "session-token" },
            transport = transport,
        )

    private fun successResponse(request: CommitObservationAttachmentRequest) =
        RpcTransportResult.Response(
            200,
            """
            {
              "command":"commit_observation_attachment_v1",
              "operation_id":"${request.operationId}",
              "attachment_id":"${request.attachmentId}",
              "observation_id":"${request.observationId}",
              "actor_app_user_id":"10000000-0000-0000-0000-000000000001",
              "organization_id":"${request.organizationId}",
              "student_id":"${request.studentId}",
              "subject_profile_id":"${request.subjectProfileId}",
              "subject_key":"chinese",
              "assignment_id":"${request.assignmentId}",
              "bucket_id":"teaching-attachments-v1",
              "object_name":"${canonicalObjectName(request)}",
              "content_type":"image/png",
              "byte_size":4,
              "server_committed_at":"2026-09-20T12:00:00Z"
            }
            """.trimIndent(),
        )

    private fun canonicalObjectName(request: CommitObservationAttachmentRequest) =
        "v1/org/${request.organizationId}" +
            "/student/${request.studentId}" +
            "/profile/${request.subjectProfileId}" +
            "/observation/${request.observationId}" +
            "/attachment/${request.attachmentId}"

    private class RecordingStorageTransport(
        var result: StorageTransportResult,
    ) : StorageTransport {
        val calls = mutableListOf<StorageUploadCall>()

        override fun upload(call: StorageUploadCall): StorageTransportResult {
            calls += call
            return result
        }
    }

    private class RecordingRpcTransport(
        var result: RpcTransportResult,
    ) : RpcTransport {
        val calls = mutableListOf<RpcCall>()

        override fun post(call: RpcCall): RpcTransportResult {
            calls += call
            return result
        }
    }

    private fun JsonObject.string(name: String): String =
        (get(name) as JsonPrimitive).content
}
