package com.xueqing.app.infrastructure.remote

import java.io.IOException
import java.net.HttpURLConnection
import java.net.SocketTimeoutException
import java.net.URI

fun interface SessionTokenSource {
    fun currentAccessToken(): String?
}

data class RpcCall(
    val functionName: String,
    val accessToken: String,
    val bodyJson: String,
)

sealed interface RpcTransportResult {
    data class Response(
        val statusCode: Int,
        val body: String,
    ) : RpcTransportResult

    data object Timeout : RpcTransportResult

    data object NetworkFailure : RpcTransportResult
}

fun interface RpcTransport {
    fun post(call: RpcCall): RpcTransportResult
}

/**
 * Deliberately tiny Supabase/PostgREST transport. It owns provider HTTP details;
 * application/domain code never sees provider SDK or HTTP response types.
 *
 * Plain HTTP is rejected by default. The one exception is an explicit
 * development/test switch for loopback aliases used by the local Supabase stack
 * from an Android emulator. Release composition must keep the default false.
 *
 * This transport is blocking by design. Outbox workers call it off the UI
 * thread; network waits never occur inside a Room transaction.
 */
class HttpRpcTransport(
    baseUrl: String,
    private val publishableKey: String,
    private val connectTimeoutMillis: Int = 15_000,
    private val readTimeoutMillis: Int = 20_000,
    allowInsecureLoopbackForDevelopment: Boolean = false,
) : RpcTransport {
    private val normalizedBaseUrl = baseUrl.trimEnd('/')

    init {
        require(publishableKey.isNotBlank()) { "Supabase publishable key must not be blank." }
        val uri = runCatching { URI.create(normalizedBaseUrl) }.getOrNull()
        val host = uri?.host?.lowercase()
        val secureOrigin = uri?.scheme == "https" && !host.isNullOrBlank()
        val explicitDevelopmentLoopback =
            allowInsecureLoopbackForDevelopment &&
                uri?.scheme == "http" &&
                host in DEVELOPMENT_LOOPBACK_HOSTS
        require(secureOrigin || explicitDevelopmentLoopback) {
            "RPC base URL must be HTTPS, except explicit development loopback origins."
        }
        require(uri?.rawUserInfo == null) { "RPC base URL must not contain user info." }
        require(uri?.rawQuery == null && uri?.rawFragment == null) {
            "RPC base URL must be an origin without query or fragment."
        }
        require(connectTimeoutMillis > 0) { "Connect timeout must be positive." }
        require(readTimeoutMillis > 0) { "Read timeout must be positive." }
    }

    override fun post(call: RpcCall): RpcTransportResult {
        require(call.functionName.matches(Regex("[a-z0-9_]+"))) { "Invalid RPC function name." }
        require(call.accessToken.isNotBlank()) { "Access token must not be blank." }

        val connection = URI.create("$normalizedBaseUrl/rest/v1/rpc/${call.functionName}")
            .toURL()
            .openConnection() as HttpURLConnection

        return try {
            connection.requestMethod = "POST"
            connection.connectTimeout = connectTimeoutMillis
            connection.readTimeout = readTimeoutMillis
            connection.doOutput = true
            connection.setRequestProperty("apikey", publishableKey)
            connection.setRequestProperty("Authorization", "Bearer ${call.accessToken}")
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            connection.setRequestProperty("Accept", "application/json")

            connection.outputStream.bufferedWriter(Charsets.UTF_8).use { writer ->
                writer.write(call.bodyJson)
            }

            val statusCode = connection.responseCode
            val stream = if (statusCode in 200..299) connection.inputStream else connection.errorStream
            val body = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }.orEmpty()
            RpcTransportResult.Response(statusCode = statusCode, body = body)
        } catch (_: SocketTimeoutException) {
            RpcTransportResult.Timeout
        } catch (_: IOException) {
            RpcTransportResult.NetworkFailure
        } finally {
            connection.disconnect()
        }
    }

    private companion object {
        val DEVELOPMENT_LOOPBACK_HOSTS = setOf("127.0.0.1", "localhost", "10.0.2.2")
    }
}
