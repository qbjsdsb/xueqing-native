package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.paper.PaperExtractionRemote
import com.xueqing.app.application.paper.PaperExtractionTriggerResult
import com.xueqing.app.application.paper.TriggerPaperExtractionRequest
import java.io.IOException
import java.net.HttpURLConnection
import java.net.SocketTimeoutException
import java.net.URI
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

fun interface EdgeFunctionTransport {
    fun post(
        functionName: String,
        accessToken: String,
        bodyJson: String,
    ): RpcTransportResult
}

class HttpEdgeFunctionTransport(
    baseUrl: String,
    private val publishableKey: String,
    private val functionRegion: String? = null,
    private val connectTimeoutMillis: Int = 15_000,
    private val readTimeoutMillis: Int = 50_000,
    allowInsecureLoopbackForDevelopment: Boolean = false,
) : EdgeFunctionTransport {
    private val normalizedBaseUrl = baseUrl.trimEnd('/')

    init {
        require(publishableKey.isNotBlank()) { "Supabase publishable key must not be blank." }
        val uri = runCatching { URI.create(normalizedBaseUrl) }.getOrNull()
        val host = uri?.host?.lowercase()
        val secureOrigin = uri?.scheme == "https" && !host.isNullOrBlank()
        val explicitDevelopmentLoopback =
            allowInsecureLoopbackForDevelopment &&
                uri?.scheme == "http" &&
                host != null &&
                host in DEVELOPMENT_LOOPBACK_HOSTS
        require(secureOrigin || explicitDevelopmentLoopback) {
            "Edge Function base URL must be HTTPS, except explicit development loopback origins."
        }
    }

    override fun post(
        functionName: String,
        accessToken: String,
        bodyJson: String,
    ): RpcTransportResult {
        require(functionName.matches(Regex("[a-z0-9-]+"))) { "Invalid Edge Function name." }
        require(accessToken.isNotBlank()) { "Access token must not be blank." }

        val connection = URI.create("$normalizedBaseUrl/functions/v1/$functionName")
            .toURL()
            .openConnection() as HttpURLConnection

        return try {
            connection.requestMethod = "POST"
            connection.connectTimeout = connectTimeoutMillis
            connection.readTimeout = readTimeoutMillis
            connection.doOutput = true
            connection.setRequestProperty("apikey", publishableKey)
            connection.setRequestProperty("Authorization", "Bearer $accessToken")
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
            connection.setRequestProperty("Accept", "application/json")
            if (!functionRegion.isNullOrBlank()) {
                connection.setRequestProperty("x-region", functionRegion.trim())
            }

            connection.outputStream.bufferedWriter(Charsets.UTF_8).use { writer ->
                writer.write(bodyJson)
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

class SupabasePaperExtractionAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: EdgeFunctionTransport,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : PaperExtractionRemote {
    override fun trigger(request: TriggerPaperExtractionRequest): PaperExtractionTriggerResult {
        val accessToken = sessionTokenSource.currentAccessToken()
            ?.takeIf(String::isNotBlank)
            ?: return PaperExtractionTriggerResult.AuthenticationRequired

        val bodyJson = buildJsonObject {
            put("operation_id", request.operationId.toString())
            put("organization_id", request.organizationId.toString())
            put("student_id", request.studentId.toString())
            put("subject_profile_id", request.subjectProfileId.toString())
            put("paper_submission_id", request.paperSubmissionId.toString())
            request.expectedVersion?.let { put("expected_version", it) }
        }.toString()

        return when (val result = transport.post(FUNCTION_NAME, accessToken, bodyJson)) {
            RpcTransportResult.Timeout ->
                PaperExtractionTriggerResult.Unavailable("XQ_NETWORK_TIMEOUT")

            RpcTransportResult.NetworkFailure ->
                PaperExtractionTriggerResult.Unavailable("XQ_NETWORK_UNAVAILABLE")

            is RpcTransportResult.Response -> when {
                result.statusCode in 200..299 -> parseSuccess(request, result.body)
                result.statusCode == 401 -> PaperExtractionTriggerResult.AuthenticationRequired
                else -> PaperExtractionTriggerResult.Unavailable(parseErrorCode(result.body))
            }
        }
    }

    private fun parseSuccess(
        request: TriggerPaperExtractionRequest,
        body: String,
    ): PaperExtractionTriggerResult = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject
            ?: return PaperExtractionTriggerResult.Unavailable("XQ_RECEIPT_INVALID")
        val ok = (root["ok"] as? JsonPrimitive)?.content?.toBooleanStrictOrNull() == true
        if (!ok) {
            return PaperExtractionTriggerResult.Unavailable(parseErrorCode(body))
        }
        val state = (root["confirmation_state"] as? JsonPrimitive)?.content ?: "draft_ready"
        val saved = (root["mistake_items_saved"] as? JsonPrimitive)?.content?.toIntOrNull() ?: 0
        PaperExtractionTriggerResult.Triggered(
            paperSubmissionId = request.paperSubmissionId,
            confirmationState = state,
            mistakeItemsSaved = saved,
        )
    }.getOrElse { PaperExtractionTriggerResult.Unavailable("XQ_RECEIPT_INVALID") }

    private fun parseErrorCode(body: String): String = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return "XQ_PAPER_EXTRACTION_FAILED"
        (root["error"] as? JsonPrimitive)?.content?.takeIf(String::isNotBlank)
            ?: "XQ_PAPER_EXTRACTION_FAILED"
    }.getOrDefault("XQ_PAPER_EXTRACTION_FAILED")

    private companion object {
        const val FUNCTION_NAME = "extract-paper-v1"
    }
}
