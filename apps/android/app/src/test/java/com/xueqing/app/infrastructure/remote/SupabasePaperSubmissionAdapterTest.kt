package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.paper.CreatePaperSubmissionRejection
import com.xueqing.app.application.paper.CreatePaperSubmissionRequest
import com.xueqing.app.application.paper.PaperKind
import com.xueqing.app.application.paper.PaperPageInput
import com.xueqing.app.application.paper.PaperSubmissionResult
import com.xueqing.app.application.paper.PaperSubmissionUnknownReason
import java.time.LocalDate
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SupabasePaperSubmissionAdapterTest {
    @Test
    fun create_encodesMultiPagePayloadAndValidatesServerReceipt() {
        val request = sampleRequest()
        val paperId = UUID.fromString("88888888-0000-0000-0000-000000000001")
        val responseBody = """
            {
              "contract": "create_paper_submission_v1",
              "operation_id": "${request.operationId}",
              "paper_submission_id": "$paperId",
              "organization_id": "${request.organizationId}",
              "student_id": "${request.studentId}",
              "subject_profile_id": "${request.subjectProfileId}",
              "assignment_id": "${request.assignmentId}",
              "paper_date": "2026-11-22",
              "paper_kind": "unit_test",
              "title": "第3单元小数除法周测",
              "confirmation_state": "awaiting_extraction",
              "version": 1,
              "page_count": 2,
              "server_committed_at": "2026-11-22T04:00:00Z"
            }
        """.trimIndent()
        val transport = RecordingRpcTransport(RpcTransportResult.Response(200, responseBody))
        val adapter = SupabasePaperSubmissionAdapter(
            sessionTokenSource = SessionTokenSource { "user-jwt-token" },
            transport = transport,
        )

        val result = adapter.create(request)

        assertTrue(result is PaperSubmissionResult.Accepted)
        val accepted = result as PaperSubmissionResult.Accepted
        assertEquals(paperId, accepted.receipt.paperSubmissionId)
        assertEquals(2, accepted.receipt.pageCount)
        assertEquals(PaperKind.UnitTest, accepted.receipt.paperKind)
        assertEquals("awaiting_extraction", accepted.receipt.confirmationState)

        val call = transport.calls.single()
        assertEquals("create_paper_submission_v1", call.functionName)
        assertEquals("user-jwt-token", call.accessToken)
        val body = Json.parseToJsonElement(call.bodyJson) as JsonObject
        assertEquals("2026-11-22", (body["p_paper_date"] as JsonPrimitive).content)
        assertEquals("unit_test", (body["p_paper_kind"] as JsonPrimitive).content)
        val pages = body["p_pages"] as JsonArray
        assertEquals(2, pages.size)
        val page1 = pages[0] as JsonObject
        val page2 = pages[1] as JsonObject
        assertEquals("1", (page1["page_index"] as JsonPrimitive).content)
        assertEquals("true", (page1["header_redacted"] as JsonPrimitive).content)
        assertEquals("2", (page2["page_index"] as JsonPrimitive).content)
        assertEquals("false", (page2["header_redacted"] as JsonPrimitive).content)
    }

    @Test
    fun create_failsClosedWithoutSessionTokenBeforeNetworkCall() {
        val transport = RecordingRpcTransport(RpcTransportResult.Response(200, "{}"))
        val adapter = SupabasePaperSubmissionAdapter(
            sessionTokenSource = SessionTokenSource { null },
            transport = transport,
        )

        val result = adapter.create(sampleRequest())

        assertTrue(result is PaperSubmissionResult.AuthenticationRequired)
        assertTrue(transport.calls.isEmpty())
    }

    @Test
    fun create_mapsDomainRejectionAndTransientErrors() {
        val request = sampleRequest()
        val rejected = SupabasePaperSubmissionAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RecordingRpcTransport(
                RpcTransportResult.Response(
                    400,
                    """{"code":"P0001","message":"XQ_PAPER_ATTACHMENT_SCOPE_MISMATCH"}""",
                ),
            ),
        ).create(request)
        assertTrue(rejected is PaperSubmissionResult.Rejected)
        assertEquals(
            CreatePaperSubmissionRejection.AttachmentScopeMismatch,
            (rejected as PaperSubmissionResult.Rejected).rejection,
        )

        val timeout = SupabasePaperSubmissionAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RecordingRpcTransport(RpcTransportResult.Timeout),
        ).create(request)
        assertTrue(timeout is PaperSubmissionResult.UnknownResult)
        assertEquals(
            PaperSubmissionUnknownReason.Timeout,
            (timeout as PaperSubmissionResult.UnknownResult).reason,
        )
    }

    private fun sampleRequest(): CreatePaperSubmissionRequest =
        CreatePaperSubmissionRequest(
            operationId = UUID.fromString("99900000-0000-0000-0000-000000000001"),
            organizationId = UUID.fromString("20000000-0000-0000-0000-000000000001"),
            studentId = UUID.fromString("30000000-0000-0000-0000-000000000001"),
            subjectProfileId = UUID.fromString("40000000-0000-0000-0000-000000000001"),
            assignmentId = UUID.fromString("50000000-0000-0000-0000-000000000001"),
            paperDate = LocalDate.of(2026, 11, 22),
            paperKind = PaperKind.UnitTest,
            title = "第3单元小数除法周测",
            sourceObservationId = UUID.fromString("60000000-0000-0000-0000-000000000001"),
            pages = listOf(
                PaperPageInput(
                    pageIndex = 2,
                    attachmentId = UUID.fromString("70000000-0000-0000-0000-000000000002"),
                    headerRedacted = false,
                ),
                PaperPageInput(
                    pageIndex = 1,
                    attachmentId = UUID.fromString("70000000-0000-0000-0000-000000000001"),
                    headerRedacted = true,
                ),
            ),
        )

    private class RecordingRpcTransport(
        private val response: RpcTransportResult,
    ) : RpcTransport {
        val calls = mutableListOf<RpcCall>()

        override fun post(call: RpcCall): RpcTransportResult {
            calls += call
            return response
        }
    }
}
