package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.paper.PaperExtractionTriggerResult
import com.xueqing.app.application.paper.TriggerPaperExtractionRequest
import java.util.UUID
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SupabasePaperExtractionAdapterTest {
    @Test
    fun trigger_callsExtractPaperEdgeFunctionAndParsesReceipt() {
        val request = sampleRequest()
        var calledFunction = ""
        var calledToken = ""
        val adapter = SupabasePaperExtractionAdapter(
            sessionTokenSource = SessionTokenSource { "jwt-user-token" },
            transport = EdgeFunctionTransport { functionName, accessToken, _ ->
                calledFunction = functionName
                calledToken = accessToken
                RpcTransportResult.Response(
                    200,
                    """{"ok":true,"contract":"extract_paper_v1","confirmation_state":"draft_ready","mistake_items_saved":3}""",
                )
            },
        )

        val result = adapter.trigger(request)

        assertEquals("extract-paper-v1", calledFunction)
        assertEquals("jwt-user-token", calledToken)
        assertTrue(result is PaperExtractionTriggerResult.Triggered)
        val triggered = result as PaperExtractionTriggerResult.Triggered
        assertEquals("draft_ready", triggered.confirmationState)
        assertEquals(3, triggered.mistakeItemsSaved)
    }

    @Test
    fun trigger_failsClosedWithoutSessionTokenAndMapsErrors() {
        val request = sampleRequest()
        var called = false
        val unauthenticated = SupabasePaperExtractionAdapter(
            sessionTokenSource = SessionTokenSource { null },
            transport = EdgeFunctionTransport { _, _, _ ->
                called = true
                RpcTransportResult.Response(200, "{}")
            },
        ).trigger(request)
        assertTrue(unauthenticated is PaperExtractionTriggerResult.AuthenticationRequired)
        assertTrue(!called)

        val unavailable = SupabasePaperExtractionAdapter(
            sessionTokenSource = SessionTokenSource { "jwt" },
            transport = EdgeFunctionTransport { _, _, _ ->
                RpcTransportResult.Response(409, """{"ok":false,"error":"XQ_PAPER_ALREADY_CONFIRMED"}""")
            },
        ).trigger(request)
        assertTrue(unavailable is PaperExtractionTriggerResult.Unavailable)
        assertEquals(
            "XQ_PAPER_ALREADY_CONFIRMED",
            (unavailable as PaperExtractionTriggerResult.Unavailable).errorCode,
        )
    }

    private fun sampleRequest(): TriggerPaperExtractionRequest =
        TriggerPaperExtractionRequest(
            operationId = UUID.fromString("99800000-0000-0000-0000-000000000001"),
            organizationId = UUID.fromString("20000000-0000-0000-0000-000000000001"),
            studentId = UUID.fromString("30000000-0000-0000-0000-000000000001"),
            subjectProfileId = UUID.fromString("40000000-0000-0000-0000-000000000001"),
            paperSubmissionId = UUID.fromString("88800000-0000-0000-0000-000000000001"),
            expectedVersion = 1L,
        )
}
