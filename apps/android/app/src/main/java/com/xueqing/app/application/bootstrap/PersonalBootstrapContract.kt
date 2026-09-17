package com.xueqing.app.application.bootstrap

import java.time.Instant
import java.util.UUID

data class PersonalBootstrap(
    val generatedAtServer: Instant,
    val actor: PersonalBootstrapActor,
    val organizations: List<PersonalBootstrapOrganization>,
    val teachingContexts: List<PersonalTeachingContext>,
)

data class PersonalBootstrapActor(
    val appUserId: UUID,
    val displayName: String,
)

data class PersonalBootstrapOrganization(
    val organizationId: UUID,
    val name: String,
    val canTeach: Boolean,
)

data class PersonalTeachingContext(
    val organizationId: UUID,
    val studentId: UUID,
    val studentDisplayName: String,
    val subjectProfileId: UUID,
    val subjectKey: String,
    val assignmentId: UUID,
)

enum class PersonalBootstrapAccessFailure {
    ActorNotFound,
    ActorDisabled,
}

enum class PersonalBootstrapUnknownReason {
    Timeout,
    NetworkFailure,
    ServerFailure,
}

enum class PersonalBootstrapProtocolFailure {
    InvalidSuccessPayload,
    UnrecognizedResponse,
    UnknownDomainError,
    ProviderProtocolError,
}

sealed interface PersonalBootstrapResult {
    data class Loaded(val bootstrap: PersonalBootstrap) : PersonalBootstrapResult
    data object AuthenticationRequired : PersonalBootstrapResult
    data class AccessUnavailable(val reason: PersonalBootstrapAccessFailure) : PersonalBootstrapResult
    data class UnknownResult(val reason: PersonalBootstrapUnknownReason) : PersonalBootstrapResult
    data class ProtocolFailure(val failure: PersonalBootstrapProtocolFailure) : PersonalBootstrapResult
}

fun interface PersonalBootstrapRemote {
    fun fetch(): PersonalBootstrapResult
}
