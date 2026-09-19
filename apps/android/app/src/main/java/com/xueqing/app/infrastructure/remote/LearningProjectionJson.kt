package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.learning.ActionDueBucket
import com.xueqing.app.application.learning.LearningCaseState
import com.xueqing.app.application.learning.LearningPrimaryAction
import com.xueqing.app.application.learning.PersonalTodayAction
import com.xueqing.app.application.learning.PersonalTodayActionsSnapshot
import com.xueqing.app.application.learning.StudentLearningCaseFocus
import com.xueqing.app.application.learning.StudentLearningFocusSnapshot
import com.xueqing.app.application.learning.StudentLearningScope
import java.time.Instant
import java.time.LocalDate
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.longOrNull

internal object LearningProjectionJson {
    private const val STUDENT_FOCUS_CONTRACT = "student_learning_focus_v1"
    private const val PERSONAL_TODAY_CONTRACT = "personal_today_actions_v1"
    private const val MAXIMUM_FOCUS_CASES = 3
    private const val MAXIMUM_TODAY_ACTIONS = 200
    private val canonicalUuid = Regex(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
    )
    private val zeroUuid = UUID(0L, 0L)

    fun parseStudentFocus(
        json: Json,
        body: String,
        requestedScope: StudentLearningScope,
        expectedActorAppUserId: UUID,
    ): StudentLearningFocusSnapshot {
        val root = json.parseToJsonElement(body) as? JsonObject
            ?: error("Projection root must be an object.")
        require(root.requiredString("contract") == STUDENT_FOCUS_CONTRACT)

        val actorId = root.requiredUuid("actor_app_user_id")
        require(actorId == expectedActorAppUserId)

        val organizationId = root.requiredUuid("organization_id")
        val studentId = root.requiredUuid("student_id")
        val subjectProfileId = root.requiredUuid("subject_profile_id")
        require(organizationId == requestedScope.organizationId)
        require(studentId == requestedScope.studentId)
        require(subjectProfileId == requestedScope.subjectProfileId)

        val generatedAt = root.requiredInstant("generated_at_server")
        val organizationName = root.requiredNonBlankString("organization_name")
        val organizationTimeZone = root.requiredNonBlankString("organization_time_zone")
        val businessDate = root.requiredDate("organization_business_date")
        val studentDisplayName = root.requiredNonBlankString("student_display_name")
        val subjectKey = root.requiredNonBlankString("subject_key")
        val assignmentId = root.requiredUuid("assignment_id")
        val items = root.requiredArray("cases")
        require(items.size <= MAXIMUM_FOCUS_CASES)

        val seenCaseIds = mutableSetOf<UUID>()
        val seenActionIds = mutableSetOf<UUID>()
        var previous: StudentLearningCaseFocus? = null

        val cases = items.map { element ->
            val item = element as? JsonObject ?: error("Case item must be an object.")
            val action = item["primary_action"] as? JsonObject
                ?: error("Open Case is missing primary_action.")

            val responsibleTeacher = item.requiredUuid("responsible_teacher_app_user_id")
            val ownerAssignment = item.requiredUuid("owner_assignment_id")
            require(responsibleTeacher == expectedActorAppUserId)
            require(ownerAssignment == assignmentId)

            val dueOn = action.optionalDate("due_on")
            val dueBucket = action.requiredDueBucket("due_bucket")
            require(dueBucket == expectedDueBucket(dueOn, businessDate))

            val focus = StudentLearningCaseFocus(
                caseId = item.requiredUuid("case_id"),
                title = item.requiredNonBlankString("title"),
                state = item.requiredOpenCaseState("state"),
                version = item.requiredPositiveLong("case_version"),
                responsibleTeacherAppUserId = responsibleTeacher,
                ownerAssignmentId = ownerAssignment,
                createdAtServer = item.requiredInstant("created_at_server"),
                updatedAtServer = item.requiredInstant("updated_at_server"),
                primaryAction = LearningPrimaryAction(
                    actionId = action.requiredUuid("action_id"),
                    actionText = action.requiredNonBlankString("action_text"),
                    dueOn = dueOn,
                    dueBucket = dueBucket,
                    version = action.requiredPositiveLong("action_version"),
                ),
            )

            require(!focus.updatedAtServer.isBefore(focus.createdAtServer))
            require(seenCaseIds.add(focus.caseId))
            require(seenActionIds.add(focus.primaryAction.actionId))
            previous?.let { require(!focus.comesBefore(it)) }
            previous = focus
            focus
        }

        val hasMore = root.requiredBoolean("has_more")
        require(!hasMore || cases.size == MAXIMUM_FOCUS_CASES)

        return StudentLearningFocusSnapshot(
            generatedAtServer = generatedAt,
            actorAppUserId = actorId,
            organizationId = organizationId,
            organizationName = organizationName,
            organizationTimeZone = organizationTimeZone,
            organizationBusinessDate = businessDate,
            studentId = studentId,
            studentDisplayName = studentDisplayName,
            subjectProfileId = subjectProfileId,
            subjectKey = subjectKey,
            assignmentId = assignmentId,
            cases = cases,
            hasMore = hasMore,
        )
    }

    fun parsePersonalToday(
        json: Json,
        body: String,
        expectedActorAppUserId: UUID,
    ): PersonalTodayActionsSnapshot {
        val root = json.parseToJsonElement(body) as? JsonObject
            ?: error("Projection root must be an object.")
        require(root.requiredString("contract") == PERSONAL_TODAY_CONTRACT)

        val actorId = root.requiredUuid("actor_app_user_id")
        require(actorId == expectedActorAppUserId)
        val generatedAt = root.requiredInstant("generated_at_server")
        val items = root.requiredArray("actions")
        require(items.size <= MAXIMUM_TODAY_ACTIONS)

        val seenActionIds = mutableSetOf<UUID>()
        val seenCaseIds = mutableSetOf<UUID>()
        val organizationSemantics =
            mutableMapOf<UUID, Triple<String, String, LocalDate>>()
        var previous: PersonalTodayAction? = null

        val actions = items.map { element ->
            val item = element as? JsonObject ?: error("Action item must be an object.")
            val organizationId = item.requiredUuid("organization_id")
            val organizationName = item.requiredNonBlankString("organization_name")
            val organizationTimeZone = item.requiredNonBlankString("organization_time_zone")
            val businessDate = item.requiredDate("organization_business_date")
            val dueOn = item.optionalDate("due_on")
            val dueBucket = item.requiredDueBucket("due_bucket")
            require(dueBucket == expectedDueBucket(dueOn, businessDate))

            val action = PersonalTodayAction(
                organizationId = organizationId,
                organizationName = organizationName,
                organizationTimeZone = organizationTimeZone,
                organizationBusinessDate = businessDate,
                studentId = item.requiredUuid("student_id"),
                studentDisplayName = item.requiredNonBlankString("student_display_name"),
                subjectProfileId = item.requiredUuid("subject_profile_id"),
                subjectKey = item.requiredNonBlankString("subject_key"),
                assignmentId = item.requiredUuid("assignment_id"),
                caseId = item.requiredUuid("case_id"),
                caseTitle = item.requiredNonBlankString("case_title"),
                caseState = item.requiredOpenCaseState("case_state"),
                caseVersion = item.requiredPositiveLong("case_version"),
                actionId = item.requiredUuid("action_id"),
                actionText = item.requiredNonBlankString("action_text"),
                dueOn = dueOn,
                dueBucket = dueBucket,
                actionVersion = item.requiredPositiveLong("action_version"),
                caseUpdatedAtServer = item.requiredInstant("case_updated_at_server"),
            )

            require(seenActionIds.add(action.actionId))
            require(seenCaseIds.add(action.caseId))

            val semantics = Triple(organizationName, organizationTimeZone, businessDate)
            val known = organizationSemantics.putIfAbsent(organizationId, semantics)
            require(known == null || known == semantics)

            previous?.let { require(!action.comesBefore(it)) }
            previous = action
            action
        }

        val hasMore = root.requiredBoolean("has_more")
        require(!hasMore || actions.size == MAXIMUM_TODAY_ACTIONS)

        return PersonalTodayActionsSnapshot(
            generatedAtServer = generatedAt,
            actorAppUserId = actorId,
            actions = actions,
            hasMore = hasMore,
        )
    }

    private fun StudentLearningCaseFocus.comesBefore(
        previous: StudentLearningCaseFocus,
    ): Boolean = when {
        updatedAtServer > previous.updatedAtServer -> true
        updatedAtServer < previous.updatedAtServer -> false
        else -> caseId.toString() > previous.caseId.toString()
    }

    private fun PersonalTodayAction.comesBefore(
        previous: PersonalTodayAction,
    ): Boolean {
        val currentRank = bucketRank(dueBucket)
        val previousRank = bucketRank(previous.dueBucket)
        if (currentRank != previousRank) return currentRank < previousRank

        if (dueOn != null && previous.dueOn != null && dueOn != previous.dueOn) {
            return dueOn < previous.dueOn
        }

        return caseUpdatedAtServer > previous.caseUpdatedAtServer
    }

    private fun expectedDueBucket(
        dueOn: LocalDate?,
        businessDate: LocalDate,
    ): ActionDueBucket = when {
        dueOn == null -> ActionDueBucket.Undated
        dueOn < businessDate -> ActionDueBucket.Overdue
        dueOn == businessDate -> ActionDueBucket.Today
        else -> ActionDueBucket.Future
    }

    private fun bucketRank(bucket: ActionDueBucket): Int = when (bucket) {
        ActionDueBucket.Overdue -> 0
        ActionDueBucket.Today -> 1
        ActionDueBucket.Undated -> 2
        ActionDueBucket.Future -> 3
    }

    private fun JsonObject.requiredArray(name: String): JsonArray =
        this[name] as? JsonArray ?: error("Missing JSON array field: $name")

    private fun JsonObject.requiredString(name: String): String {
        val value = this[name] as? JsonPrimitive ?: error("Missing JSON string field: $name")
        require(value.isString)
        return value.content
    }

    private fun JsonObject.requiredNonBlankString(name: String): String =
        requiredString(name).also { require(it.isNotBlank()) }

    private fun JsonObject.requiredUuid(name: String): UUID {
        val value = requiredString(name)
        require(canonicalUuid.matches(value))
        return UUID.fromString(value).also { require(it != zeroUuid) }
    }

    private fun JsonObject.requiredInstant(name: String): Instant =
        Instant.parse(requiredString(name))

    private fun JsonObject.requiredDate(name: String): LocalDate =
        LocalDate.parse(requiredString(name))

    private fun JsonObject.optionalDate(name: String): LocalDate? {
        val value = this[name] ?: return null
        if (value === JsonNull) return null
        val primitive = value as? JsonPrimitive
            ?: error("Date field must be a string or null: $name")
        require(primitive.isString)
        return LocalDate.parse(primitive.content)
    }

    private fun JsonObject.requiredPositiveLong(name: String): Long {
        val primitive = this[name] as? JsonPrimitive ?: error("Missing integer field: $name")
        require(!primitive.isString)
        val value = primitive.longOrNull ?: error("Invalid integer field: $name")
        require(value > 0)
        return value
    }

    private fun JsonObject.requiredBoolean(name: String): Boolean {
        val primitive = this[name] as? JsonPrimitive ?: error("Missing boolean field: $name")
        require(!primitive.isString)
        return primitive.booleanOrNull ?: error("Invalid boolean field: $name")
    }

    private fun JsonObject.requiredOpenCaseState(name: String): LearningCaseState =
        when (requiredString(name)) {
            "new" -> LearningCaseState.New
            "confirmed" -> LearningCaseState.Confirmed
            "intervening" -> LearningCaseState.Intervening
            "pending_verification" -> LearningCaseState.PendingVerification
            "stable" -> LearningCaseState.Stable
            "closed" -> error("Open-learning projection must not contain a closed Case.")
            else -> error("Unsupported Learning Case state.")
        }

    private fun JsonObject.requiredDueBucket(name: String): ActionDueBucket =
        when (requiredString(name)) {
            "overdue" -> ActionDueBucket.Overdue
            "today" -> ActionDueBucket.Today
            "undated" -> ActionDueBucket.Undated
            "future" -> ActionDueBucket.Future
            else -> error("Unsupported Action due bucket.")
        }
}
