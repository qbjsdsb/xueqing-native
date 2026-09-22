package com.xueqing.app.infrastructure.deployment

import java.net.URI
import java.util.Locale
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

data class DeploymentProfileSource(
    val repository: String,
    val commit: String,
)

data class DeploymentProfile(
    val profileId: String,
    val environmentId: String,
    val trustDomainId: String,
    val providerId: String,
    val projectOrigin: String,
    val publishableKey: String,
    val requiredEdgeRegion: String,
    val capabilities: List<String>,
    val source: DeploymentProfileSource,
)

object DeploymentProfileParser {
    private const val CONTRACT = "deployment_profile_v1"
    private const val REPOSITORY = "qbjsdsb/xueqing-native"

    private val topKeys = setOf(
        "contract",
        "profile_id",
        "environment_id",
        "trust_domain_id",
        "provider_id",
        "project_origin",
        "publishable_key",
        "required_edge_region",
        "capabilities",
        "source",
    )
    private val sourceKeys = setOf("repository", "commit")
    private val supportedCapabilities = setOf(
        "auth",
        "database-rpc",
        "edge-functions",
        "private-storage",
    )
    private val identifierRegex = Regex("^[a-z0-9][a-z0-9._-]{1,95}$")
    private val regionRegex = Regex("^[a-z]{2}-[a-z0-9-]+-[0-9]+$")
    private val commitRegex = Regex("^[0-9a-f]{40}$")
    private val ipv4Regex = Regex("^(?:[0-9]{1,3}\\.){3}[0-9]{1,3}$")

    fun parse(value: String): DeploymentProfile {
        val root = try {
            Json.parseToJsonElement(value) as? JsonObject
                ?: error("Deployment profile must be a JSON object.")
        } catch (error: Exception) {
            throw IllegalArgumentException("Deployment profile JSON is malformed.", error)
        }

        require(root.keys == topKeys) {
            "Deployment profile keys do not match the accepted v1 contract."
        }
        require(root.requiredString("contract") == CONTRACT) {
            "Unsupported deployment profile contract."
        }

        val profileId = root.identifier("profile_id", 64)
        val environmentId = root.identifier("environment_id", 64)
        val trustDomainId = root.identifier("trust_domain_id", 96)
        val providerId = root.identifier("provider_id", 32)
        val origin = validateOrigin(root.requiredString("project_origin"))
        val publishableKey = validatePublishableKey(
            root.requiredString("publishable_key"),
        )
        val edgeRegion = root.requiredString("required_edge_region")
        require(regionRegex.matches(edgeRegion)) {
            "Deployment profile Edge region is invalid."
        }

        val capabilitiesElement = root["capabilities"] as? JsonArray
            ?: error("Deployment profile capabilities must be an array.")
        val capabilities = capabilitiesElement.map { element ->
            val primitive = element as? JsonPrimitive
                ?: error("Deployment profile capability must be a string.")
            require(primitive.isString)
            primitive.content.also { require(it.isNotBlank()) }
        }
        require(capabilities.isNotEmpty())
        require(capabilities.distinct().size == capabilities.size)
        require(capabilities == capabilities.sorted())
        require(capabilities.all(supportedCapabilities::contains))

        val source = root["source"] as? JsonObject
            ?: error("Deployment profile source must be an object.")
        require(source.keys == sourceKeys)
        val repository = source.requiredString("repository")
        require(repository == REPOSITORY)
        val commit = source.requiredString("commit")
        require(commitRegex.matches(commit))

        return DeploymentProfile(
            profileId = profileId,
            environmentId = environmentId,
            trustDomainId = trustDomainId,
            providerId = providerId,
            projectOrigin = origin,
            publishableKey = publishableKey,
            requiredEdgeRegion = edgeRegion,
            capabilities = capabilities,
            source = DeploymentProfileSource(repository, commit),
        )
    }

    private fun JsonObject.requiredString(name: String): String {
        val primitive = this[name] as? JsonPrimitive
            ?: error("Missing deployment profile string: $name")
        require(primitive.isString)
        return primitive.content.also { require(it.isNotBlank()) }
    }

    private fun JsonObject.identifier(name: String, maxLength: Int): String =
        requiredString(name).also { value ->
            require(value.length in 2..maxLength && identifierRegex.matches(value)) {
                "Deployment profile identifier '$name' is invalid."
            }
        }

    private fun validateOrigin(value: String): String {
        val uri = runCatching { URI.create(value) }.getOrNull()
            ?: error("Deployment profile project origin is invalid.")
        val host = uri.host?.lowercase(Locale.ROOT)
        require(
            uri.scheme == "https" &&
                !host.isNullOrBlank() &&
                uri.rawUserInfo == null &&
                uri.rawPath.isNullOrEmpty() &&
                uri.rawQuery == null &&
                uri.rawFragment == null &&
                uri.port in listOf(-1, 443) &&
                host !in setOf("localhost", "localhost.localdomain") &&
                !host.endsWith(".local") &&
                !host.endsWith(".internal") &&
                !host.endsWith(".localhost") &&
                !ipv4Regex.matches(host) &&
                !host.contains(":"),
        ) {
            "Deployment profile project origin is invalid."
        }
        return "${uri.scheme}://${uri.host}"
    }

    private fun validatePublishableKey(value: String): String {
        require(value.length in 16..2048 && value.none(Char::isWhitespace))
        val privilegedPrefix = listOf("sb_", "secret", "_").joinToString("")
        val privilegedRoleMarker = listOf("service", "_role").joinToString("")
        require(!value.contains(privilegedPrefix, ignoreCase = true))
        require(!value.contains(privilegedRoleMarker, ignoreCase = true))
        require(!value.contains("postgresql://", ignoreCase = true))
        require(!value.contains("postgres://", ignoreCase = true))
        return value
    }
}
