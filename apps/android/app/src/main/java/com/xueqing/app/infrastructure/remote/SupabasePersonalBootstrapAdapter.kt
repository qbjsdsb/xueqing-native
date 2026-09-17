package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapAccessFailure
import com.xueqing.app.application.bootstrap.PersonalBootstrapActor
import com.xueqing.app.application.bootstrap.PersonalBootstrapOrganization
import com.xueqing.app.application.bootstrap.PersonalBootstrapProtocolFailure
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalBootstrapUnknownReason
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import java.time.Instant
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

class SupabasePersonalBootstrapAdapter(
    private val sessionTokenSource: SessionTokenSource,
    private val transport: RpcTransport,
    private val json: Json = Json { ignoreUnknownKeys = true },
) : PersonalBootstrapRemote {
    override fun fetch(): PersonalBootstrapResult {
        val accessToken = sessionTokenSource.currentAccessToken()
            ?.takeIf(String::isNotBlank)
            ?: return PersonalBootstrapResult.AuthenticationRequired

        return when (
            val result = transport.post(
                RpcCall(
                    functionName = RPC_NAME,
                    accessToken = accessToken,
                    bodyJson = "{}",
                ),
            )
        ) {
            RpcTransportResult.Timeout -> PersonalBootstrapResult.UnknownResult(
                PersonalBootstrapUnknownReason.Timeout,
            )

            RpcTransportResult.NetworkFailure -> PersonalBootstrapResult.UnknownResult(
                PersonalBootstrapUnknownReason.NetworkFailure,
            )

            is RpcTransportResult.Response -> mapResponse(result)
        }
    }

    private fun mapResponse(response: RpcTransportResult.Response): PersonalBootstrapResult {
        if (response.statusCode in 200..299) {
            val bootstrap = decodeAndValidate(response.body)
                ?: return PersonalBootstrapResult.ProtocolFailure(
                    PersonalBootstrapProtocolFailure.InvalidSuccessPayload,
                )
            return PersonalBootstrapResult.Loaded(bootstrap)
        }

        if (response.statusCode == 401) {
            return PersonalBootstrapResult.AuthenticationRequired
        }

        if (response.statusCode >= 500) {
            return PersonalBootstrapResult.UnknownResult(PersonalBootstrapUnknownReason.ServerFailure)
        }

        val error = decodeProviderError(response.body)
            ?: return PersonalBootstrapResult.ProtocolFailure(
                PersonalBootstrapProtocolFailure.UnrecognizedResponse,
            )

        if (error.code == "P0001") {
            return when (error.message) {
                "XQ_AUTH_REQUIRED" -> PersonalBootstrapResult.AuthenticationRequired
                "XQ_ACTOR_NOT_FOUND" -> PersonalBootstrapResult.AccessUnavailable(
                    PersonalBootstrapAccessFailure.ActorNotFound,
                )
                "XQ_ACTOR_DISABLED" -> PersonalBootstrapResult.AccessUnavailable(
                    PersonalBootstrapAccessFailure.ActorDisabled,
                )
                else -> PersonalBootstrapResult.ProtocolFailure(
                    PersonalBootstrapProtocolFailure.UnknownDomainError,
                )
            }
        }

        return PersonalBootstrapResult.ProtocolFailure(
            PersonalBootstrapProtocolFailure.ProviderProtocolError,
        )
    }

    private fun decodeAndValidate(body: String): PersonalBootstrap? = runCatching {
        val root = json.parseToJsonElement(body) as? JsonObject ?: return null
        require(root.requiredString("contract") == CONTRACT_NAME)

        val actorObject = root.requiredObject("actor")
        val actor = PersonalBootstrapActor(
            appUserId = UUID.fromString(actorObject.requiredString("app_user_id")),
            displayName = actorObject.requiredNonBlankString("display_name"),
        )

        val organizations = root.requiredArray("organizations").map { element ->
            val organization = element as? JsonObject ?: error("Organization must be an object")
            PersonalBootstrapOrganization(
                organizationId = UUID.fromString(organization.requiredString("organization_id")),
                name = organization.requiredNonBlankString("name"),
                canTeach = organization.requiredBoolean("can_teach"),
            )
        }
        require(organizations.map { it.organizationId }.distinct().size == organizations.size)
        val organizationById = organizations.associateBy { it.organizationId }

        val teachingContexts = root.requiredArray("teaching_contexts").map { element ->
            val context = element as? JsonObject ?: error("Teaching context must be an object")
            PersonalTeachingContext(
                organizationId = UUID.fromString(context.requiredString("organization_id")),
                studentId = UUID.fromString(context.requiredString("student_id")),
                studentDisplayName = context.requiredNonBlankString("student_display_name"),
                subjectProfileId = UUID.fromString(context.requiredString("subject_profile_id")),
                subjectKey = context.requiredNonBlankString("subject_key"),
                assignmentId = UUID.fromString(context.requiredString("assignment_id")),
            )
        }
        require(teachingContexts.map { it.assignmentId }.distinct().size == teachingContexts.size)
        teachingContexts.forEach { context ->
            require(organizationById[context.organizationId]?.canTeach == true) {
                "Teaching context must belong to a returned teaching-capable organization"
            }
        }

        PersonalBootstrap(
            generatedAtServer = Instant.parse(root.requiredString("generated_at_server")),
            actor = actor,
            organizations = organizations,
            teachingContexts = teachingContexts,
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

    private fun JsonObject.requiredArray(name: String): JsonArray =
        this[name] as? JsonArray ?: error("Missing JSON array field: $name")

    private fun JsonObject.requiredString(name: String): String {
        val value = this[name] as? JsonPrimitive ?: error("Missing JSON string field: $name")
        require(value.isString) { "JSON field is not a string: $name" }
        return value.content
    }

    private fun JsonObject.requiredNonBlankString(name: String): String =
        requiredString(name).also { require(it.isNotBlank()) { "JSON string is blank: $name" } }

    private fun JsonObject.requiredBoolean(name: String): Boolean {
        val value = this[name] as? JsonPrimitive ?: error("Missing JSON boolean field: $name")
        return value.content.toBooleanStrict()
    }

    private data class ProviderError(
        val code: String,
        val message: String,
    )

    private companion object {
        const val RPC_NAME = "get_personal_bootstrap_v1"
        const val CONTRACT_NAME = "personal_bootstrap_v1"
    }
}
