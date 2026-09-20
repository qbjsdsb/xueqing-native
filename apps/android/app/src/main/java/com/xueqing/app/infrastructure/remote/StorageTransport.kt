package com.xueqing.app.infrastructure.remote

import java.io.IOException
import java.net.HttpURLConnection
import java.net.SocketTimeoutException
import java.net.URI

data class StorageUploadCall(
    val bucketId: String,
    val objectName: String,
    val accessToken: String,
    val contentType: String,
    val body: ByteArray,
)

sealed interface StorageTransportResult {
    data class Response(
        val statusCode: Int,
        val body: String,
    ) : StorageTransportResult

    data object Timeout : StorageTransportResult
    data object NetworkFailure : StorageTransportResult
}

fun interface StorageTransport {
    fun upload(call: StorageUploadCall): StorageTransportResult
}

class HttpStorageTransport(
    baseUrl: String,
    private val publishableKey: String,
    private val connectTimeoutMillis: Int = 15_000,
    private val readTimeoutMillis: Int = 30_000,
    allowInsecureLoopbackForDevelopment: Boolean = false,
) : StorageTransport {
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
            "Storage base URL must be HTTPS, except explicit development loopback origins."
        }
        require(uri?.rawUserInfo == null) { "Storage base URL must not contain user info." }
        require(uri?.rawPath.isNullOrEmpty()) { "Storage base URL must not contain a path." }
        require(uri?.rawQuery == null && uri?.rawFragment == null) {
            "Storage base URL must be an origin without query or fragment."
        }
        require(connectTimeoutMillis > 0)
        require(readTimeoutMillis > 0)
    }

    override fun upload(call: StorageUploadCall): StorageTransportResult {
        require(call.bucketId.matches(SAFE_SEGMENT)) { "Invalid Storage bucket id." }
        require(call.objectName.matches(SAFE_OBJECT_NAME)) { "Invalid Storage object name." }
        require(!call.objectName.startsWith('/') && !call.objectName.endsWith('/'))
        require(!call.objectName.contains("//") && !call.objectName.contains(".."))
        require(call.accessToken.isNotBlank())
        require(call.contentType in ALLOWED_CONTENT_TYPES)
        require(call.body.isNotEmpty())

        val connection = URI.create(
            "${normalizedBaseUrl}/storage/v1/object/${call.bucketId}/${call.objectName}",
        ).toURL().openConnection() as HttpURLConnection

        return try {
            connection.requestMethod = "POST"
            connection.connectTimeout = connectTimeoutMillis
            connection.readTimeout = readTimeoutMillis
            connection.doOutput = true
            connection.setFixedLengthStreamingMode(call.body.size)
            connection.setRequestProperty("apikey", publishableKey)
            connection.setRequestProperty("Authorization", "Bearer ${call.accessToken}")
            connection.setRequestProperty("Content-Type", call.contentType)
            connection.setRequestProperty("Accept", "application/json")
            connection.setRequestProperty("x-upsert", "false")

            connection.outputStream.use { it.write(call.body) }

            val statusCode = connection.responseCode
            val stream = if (statusCode in 200..299) connection.inputStream else connection.errorStream
            val body = stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }.orEmpty()
            StorageTransportResult.Response(statusCode = statusCode, body = body)
        } catch (_: SocketTimeoutException) {
            StorageTransportResult.Timeout
        } catch (_: IOException) {
            StorageTransportResult.NetworkFailure
        } finally {
            connection.disconnect()
        }
    }

    private companion object {
        val DEVELOPMENT_LOOPBACK_HOSTS = setOf("127.0.0.1", "localhost", "10.0.2.2")
        val SAFE_SEGMENT = Regex("^[a-z0-9-]+$")
        val SAFE_OBJECT_NAME = Regex("^[a-z0-9-/]+$")
        val ALLOWED_CONTENT_TYPES = setOf("image/jpeg", "image/png", "image/webp")
    }
}
