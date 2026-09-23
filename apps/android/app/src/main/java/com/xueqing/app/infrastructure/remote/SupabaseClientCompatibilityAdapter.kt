package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.compatibility.ClientCompatibilityDecision
import com.xueqing.app.application.compatibility.ClientCompatibilityRemote
import com.xueqing.app.application.compatibility.ClientCompatibilityRequest
import com.xueqing.app.application.compatibility.ClientCompatibilityResult
import com.xueqing.app.application.compatibility.ClientCompatibilityState
import com.xueqing.app.application.compatibility.ClientCompatibilityUnknownReason
import java.net.URI
import java.time.Instant
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put

class SupabaseClientCompatibilityAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: RpcTransport,
    private val json: Json = Json,
) : ClientCompatibilityRemote {
    override fun check(request: ClientCompatibilityRequest): ClientCompatibilityResult {
        val accessToken = sessionTokenSource.currentAccessToken()
            ?.takeIf(String::isNotBlank)
            ?: return ClientCompatibilityResult.AuthenticationRequired

        val body = buildJsonObject {
            put("p_platform", request.platform)
            put("p_app_version", request.appVersion)
            put("p_client_contract_version", request.contractVersion)
        }.toString()

        return when (
            val result = transport.post(
                RpcCall(
                    functionName = RPC_NAME,
                    accessToken = accessToken,
                    bodyJson = body,
                ),
            )
        ) {
            RpcTransportResult.Timeout -> ClientCompatibilityResult.Unknown(
                ClientCompatibilityUnknownReason.Timeout,
            )

            RpcTransportResult.NetworkFailure -> ClientCompatibilityResult.Unknown(
                ClientCompatibilityUnknownReason.NetworkFailure,
            )

            is RpcTransportResult.Response -> mapResponse(request, result)
        }
    }

    private fun mapResponse(
        request: ClientCompatibilityRequest,
        response: RpcTransportResult.Response,
    ): ClientCompatibilityResult {
        if (response.statusCode in 200..299) {
            return decodeAndValidate(request, response.body)
                ?.let(ClientCompatibilityResult::Loaded)
                ?: ClientCompatibilityResult.ProtocolFailure("XQ_COMPATIBILITY_CONTRACT_INVALID")
        }

        if (response.statusCode == 401) {
            return ClientCompatibilityResult.AuthenticationRequired
        }

        if (response.statusCode == 429 || response.statusCode >= 500) {
            return ClientCompatibilityResult.Unknown(
                ClientCompatibilityUnknownReason.ServerFailure,
            )
        }

        val error = decodeProviderError(response.body)
            ?: return ClientCompatibilityResult.ProtocolFailure(
                "XQ_COMPATIBILITY_PROVIDER_PROTOCOL_INVALID",
            )

        if (error.code == "P0001") {
            return when (error.message) {
                "XQ_AUTH_REQUIRED" -> ClientCompatibilityResult.AuthenticationRequired
                "XQ_COMPATIBILITY_POLICY_UNAVAILABLE" -> ClientCompatibilityResult.Unknown(
                    ClientCompatibilityUnknownReason.PolicyUnavailable,
                )
                else -> ClientCompatibilityResult.ProtocolFailure(error.message)
            }
        }

        return ClientCompatibilityResult.ProtocolFailure(
            "XQ_COMPATIBILITY_PROVIDER_PROTOCOL_INVALID",
        )
    }

    private fun decodeAndValidate(
        request: ClientCompatibilityRequest,
        body: String,
    ): ClientCompatibilityDecision? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject
            ?: error("Compatibility response must be an object")
        require(root.keys == TOP_KEYS)
        require(root.requiredString("contract") == CONTRACT_NAME)

        val policyRevision = root.requiredString("policy_revision")
        require(POLICY_REVISION.matches(policyRevision))
        val generatedAt = Instant.parse(root.requiredString("generated_at_server"))

        val client = root.requiredObject("client")
        require(client.keys == CLIENT_KEYS)
        val platform = client.requiredString("platform")
        val appVersion = client.requiredString("app_version")
        val clientContractVersion = client.requiredInt("contract_version")
        require(platform == request.platform)
        require(appVersion == request.appVersion)
        require(clientContractVersion == request.contractVersion)

        val decision = root.requiredObject("decision")
        require(decision.keys == DECISION_KEYS)
        val state = when (decision.requiredString("state")) {
            "supported" -> ClientCompatibilityState.Supported
            "update_recommended" -> ClientCompatibilityState.UpdateRecommended
            "update_required" -> ClientCompatibilityState.UpdateRequired
            "security_blocked" -> ClientCompatibilityState.SecurityBlocked
            else -> error("Unsupported compatibility state")
        }
        val reason = decision.requiredString("reason_code")
        require(REASON_CODE.matches(reason))
        val minimumContract = decision.requiredInt("minimum_supported_contract_version")
        val serverContract = decision.requiredInt("server_contract_version")
        require(minimumContract >= 1 && serverContract >= minimumContract)

        val updateUri = decision.optionalString("update_uri")
        if (updateUri != null) {
            val uri = URI.create(updateUri)
            require(uri.scheme == "https" && !uri.host.isNullOrBlank())
            require(uri.rawUserInfo == null)
        }

        ClientCompatibilityDecision(
            generatedAtServer = generatedAt,
            policyRevision = policyRevision,
            platform = platform,
            appVersion = appVersion,
            clientContractVersion = clientContractVersion,
            state = state,
            reasonCode = reason,
            minimumSupportedAppVersion = decision.requiredVersion("minimum_supported_app_version"),
            recommendedAppVersion = decision.requiredVersion("recommended_app_version"),
            minimumSupportedContractVersion = minimumContract,
            serverContractVersion = serverContract,
            updateUri = updateUri,
        )
    }.getOrNull()

    private fun decodeProviderError(body: String): ProviderError? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        ProviderError(
            code = root.requiredString("code"),
            message = root.requiredString("message"),
        )
    }.getOrNull()

    private fun JsonObject.requiredObject(name: String): JsonObject =
        this[name] as? JsonObject ?: error("Missing JSON object field: $name")

    private fun JsonObject.requiredString(name: String): String {
        val value = this[name] as? JsonPrimitive ?: error("Missing JSON string field: $name")
        require(value.isString)
        return value.content.also { require(it.isNotBlank()) }
    }

    private fun JsonObject.requiredVersion(name: String): String =
        requiredString(name).also { require(it.length <= 64 && it == it.trim()) }

    private fun JsonObject.requiredInt(name: String): Int {
        val value = this[name] as? JsonPrimitive ?: error("Missing JSON integer field: $name")
        require(!value.isString)
        return value.content.toInt()
    }

    private fun JsonObject.optionalString(name: String): String? {
        val value = this[name] ?: error("Missing JSON field: $name")
        if (value === JsonNull) return null
        val primitive = value as? JsonPrimitive ?: error("JSON field is not a string: $name")
        require(primitive.isString)
        return primitive.content
    }

    private data class ProviderError(
        val code: String,
        val message: String,
    )

    private companion object {
        const val RPC_NAME = "get_client_compatibility_v1"
        const val CONTRACT_NAME = "client_compatibility_v1"
        val TOP_KEYS = setOf(
            "contract",
            "generated_at_server",
            "policy_revision",
            "client",
            "decision",
        )
        val CLIENT_KEYS = setOf("platform", "app_version", "contract_version")
        val DECISION_KEYS = setOf(
            "state",
            "reason_code",
            "minimum_supported_app_version",
            "recommended_app_version",
            "minimum_supported_contract_version",
            "server_contract_version",
            "update_uri",
        )
        val POLICY_REVISION = Regex("^[a-z0-9][a-z0-9._-]{1,63}$")
        val REASON_CODE = Regex("^XQ_[A-Z0-9_]{3,64}$")
    }
}
