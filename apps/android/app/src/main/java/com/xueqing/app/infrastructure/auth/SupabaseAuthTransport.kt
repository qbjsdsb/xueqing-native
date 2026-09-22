package com.xueqing.app.infrastructure.auth

import java.io.IOException
import java.net.HttpURLConnection
import java.net.SocketTimeoutException
import java.net.URI
import java.nio.charset.StandardCharsets
import java.time.Instant
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

enum class ProviderAuthTransportFailureKind {
    Rejected,
    RateLimited,
    Transient,
    ResultUnknown,
    InvalidResponse,
}

class ProviderAuthTransportException(
    val kind: ProviderAuthTransportFailureKind,
    val code: String,
    message: String,
) : IllegalStateException(message)

data class AuthHttpRequest(
    val pathAndQuery: String,
    val headers: Map<String, String>,
    val bodyJson: String?,
)

sealed interface AuthHttpResult {
    data class Response(
        val statusCode: Int,
        val body: String,
    ) : AuthHttpResult

    data object Timeout : AuthHttpResult
    data object NetworkFailure : AuthHttpResult
}

fun interface AuthHttpTransport {
    fun post(request: AuthHttpRequest): AuthHttpResult
}

class HttpAuthTransport(
    baseUrl: String,
    private val connectTimeoutMillis: Int = 10_000,
    private val readTimeoutMillis: Int = 20_000,
    private val allowInsecureLoopbackForDevelopment: Boolean = false,
) : AuthHttpTransport {
    private val normalizedBaseUrl = baseUrl.trimEnd('/')

    init {
        val uri = runCatching { URI.create(normalizedBaseUrl) }.getOrNull()
        val host = uri?.host?.lowercase()
        val secureOrigin = uri?.scheme == "https" && !host.isNullOrBlank()
        val developmentLoopback =
            allowInsecureLoopbackForDevelopment &&
                uri?.scheme == "http" &&
                host != null &&
                host in DEVELOPMENT_LOOPBACK_HOSTS
        require(secureOrigin || developmentLoopback) {
            "Auth base URL must be HTTPS except explicit development loopback."
        }
        require(uri?.rawUserInfo == null) { "Auth base URL must not contain user info." }
        require(uri?.rawPath.isNullOrEmpty()) { "Auth base URL must not contain a path." }
        require(uri?.rawQuery == null && uri?.rawFragment == null) {
            "Auth base URL must be an origin without query or fragment."
        }
        require(connectTimeoutMillis > 0)
        require(readTimeoutMillis > 0)
    }

    override fun post(request: AuthHttpRequest): AuthHttpResult {
        require(request.pathAndQuery.startsWith("auth/v1/")) {
            "Auth transport path must stay under auth/v1."
        }

        val connection = URI.create("$normalizedBaseUrl/${request.pathAndQuery}")
            .toURL()
            .openConnection() as HttpURLConnection

        return try {
            connection.requestMethod = "POST"
            connection.connectTimeout = connectTimeoutMillis
            connection.readTimeout = readTimeoutMillis
            connection.setRequestProperty("Accept", "application/json")
            request.headers.forEach(connection::setRequestProperty)

            if (request.bodyJson != null) {
                connection.doOutput = true
                connection.setRequestProperty(
                    "Content-Type",
                    "application/json; charset=utf-8",
                )
                connection.outputStream.bufferedWriter(StandardCharsets.UTF_8).use { writer ->
                    writer.write(request.bodyJson)
                }
            }

            val status = connection.responseCode
            val stream = if (status in 200..299) {
                connection.inputStream
            } else {
                connection.errorStream
            }
            val body = stream?.bufferedReader(StandardCharsets.UTF_8)
                ?.use { it.readText() }
                .orEmpty()
            AuthHttpResult.Response(status, body)
        } catch (_: SocketTimeoutException) {
            AuthHttpResult.Timeout
        } catch (_: IOException) {
            AuthHttpResult.NetworkFailure
        } finally {
            connection.disconnect()
        }
    }

    private companion object {
        val DEVELOPMENT_LOOPBACK_HOSTS =
            setOf("127.0.0.1", "localhost", "10.0.2.2")
    }
}

class SupabaseAuthTransport(
    publishableKey: String,
    private val http: AuthHttpTransport,
    private val now: () -> Instant = Instant::now,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : ProviderAuthTransport {
    private val publishableKey = validateClientKey(publishableKey)

    override suspend fun signInWithPassword(
        email: String,
        password: String,
    ): ProviderAuthTokens {
        require(email.isNotBlank())
        require(password.isNotBlank())

        val request = AuthHttpRequest(
            pathAndQuery = PASSWORD_TOKEN_PATH,
            headers = clientHeaders(),
            bodyJson = buildJsonObject {
                put("email", email)
                put("password", password)
            }.toString(),
        )

        return when (val result = http.post(request)) {
            AuthHttpResult.Timeout,
            AuthHttpResult.NetworkFailure,
            -> throw ProviderAuthTransportException(
                ProviderAuthTransportFailureKind.Transient,
                "XQ_AUTH_NETWORK_UNAVAILABLE",
                "Supabase password sign-in did not establish a session.",
            )

            is AuthHttpResult.Response -> {
                if (result.statusCode in 200..299) {
                    parseTokens(
                        result.body,
                        ProviderAuthTransportFailureKind.InvalidResponse,
                        "XQ_AUTH_SIGNIN_RESPONSE_INVALID",
                    )
                } else {
                    throw mapSignInFailure(result)
                }
            }
        }
    }

    override suspend fun refresh(refreshToken: String): ProviderRefreshResult {
        validateSessionToken(refreshToken, "refreshToken")
        val request = AuthHttpRequest(
            pathAndQuery = REFRESH_TOKEN_PATH,
            headers = clientHeaders(),
            bodyJson = buildJsonObject {
                put("refresh_token", refreshToken)
            }.toString(),
        )

        return when (val result = http.post(request)) {
            AuthHttpResult.Timeout,
            AuthHttpResult.NetworkFailure,
            -> ProviderRefreshResult(ProviderRefreshDisposition.ResultUnknown)

            is AuthHttpResult.Response -> {
                when {
                    result.statusCode in 200..299 -> {
                        val tokens = runCatching {
                            parseTokens(
                                result.body,
                                ProviderAuthTransportFailureKind.ResultUnknown,
                                "XQ_AUTH_REFRESH_RESPONSE_UNKNOWN",
                            )
                        }.getOrNull()
                            ?: return ProviderRefreshResult(
                                ProviderRefreshDisposition.ResultUnknown,
                            )
                        ProviderRefreshResult(
                            ProviderRefreshDisposition.Success,
                            tokens,
                        )
                    }

                    result.statusCode in setOf(400, 401, 403) ->
                        ProviderRefreshResult(ProviderRefreshDisposition.Rejected)

                    result.statusCode in setOf(408, 425, 429) ->
                        ProviderRefreshResult(
                            ProviderRefreshDisposition.RetryableFailure,
                        )

                    result.statusCode >= 500 ->
                        ProviderRefreshResult(ProviderRefreshDisposition.ResultUnknown)

                    result.statusCode in 400..499 ->
                        ProviderRefreshResult(ProviderRefreshDisposition.Rejected)

                    else -> throw ProviderAuthTransportException(
                        ProviderAuthTransportFailureKind.InvalidResponse,
                        providerErrorCode(result.body)
                            ?: "HTTP_${result.statusCode}",
                        "Supabase refresh returned an unrecognized HTTP response.",
                    )
                }
            }
        }
    }

    override suspend fun signOutLocal(accessToken: String) {
        validateSessionToken(accessToken, "accessToken")
        val result = http.post(
            AuthHttpRequest(
                pathAndQuery = LOCAL_SIGN_OUT_PATH,
                headers = clientHeaders() + mapOf(
                    "Authorization" to "Bearer $accessToken",
                ),
                bodyJson = "{}",
            ),
        )

        when (result) {
            AuthHttpResult.Timeout,
            AuthHttpResult.NetworkFailure,
            -> throw ProviderAuthTransportException(
                ProviderAuthTransportFailureKind.ResultUnknown,
                "XQ_AUTH_SIGNOUT_RESULT_UNKNOWN",
                "Supabase local sign-out was not confirmed.",
            )

            is AuthHttpResult.Response -> {
                if (result.statusCode in 200..299) return

                throw ProviderAuthTransportException(
                    if (result.statusCode >= 500) {
                        ProviderAuthTransportFailureKind.ResultUnknown
                    } else {
                        ProviderAuthTransportFailureKind.Rejected
                    },
                    providerErrorCode(result.body)
                        ?: "HTTP_${result.statusCode}",
                    "Supabase local sign-out was not confirmed.",
                )
            }
        }
    }

    private fun parseTokens(
        body: String,
        failureKind: ProviderAuthTransportFailureKind,
        failureCode: String,
    ): ProviderAuthTokens {
        try {
            val root = json.parseToJsonElement(body) as? JsonObject
                ?: error("Auth response must be an object")
            val accessToken = root.requiredString("access_token")
            val refreshToken = root.requiredString("refresh_token")
            val expiresIn = root.requiredPositiveLong("expires_in")
            return ProviderAuthTokens(
                accessToken = accessToken,
                refreshToken = refreshToken,
                accessTokenExpiresAt = now().plusSeconds(expiresIn),
            )
        } catch (error: Exception) {
            if (error is ProviderAuthTransportException) throw error
            throw ProviderAuthTransportException(
                failureKind,
                failureCode,
                "Supabase Auth token response violated the accepted session contract.",
            )
        }
    }

    private fun mapSignInFailure(
        response: AuthHttpResult.Response,
    ): ProviderAuthTransportException {
        val kind = when (response.statusCode) {
            400, 401, 403, 422 -> ProviderAuthTransportFailureKind.Rejected
            429 -> ProviderAuthTransportFailureKind.RateLimited
            408, 425 -> ProviderAuthTransportFailureKind.Transient
            in 500..599 -> ProviderAuthTransportFailureKind.Transient
            else -> ProviderAuthTransportFailureKind.InvalidResponse
        }
        return ProviderAuthTransportException(
            kind,
            providerErrorCode(response.body) ?: "HTTP_${response.statusCode}",
            "Supabase password sign-in did not establish a session.",
        )
    }

    private fun clientHeaders() = mapOf("apikey" to publishableKey)

    private fun providerErrorCode(body: String): String? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        ERROR_FIELDS.firstNotNullOfOrNull { field ->
            val value = root[field] as? JsonPrimitive
            value?.takeIf { it.isString }?.content?.trim()?.takeIf(String::isNotBlank)
        }
    }.getOrNull()

    private fun JsonObject.requiredString(name: String): String {
        val value = this[name]?.jsonPrimitive
            ?: error("Missing Auth field: $name")
        require(value.isString)
        return value.content.also { require(it.isNotBlank()) }
    }

    private fun JsonObject.requiredPositiveLong(name: String): Long {
        val value = this[name]?.jsonPrimitive
            ?: error("Missing Auth field: $name")
        val number = value.content.toLong()
        require(number > 0)
        return number
    }

    private companion object {
        const val PASSWORD_TOKEN_PATH = "auth/v1/token?grant_type=password"
        const val REFRESH_TOKEN_PATH = "auth/v1/token?grant_type=refresh_token"
        const val LOCAL_SIGN_OUT_PATH = "auth/v1/logout?scope=local"

        val ERROR_FIELDS = listOf(
            "error_code",
            "code",
            "error_description",
            "msg",
            "message",
            "error",
        )

        fun validateClientKey(value: String): String {
            require(value.isNotBlank())
            require(value.none(Char::isWhitespace))
            require(!value.startsWith("sb_secret_", ignoreCase = true)) {
                "Privileged Supabase credentials must never be supplied to a native client."
            }
            require(!value.contains("service_role", ignoreCase = true)) {
                "Service-role material must never be supplied to a native client."
            }
            return value
        }

        fun validateSessionToken(value: String, name: String) {
            require(value.isNotBlank()) { "$name must not be blank." }
            require(value.none(Char::isWhitespace)) {
                "$name must not contain whitespace."
            }
        }
    }
}
