package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.compatibility.ClientCompatibilityRequest
import com.xueqing.app.application.compatibility.ClientCompatibilityResult
import com.xueqing.app.application.compatibility.ClientCompatibilityState
import com.xueqing.app.application.compatibility.ClientCompatibilityUnknownReason
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SupabaseClientCompatibilityAdapterTest {
    private val request = ClientCompatibilityRequest("android", "1.0.0", 1)

    @Test
    fun `missing session fails before provider call`() {
        var called = false
        val adapter = SupabaseClientCompatibilityAdapter(
            sessionTokenSource = SessionTokenSource { null },
            transport = RpcTransport {
                called = true
                error("must not call provider")
            },
        )

        assertEquals(ClientCompatibilityResult.AuthenticationRequired, adapter.check(request))
        assertFalse(called)
    }

    @Test
    fun `adapter sends exact request and parses supported response`() {
        var observed: RpcCall? = null
        val adapter = SupabaseClientCompatibilityAdapter(
            sessionTokenSource = SessionTokenSource { "access-token" },
            transport = RpcTransport { call ->
                observed = call
                RpcTransportResult.Response(200, validPayload("supported", "XQ_CLIENT_SUPPORTED"))
            },
        )

        val result = adapter.check(request) as ClientCompatibilityResult.Loaded

        assertEquals("get_client_compatibility_v1", observed?.functionName)
        assertEquals("access-token", observed?.accessToken)
        assertTrue(observed!!.bodyJson.contains("\"p_platform\":\"android\""))
        assertTrue(observed!!.bodyJson.contains("\"p_app_version\":\"1.0.0\""))
        assertEquals(ClientCompatibilityState.Supported, result.decision.state)
        assertTrue(result.decision.state.allowsConsequentialWrite)
    }

    @Test
    fun `recommended update still permits consequential writes`() {
        val result = adapterWithResponse(
            200,
            validPayload("update_recommended", "XQ_UPDATE_RECOMMENDED"),
        ).check(request) as ClientCompatibilityResult.Loaded

        assertEquals(ClientCompatibilityState.UpdateRecommended, result.decision.state)
        assertTrue(result.decision.state.allowsConsequentialWrite)
    }

    @Test
    fun `required and security blocked states deny consequential writes`() {
        for ((state, reason) in listOf(
            "update_required" to "XQ_CLIENT_VERSION_UNSUPPORTED",
            "security_blocked" to "XQ_CLIENT_SECURITY_BLOCKED",
        )) {
            val result = adapterWithResponse(200, validPayload(state, reason))
                .check(request) as ClientCompatibilityResult.Loaded
            assertFalse(result.decision.state.allowsConsequentialWrite)
        }
    }

    @Test
    fun `uncertainty and missing policy never fabricate supported`() {
        val timeout = SupabaseClientCompatibilityAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RpcTransport { RpcTransportResult.Timeout },
        ).check(request)
        val missingPolicy = adapterWithResponse(
            400,
            "{\"code\":\"P0001\",\"message\":\"XQ_COMPATIBILITY_POLICY_UNAVAILABLE\"}",
        ).check(request)

        assertEquals(
            ClientCompatibilityResult.Unknown(ClientCompatibilityUnknownReason.Timeout),
            timeout,
        )
        assertEquals(
            ClientCompatibilityResult.Unknown(ClientCompatibilityUnknownReason.PolicyUnavailable),
            missingPolicy,
        )
    }

    @Test
    fun `echo mismatch or unknown authoritative state fails closed`() {
        val mismatch = validPayload("supported", "XQ_CLIENT_SUPPORTED")
            .replace("\"app_version\":\"1.0.0\"", "\"app_version\":\"other\"")
        val futureState = validPayload("supported", "XQ_CLIENT_SUPPORTED")
            .replace("\"state\":\"supported\"", "\"state\":\"unknown\"")

        assertTrue(adapterWithResponse(200, mismatch).check(request) is ClientCompatibilityResult.ProtocolFailure)
        assertTrue(adapterWithResponse(200, futureState).check(request) is ClientCompatibilityResult.ProtocolFailure)
    }

    private fun adapterWithResponse(status: Int, body: String) =
        SupabaseClientCompatibilityAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RpcTransport { RpcTransportResult.Response(status, body) },
        )

    private fun validPayload(state: String, reason: String) = """
        {
          "contract":"client_compatibility_v1",
          "generated_at_server":"2026-09-23T03:30:00Z",
          "policy_revision":"v1-initial",
          "client":{
            "platform":"android",
            "app_version":"1.0.0",
            "contract_version":1
          },
          "decision":{
            "state":"$state",
            "reason_code":"$reason",
            "minimum_supported_app_version":"0.9.0",
            "recommended_app_version":"1.0.0",
            "minimum_supported_contract_version":1,
            "server_contract_version":1,
            "update_uri":"https://github.com/qbjsdsb/xueqing-native/releases"
          }
        }
    """.trimIndent()
}
