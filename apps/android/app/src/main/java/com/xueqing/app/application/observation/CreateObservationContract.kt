package com.xueqing.app.application.observation

import java.time.Instant
import java.util.UUID

data class CreateObservationRequest(
    val operationId: UUID,
    val organizationId: UUID,
    val studentId: UUID,
    val subjectProfileId: UUID,
    val assignmentId: UUID,
    val rawText: String,
    val clientCapturedAt: Instant? = null,
    val clientCaptureMetadata: Map<String, String> = emptyMap(),
)

data class CreateObservationReceipt(
    val command: String,
    val operationId: UUID,
    val observationId: UUID,
    val actorAppUserId: UUID,
    val organizationId: UUID,
    val studentId: UUID,
    val subjectProfileId: UUID,
    val subjectKey: String,
    val serverCommittedAt: Instant,
)

enum class CreateObservationRejection(val wireCode: String) {
    OperationIdRequired("XQ_OPERATION_ID_REQUIRED"),
    TeachingContextRequired("XQ_TEACHING_CONTEXT_REQUIRED"),
    InvalidObservationText("XQ_INVALID_OBSERVATION_TEXT"),
    InvalidCaptureMetadata("XQ_INVALID_CAPTURE_METADATA"),
    AuthRequired("XQ_AUTH_REQUIRED"),
    ActorNotFound("XQ_ACTOR_NOT_FOUND"),
    ActorDisabled("XQ_ACTOR_DISABLED"),
    OperationReusedWithDifferentPayload("XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD"),
    MembershipRequired("XQ_MEMBERSHIP_REQUIRED"),
    MembershipDisabled("XQ_MEMBERSHIP_DISABLED"),
    TeachingCapabilityRequired("XQ_TEACHING_CAPABILITY_REQUIRED"),
    StudentNotInOrganization("XQ_STUDENT_NOT_IN_ORG"),
    SubjectProfileRequired("XQ_SUBJECT_PROFILE_REQUIRED"),
    TeacherAssignmentRequired("XQ_TEACHER_ASSIGNMENT_REQUIRED"),
    ;

    companion object {
        private val byWireCode = entries.associateBy(CreateObservationRejection::wireCode)

        fun fromWireCode(wireCode: String): CreateObservationRejection? = byWireCode[wireCode]
    }
}

enum class ObservationUnknownReason {
    Timeout,
    NetworkFailure,
    ServerFailure,
    InvalidSuccessPayload,
}

enum class ObservationProtocolFailure {
    UnrecognizedResponse,
    UnknownDomainError,
    ProviderProtocolError,
}

sealed interface ObservationCommandResult {
    val operationId: UUID

    data class Accepted(
        override val operationId: UUID,
        val receipt: CreateObservationReceipt,
    ) : ObservationCommandResult

    data class Rejected(
        override val operationId: UUID,
        val rejection: CreateObservationRejection,
    ) : ObservationCommandResult

    data class AuthenticationRequired(
        override val operationId: UUID,
    ) : ObservationCommandResult

    data class UnknownResult(
        override val operationId: UUID,
        val reason: ObservationUnknownReason,
    ) : ObservationCommandResult

    data class ProtocolFailure(
        override val operationId: UUID,
        val failure: ObservationProtocolFailure,
    ) : ObservationCommandResult
}

fun interface ObservationCommandRemote {
    fun createObservation(request: CreateObservationRequest): ObservationCommandResult
}
