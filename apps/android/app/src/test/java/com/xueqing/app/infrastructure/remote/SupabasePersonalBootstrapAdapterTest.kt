package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.bootstrap.PersonalBootstrapAccessFailure
import com.xueqing.app.application.bootstrap.PersonalBootstrapProtocolFailure
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalBootstrapUnknownReason
import java.util.UUID
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SupabasePersonalBootstrapAdapterTest {
    @Test
    fun `missing session is authentication required and does not call provider`() {
        var called = false
        val adapter = SupabasePersonalBootstrapAdapter(
            sessionTokenSource = SessionTokenSource { null },
            transport = RpcTransport {
                called = true
                error("transport must not be called")
            },
        )

        assertEquals(PersonalBootstrapResult.AuthenticationRequired, adapter.fetch())
        assertTrue(!called)
    }

    @Test
    fun `adapter calls frozen rpc with bearer session and empty body`() {
        var observed: RpcCall? = null
        val adapter = SupabasePersonalBootstrapAdapter(
            sessionTokenSource = SessionTokenSource { "test-access-token" },
            transport = RpcTransport { call ->
                observed = call
                RpcTransportResult.Response(200, validPayload())
            },
        )

        assertTrue(adapter.fetch() is PersonalBootstrapResult.Loaded)
        assertEquals("get_personal_bootstrap_v1", observed?.functionName)
        assertEquals("test-access-token", observed?.accessToken)
        assertEquals("{}", observed?.bodyJson)
    }

    @Test
    fun `valid projection decodes application identity and real teaching context`() {
        val result = adapterWithResponse(200, validPayload()).fetch() as PersonalBootstrapResult.Loaded
        val bootstrap = result.bootstrap

        assertEquals(UUID.fromString(ACTOR_ID), bootstrap.actor.appUserId)
        assertEquals("虚构教师甲", bootstrap.actor.displayName)
        assertEquals(1, bootstrap.organizations.size)
        assertEquals(1, bootstrap.teachingContexts.size)
        assertEquals(UUID.fromString(ASSIGNMENT_ID), bootstrap.teachingContexts.single().assignmentId)
        assertEquals("chinese", bootstrap.teachingContexts.single().subjectKey)
    }

    @Test
    fun `teaching context outside returned teaching capable organizations fails closed`() {
        val body = validPayload().replace("\"can_teach\":true", "\"can_teach\":false")
        assertEquals(
            PersonalBootstrapResult.ProtocolFailure(PersonalBootstrapProtocolFailure.InvalidSuccessPayload),
            adapterWithResponse(200, body).fetch(),
        )
    }

    @Test
    fun `401 and frozen actor errors remain distinct`() {
        assertEquals(
            PersonalBootstrapResult.AuthenticationRequired,
            adapterWithResponse(401, "{}").fetch(),
        )
        assertEquals(
            PersonalBootstrapResult.AccessUnavailable(PersonalBootstrapAccessFailure.ActorDisabled),
            adapterWithResponse(
                400,
                "{\"code\":\"P0001\",\"message\":\"XQ_ACTOR_DISABLED\",\"details\":null,\"hint\":null}",
            ).fetch(),
        )
    }

    @Test
    fun `transport uncertainty stays retryable without invented product meaning`() {
        val timeout = SupabasePersonalBootstrapAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RpcTransport { RpcTransportResult.Timeout },
        ).fetch()
        val serverFailure = adapterWithResponse(503, "temporary").fetch()

        assertEquals(
            PersonalBootstrapResult.UnknownResult(PersonalBootstrapUnknownReason.Timeout),
            timeout,
        )
        assertEquals(
            PersonalBootstrapResult.UnknownResult(PersonalBootstrapUnknownReason.ServerFailure),
            serverFailure,
        )
    }

    @Test
    fun `unknown p0001 and malformed success fail closed`() {
        assertEquals(
            PersonalBootstrapResult.ProtocolFailure(PersonalBootstrapProtocolFailure.UnknownDomainError),
            adapterWithResponse(
                400,
                "{\"code\":\"P0001\",\"message\":\"XQ_FUTURE_ERROR\"}",
            ).fetch(),
        )
        assertEquals(
            PersonalBootstrapResult.ProtocolFailure(PersonalBootstrapProtocolFailure.InvalidSuccessPayload),
            adapterWithResponse(200, "{\"contract\":\"personal_bootstrap_v2\"}").fetch(),
        )
    }

    private fun adapterWithResponse(status: Int, body: String) =
        SupabasePersonalBootstrapAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RpcTransport { RpcTransportResult.Response(status, body) },
        )

    private fun validPayload() = """
        {
          "contract":"personal_bootstrap_v1",
          "generated_at_server":"2026-09-17T14:00:00Z",
          "actor":{
            "app_user_id":"$ACTOR_ID",
            "display_name":"虚构教师甲"
          },
          "organizations":[{
            "organization_id":"$ORGANIZATION_ID",
            "name":"虚构机构甲",
            "can_teach":true,
            "future_field":"ignored"
          }],
          "teaching_contexts":[{
            "organization_id":"$ORGANIZATION_ID",
            "student_id":"$STUDENT_ID",
            "student_display_name":"虚构学生甲",
            "subject_profile_id":"$PROFILE_ID",
            "subject_key":"chinese",
            "assignment_id":"$ASSIGNMENT_ID"
          }],
          "future_top_level":"ignored"
        }
    """.trimIndent()

    private companion object {
        const val ACTOR_ID = "10000000-0000-0000-0000-000000000001"
        const val ORGANIZATION_ID = "20000000-0000-0000-0000-000000000001"
        const val STUDENT_ID = "30000000-0000-0000-0000-000000000001"
        const val PROFILE_ID = "40000000-0000-0000-0000-000000000001"
        const val ASSIGNMENT_ID = "50000000-0000-0000-0000-000000000001"
    }
}
