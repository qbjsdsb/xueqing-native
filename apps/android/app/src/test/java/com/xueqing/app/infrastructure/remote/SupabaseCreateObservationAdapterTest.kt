package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.observation.CreateObservationRejection
import com.xueqing.app.application.observation.CreateObservationRequest
import com.xueqing.app.application.observation.ObservationCommandResult
import com.xueqing.app.application.observation.ObservationUnknownReason
import java.time.Instant
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SupabaseCreateObservationAdapterTest {
    @Test
    fun request_uses_exact_rpc_shape_and_never_sends_actor() {
        val request = request()
        val transport = RecordingTransport(successResponse(request))
        val adapter = adapter(transport = transport)

        val result = adapter.createObservation(request)

        assertTrue(result is ObservationCommandResult.Accepted)
        assertEquals(1, transport.calls.size)
        val call = transport.calls.single()
        assertEquals("create_observation", call.functionName)
        assertEquals("session-token", call.accessToken)

        val body = Json.parseToJsonElement(call.bodyJson) as JsonObject
        assertEquals(
            setOf(
                "p_operation_id",
                "p_organization_id",
                "p_student_id",
                "p_subject_profile_id",
                "p_assignment_id",
                "p_raw_text",
                "p_client_captured_at",
                "p_client_capture_metadata",
            ),
            body.keys,
        )
        assertEquals(request.operationId.toString(), body.string("p_operation_id"))
        assertEquals(request.organizationId.toString(), body.string("p_organization_id"))
        assertEquals(request.studentId.toString(), body.string("p_student_id"))
        assertEquals(request.subjectProfileId.toString(), body.string("p_subject_profile_id"))
        assertEquals(request.assignmentId.toString(), body.string("p_assignment_id"))
        assertEquals(request.rawText, body.string("p_raw_text"))
        assertFalse(body.keys.any { it.contains("actor", ignoreCase = true) })
    }

    @Test
    fun accepted_receipt_is_decoded_and_context_checked() {
        val request = request()
        val adapter = adapter(response = successResponse(request))

        val result = adapter.createObservation(request)

        assertTrue(result is ObservationCommandResult.Accepted)
        val accepted = result as ObservationCommandResult.Accepted
        assertEquals(request.operationId, accepted.receipt.operationId)
        assertEquals(request.organizationId, accepted.receipt.organizationId)
        assertEquals(request.studentId, accepted.receipt.studentId)
        assertEquals(request.subjectProfileId, accepted.receipt.subjectProfileId)
        assertEquals("chinese", accepted.receipt.subjectKey)
    }

    @Test
    fun every_frozen_xq_code_maps_to_a_typed_domain_rejection() {
        val request = request()

        CreateObservationRejection.entries.forEach { rejection ->
            val response = RpcTransportResult.Response(
                statusCode = 400,
                body = """{"code":"P0001","message":"${rejection.wireCode}","details":null,"hint":null}""",
            )
            val result = adapter(response = response).createObservation(request)

            assertTrue("Expected typed rejection for ${rejection.wireCode}", result is ObservationCommandResult.Rejected)
            assertEquals(rejection, (result as ObservationCommandResult.Rejected).rejection)
            assertEquals(request.operationId, result.operationId)
        }
    }

    @Test
    fun missing_session_is_authentication_required_without_network_call() {
        val transport = RecordingTransport(successResponse(request()))
        val adapter = SupabaseCreateObservationAdapter(
            sessionTokenSource = SessionTokenSource { null },
            transport = transport,
        )

        val result = adapter.createObservation(request())

        assertTrue(result is ObservationCommandResult.AuthenticationRequired)
        assertTrue(transport.calls.isEmpty())
    }

    @Test
    fun transport_unknown_outcomes_keep_the_original_operation_id() {
        val request = request()
        val cases = listOf(
            RpcTransportResult.Timeout to ObservationUnknownReason.Timeout,
            RpcTransportResult.NetworkFailure to ObservationUnknownReason.NetworkFailure,
            RpcTransportResult.Response(503, "service unavailable") to ObservationUnknownReason.ServerFailure,
        )

        cases.forEach { (transportResult, expectedReason) ->
            val result = adapter(response = transportResult).createObservation(request)
            assertTrue(result is ObservationCommandResult.UnknownResult)
            result as ObservationCommandResult.UnknownResult
            assertEquals(request.operationId, result.operationId)
            assertEquals(expectedReason, result.reason)
        }
    }

    @Test
    fun retrying_the_same_request_forwards_the_same_operation_id() {
        val request = request()
        val transport = RecordingTransport(RpcTransportResult.Timeout)
        val adapter = adapter(transport = transport)

        adapter.createObservation(request)
        adapter.createObservation(request)

        assertEquals(2, transport.calls.size)
        val operationIds = transport.calls.map { call ->
            (Json.parseToJsonElement(call.bodyJson) as JsonObject).string("p_operation_id")
        }
        assertEquals(listOf(request.operationId.toString(), request.operationId.toString()), operationIds)
    }

    @Test
    fun unknown_xq_or_provider_error_fails_closed_instead_of_inventing_domain_meaning() {
        val request = request()
        val unknownXq = adapter(
            response = RpcTransportResult.Response(
                400,
                """{"code":"P0001","message":"XQ_FUTURE_ERROR"}""",
            ),
        ).createObservation(request)
        val provider403 = adapter(
            response = RpcTransportResult.Response(
                403,
                """{"code":"PGRST301","message":"provider denied"}""",
            ),
        ).createObservation(request)

        assertTrue(unknownXq is ObservationCommandResult.ProtocolFailure)
        assertTrue(provider403 is ObservationCommandResult.ProtocolFailure)
        assertEquals(request.operationId, unknownXq.operationId)
        assertEquals(request.operationId, provider403.operationId)
    }

    @Test
    fun malformed_or_mismatched_success_is_unknown_because_server_may_have_committed() {
        val request = request()
        val malformed = adapter(
            response = RpcTransportResult.Response(200, "{}"),
        ).createObservation(request)
        val mismatched = adapter(
            response = successResponse(request).copy(
                body = successBody(request).replace(
                    request.operationId.toString(),
                    "99999999-9999-9999-9999-999999999999",
                ),
            ),
        ).createObservation(request)

        assertTrue(malformed is ObservationCommandResult.UnknownResult)
        assertTrue(mismatched is ObservationCommandResult.UnknownResult)
        assertEquals(ObservationUnknownReason.InvalidSuccessPayload, (malformed as ObservationCommandResult.UnknownResult).reason)
        assertEquals(ObservationUnknownReason.InvalidSuccessPayload, (mismatched as ObservationCommandResult.UnknownResult).reason)
        assertEquals(request.operationId, malformed.operationId)
        assertEquals(request.operationId, mismatched.operationId)
    }

    @Test
    fun http_401_is_authentication_required_not_a_teaching_assignment_rejection() {
        val request = request()
        val result = adapter(
            response = RpcTransportResult.Response(
                401,
                """{"code":"PGRST301","message":"invalid jwt"}""",
            ),
        ).createObservation(request)

        assertTrue(result is ObservationCommandResult.AuthenticationRequired)
        assertFalse(result is ObservationCommandResult.Rejected)
        assertEquals(request.operationId, result.operationId)
    }

    private fun adapter(
        response: RpcTransportResult = successResponse(request()),
        transport: RecordingTransport = RecordingTransport(response),
    ): SupabaseCreateObservationAdapter = SupabaseCreateObservationAdapter(
        sessionTokenSource = SessionTokenSource { "session-token" },
        transport = transport,
    )

    private fun request() = CreateObservationRequest(
        operationId = UUID.fromString("60000000-0000-0000-0000-000000000001"),
        organizationId = UUID.fromString("20000000-0000-0000-0000-000000000001"),
        studentId = UUID.fromString("30000000-0000-0000-0000-000000000001"),
        subjectProfileId = UUID.fromString("40000000-0000-0000-0000-000000000001"),
        assignmentId = UUID.fromString("50000000-0000-0000-0000-000000000001"),
        rawText = "概括题仍然容易照抄原句。",
        clientCapturedAt = Instant.parse("2026-09-17T12:00:00Z"),
        clientCaptureMetadata = mapOf("source" to "quick_capture", "platform" to "android"),
    )

    private fun successResponse(request: CreateObservationRequest) =
        RpcTransportResult.Response(200, successBody(request))

    private fun successBody(request: CreateObservationRequest) = """
        {
          "command":"create_observation_v1",
          "operation_id":"${request.operationId}",
          "observation_id":"70000000-0000-0000-0000-000000000001",
          "actor_app_user_id":"10000000-0000-0000-0000-000000000001",
          "organization_id":"${request.organizationId}",
          "student_id":"${request.studentId}",
          "subject_profile_id":"${request.subjectProfileId}",
          "subject_key":"chinese",
          "server_committed_at":"2026-09-17T12:00:01Z"
        }
    """.trimIndent()

    private class RecordingTransport(
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
