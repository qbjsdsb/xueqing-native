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

data class StorageDownloadCall(
    val bucketId: String,
    val objectName: String,
    val accessToken: String,
)

sealed interface StorageDownloadTransportResult {
    data class Response(
        val statusCode: Int,
        val body: ByteArray,
        val contentType: String?,
    ) : StorageDownloadTransportResult

    data object Timeout : StorageDownloadTransportResult
    data object NetworkFailure : StorageDownloadTransportResult
}

fun interface StorageDownloadTransport {
    fun download(call: StorageDownloadCall): StorageDownloadTransportResult
}

class HttpStorageTransport(
    baseUrl: String,
    private val publishableKey: String,
    private val connectTimeoutMillis: Int = 15_000,
    private val readTimeoutMillis: Int = 30_000,
    allowInsecureLoopbackForDevelopment: Boolean = false,
) : StorageTransport, StorageDownloadTransport {
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

    override fun download(call: StorageDownloadCall): StorageDownloadTransportResult {
        validateLocator(call.bucketId, call.objectName)
        require(call.accessToken.isNotBlank())

        val connection = URI.create(
            "${normalizedBaseUrl}/storage/v1/object/authenticated/${call.bucketId}/${call.objectName}",
        ).toURL().openConnection() as HttpURLConnection

        return try {
            connection.requestMethod = "GET"
            connection.connectTimeout = connectTimeoutMillis
            connection.readTimeout = readTimeoutMillis
            connection.setRequestProperty("apikey", publishableKey)
            connection.setRequestProperty("Authorization", "Bearer ${call.accessToken}")
            connection.setRequestProperty("Accept", "image/jpeg, image/png, image/webp")

            val statusCode = connection.responseCode
            val stream = if (statusCode in 200..299) {
                connection.inputStream
            } else {
                connection.errorStream
            }
            val body = stream?.use { readBounded(it, MAX_DOWNLOAD_BYTES + 1) } ?: byteArrayOf()
            StorageDownloadTransportResult.Response(
                statusCode = statusCode,
                body = body,
                contentType = connection.contentType,
            )
        } catch (_: SocketTimeoutException) {
            StorageDownloadTransportResult.Timeout
        } catch (_: IOException) {
            StorageDownloadTransportResult.NetworkFailure
        } finally {
            connection.disconnect()
        }
    }

    override fun upload(call: StorageUploadCall): StorageTransportResult {
        validateLocator(call.bucketId, call.objectName)
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

    private fun readBounded(
        stream: java.io.InputStream,
        limit: Int,
    ): ByteArray {
        require(limit > 0)
        val output = java.io.ByteArrayOutputStream(minOf(limit, 64 * 1024))
        val buffer = ByteArray(16 * 1024)
        var remaining = limit

        while (remaining > 0) {
            val count = stream.read(buffer, 0, minOf(buffer.size, remaining))
            if (count < 0) break
            if (count == 0) continue
            output.write(buffer, 0, count)
            remaining -= count
        }
        return output.toByteArray()
    }

    private fun validateLocator(bucketId: String, objectName: String) {
        require(bucketId.matches(SAFE_SEGMENT)) { "Invalid Storage bucket id." }
        require(objectName.matches(SAFE_OBJECT_NAME)) { "Invalid Storage object name." }
        require(!objectName.startsWith('/') && !objectName.endsWith('/'))
        require(!objectName.contains("//") && !objectName.contains(".."))
    }

    private companion object {
        val DEVELOPMENT_LOOPBACK_HOSTS = setOf("127.0.0.1", "localhost", "10.0.2.2")
        val SAFE_SEGMENT = Regex("^[a-z0-9-]+$")
        val SAFE_OBJECT_NAME = Regex("^[a-z0-9-/]+$")
        const val MAX_DOWNLOAD_BYTES = 6_291_456
        val ALLOWED_CONTENT_TYPES = setOf("image/jpeg", "image/png", "image/webp")
    }
}
