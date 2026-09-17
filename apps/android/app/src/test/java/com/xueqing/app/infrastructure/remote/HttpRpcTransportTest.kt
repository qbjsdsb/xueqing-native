package com.xueqing.app.infrastructure.remote

import org.junit.Assert.assertNotNull
import org.junit.Assert.assertThrows
import org.junit.Test

class HttpRpcTransportTest {
    @Test
    fun httpsOriginIsAcceptedByDefault() {
        assertNotNull(HttpRpcTransport("https://example.invalid", "publishable-key"))
    }

    @Test
    fun plainHttpIsRejectedByDefault() {
        assertThrows(IllegalArgumentException::class.java) {
            HttpRpcTransport("http://127.0.0.1:54321", "publishable-key")
        }
    }

    @Test
    fun explicitDevelopmentLoopbackAllowsOnlyKnownLocalAliases() {
        listOf("127.0.0.1", "localhost", "10.0.2.2").forEach { host ->
            assertNotNull(
                HttpRpcTransport(
                    baseUrl = "http://$host:54321",
                    publishableKey = "publishable-key",
                    allowInsecureLoopbackForDevelopment = true,
                ),
            )
        }

        assertThrows(IllegalArgumentException::class.java) {
            HttpRpcTransport(
                baseUrl = "http://192.168.1.20:54321",
                publishableKey = "publishable-key",
                allowInsecureLoopbackForDevelopment = true,
            )
        }
    }

    @Test
    fun originCannotEmbedCredentialsPathQueryOrFragment() {
        listOf(
            "https://user@example.invalid",
            "https://example.invalid/rest",
            "https://example.invalid?token=value",
            "https://example.invalid#fragment",
        ).forEach { invalidOrigin ->
            assertThrows(IllegalArgumentException::class.java) {
                HttpRpcTransport(invalidOrigin, "publishable-key")
            }
        }
    }
}
