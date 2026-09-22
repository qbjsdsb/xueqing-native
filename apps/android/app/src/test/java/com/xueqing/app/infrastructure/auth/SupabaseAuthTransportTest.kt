package com.xueqing.app.infrastructure.auth

import java.time.Instant
import kotlin.coroutines.startCoroutine
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class SupabaseAuthTransportTest {
    private val now = Instant.parse("2026-09-22T00:00:00Z")

    @Test
    fun `password signin uses gotrue password grant and publishable key`() {
        val fake = RecordingAuthHttpTransport(
            AuthHttpResult.Response(
                200,
                """{"access_token":"access","refresh_token":"refresh","expires_in":3600}""",
            ),
        )
        val transport = create(fake)

        val tokens = runSuspend {
            transport.signInWithPassword(
                "teacher@example.invalid",
                "fictional-password",
            )
        }

        assertEquals("access", tokens.accessToken)
        assertEquals("refresh", tokens.refreshToken)
        assertEquals(now.plusSeconds(3600), tokens.accessTokenExpiresAt)
        assertEquals(
            "auth/v1/token?grant_type=password",
            fake.lastRequest?.pathAndQuery,
        )
        assertEquals(
            "sb_publishable_fictional",
            fake.lastRequest?.headers?.get("apikey"),
        )
        assertTrue(fake.lastRequest?.bodyJson?.contains("\"email\"") == true)
        assertTrue(fake.lastRequest?.bodyJson?.contains("\"password\"") == true)
    }

    @Test
    fun `refresh network ambiguity is result unknown`() {
        val transport = create(
            RecordingAuthHttpTransport(AuthHttpResult.NetworkFailure),
        )

        val result = runSuspend { transport.refresh("old-refresh") }

        assertEquals(
            ProviderRefreshDisposition.ResultUnknown,
            result.disposition,
        )
    }

    @Test
    fun `refresh rejection and rate limit remain distinct`() {
        val rejected = create(
            RecordingAuthHttpTransport(
                AuthHttpResult.Response(
                    400,
                    """{"error_code":"refresh_token_not_found"}""",
                ),
            ),
        )
        val limited = create(
            RecordingAuthHttpTransport(
                AuthHttpResult.Response(429, """{"message":"rate limited"}"""),
            ),
        )

        assertEquals(
            ProviderRefreshDisposition.Rejected,
            runSuspend { rejected.refresh("refresh") }.disposition,
        )
        assertEquals(
            ProviderRefreshDisposition.RetryableFailure,
            runSuspend { limited.refresh("refresh") }.disposition,
        )
    }

    @Test
    fun `malformed successful refresh is result unknown`() {
        val transport = create(
            RecordingAuthHttpTransport(
                AuthHttpResult.Response(
                    200,
                    """{"access_token":"access"}""",
                ),
            ),
        )

        assertEquals(
            ProviderRefreshDisposition.ResultUnknown,
            runSuspend { transport.refresh("refresh") }.disposition,
        )
    }

    @Test
    fun `local signout is scoped and uses bearer token`() {
        val fake = RecordingAuthHttpTransport(
            AuthHttpResult.Response(204, ""),
        )
        val transport = create(fake)

        runSuspend { transport.signOutLocal("access-token") }

        assertEquals(
            "auth/v1/logout?scope=local",
            fake.lastRequest?.pathAndQuery,
        )
        assertEquals(
            "Bearer access-token",
            fake.lastRequest?.headers?.get("Authorization"),
        )
    }

    @Test
    fun `production http transport rejects insecure origin`() {
        assertThrows(IllegalArgumentException::class.java) {
            HttpAuthTransport("http://example.invalid")
        }
        assertThrows(IllegalArgumentException::class.java) {
            SupabaseAuthTransport(
                "sb_secret_never_ship",
                RecordingAuthHttpTransport(
                    AuthHttpResult.Response(200, "{}"),
                ),
            )
        }
    }

    private fun create(fake: RecordingAuthHttpTransport) =
        SupabaseAuthTransport(
            publishableKey = "sb_publishable_fictional",
            http = fake,
            now = { now },
        )

    private class RecordingAuthHttpTransport(
        private val result: AuthHttpResult,
    ) : AuthHttpTransport {
        var lastRequest: AuthHttpRequest? = null
            private set

        override fun post(request: AuthHttpRequest): AuthHttpResult {
            lastRequest = request
            return result
        }
    }

    private fun <T> runSuspend(block: suspend () -> T): T {
        var completion: Result<T>? = null
        block.startCoroutine(
            object : kotlin.coroutines.Continuation<T> {
                override val context = kotlin.coroutines.EmptyCoroutineContext

                override fun resumeWith(result: Result<T>) {
                    completion = result
                }
            },
        )
        return checkNotNull(completion).getOrThrow()
    }
}
