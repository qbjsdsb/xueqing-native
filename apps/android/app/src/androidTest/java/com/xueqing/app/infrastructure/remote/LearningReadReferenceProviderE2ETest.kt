package com.xueqing.app.infrastructure.remote

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.learning.ActionDueBucket
import com.xueqing.app.application.learning.PersonalTodayActionsResult
import com.xueqing.app.application.learning.StudentLearningFocusResult
import com.xueqing.app.application.learning.StudentLearningScope
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LearningReadReferenceProviderE2ETest {
    private lateinit var rpcBaseUrl: String
    private lateinit var publishableKey: String
    private lateinit var accessToken: String

    @Before
    fun setUp() {
        val arguments = InstrumentationRegistry.getArguments()
        rpcBaseUrl = arguments.getString(ARG_RPC_BASE_URL).orEmpty()
        publishableKey = arguments.getString(ARG_PUBLISHABLE_KEY).orEmpty()
        accessToken = arguments.getString(ARG_ACCESS_TOKEN).orEmpty()

        assumeTrue(
            "Reference-provider arguments are supplied only by the dedicated E2E gate.",
            rpcBaseUrl.isNotBlank() && publishableKey.isNotBlank() && accessToken.isNotBlank(),
        )
    }

    @Test
    fun focusAndTodayReadTheSameAuthoritativeLearningCase() {
        val tokenSource = SessionTokenSource { accessToken }
        val transport = HttpRpcTransport(
            baseUrl = rpcBaseUrl,
            publishableKey = publishableKey,
            allowInsecureLoopbackForDevelopment = true,
        )
        val bootstrapAdapter = SupabasePersonalBootstrapAdapter(tokenSource, transport)
        val bootstrapResult = bootstrapAdapter.fetch()
        assertTrue(bootstrapResult is PersonalBootstrapResult.Loaded)
        val bootstrap = (bootstrapResult as PersonalBootstrapResult.Loaded).bootstrap
        val context = bootstrap.teachingContexts.single()

        val setupResult = transport.post(
            RpcCall(
                functionName = "create_learning_case",
                accessToken = accessToken,
                bodyJson = buildJsonObject {
                    put("p_operation_id", OPERATION_ID)
                    put("p_organization_id", context.organizationId.toString())
                    put("p_student_id", context.studentId.toString())
                    put("p_subject_profile_id", context.subjectProfileId.toString())
                    put("p_owner_assignment_id", context.assignmentId.toString())
                    put("p_title", CASE_TITLE)
                    put("p_primary_action_text", ACTION_TEXT)
                    put("p_primary_action_due_on", JsonNull)
                    put("p_source_observation_id", JsonNull)
                }.toString(),
            ),
        )
        assertTrue(setupResult is RpcTransportResult.Response)
        setupResult as RpcTransportResult.Response
        assertTrue(
            "CreateLearningCase fixture failed: HTTP " +
                setupResult.statusCode + " " + setupResult.body,
            setupResult.statusCode in 200..299,
        )
        val receipt = Json.parseToJsonElement(setupResult.body) as JsonObject
        val caseId = UUID.fromString(receipt.requiredString("case_id"))
        val primaryActionId = UUID.fromString(receipt.requiredString("primary_action_id"))

        val focus = SupabaseStudentLearningFocusAdapter(tokenSource, transport).fetch(
            StudentLearningScope(
                organizationId = context.organizationId,
                studentId = context.studentId,
                subjectProfileId = context.subjectProfileId,
            ),
            bootstrap.actor.appUserId,
        )
        assertTrue(focus is StudentLearningFocusResult.Loaded)
        focus as StudentLearningFocusResult.Loaded
        val projectedCase = focus.snapshot.cases.single { it.caseId == caseId }
        assertEquals(CASE_TITLE, projectedCase.title)
        assertEquals(primaryActionId, projectedCase.primaryAction.actionId)
        assertEquals(ACTION_TEXT, projectedCase.primaryAction.actionText)
        assertEquals(ActionDueBucket.Undated, projectedCase.primaryAction.dueBucket)
        assertEquals(context.assignmentId, projectedCase.ownerAssignmentId)
        assertEquals(bootstrap.actor.appUserId, projectedCase.responsibleTeacherAppUserId)

        val today = SupabasePersonalTodayActionsAdapter(tokenSource, transport)
            .fetch(bootstrap.actor.appUserId)
        assertTrue(today is PersonalTodayActionsResult.Loaded)
        today as PersonalTodayActionsResult.Loaded
        val projectedAction = today.snapshot.actions.single { it.actionId == primaryActionId }
        assertEquals(caseId, projectedAction.caseId)
        assertEquals(CASE_TITLE, projectedAction.caseTitle)
        assertEquals(ACTION_TEXT, projectedAction.actionText)
        assertEquals(ActionDueBucket.Undated, projectedAction.dueBucket)
        assertEquals(context.assignmentId, projectedAction.assignmentId)
        assertEquals(context.studentId, projectedAction.studentId)
        assertEquals(context.subjectProfileId, projectedAction.subjectProfileId)
    }

    private fun JsonObject.requiredString(name: String): String =
        (this[name] as? JsonPrimitive)?.content
            ?.takeIf(String::isNotBlank)
            ?: error("Missing receipt field: " + name)

    private companion object {
        const val ARG_RPC_BASE_URL = "xueqingRpcBaseUrl"
        const val ARG_PUBLISHABLE_KEY = "xueqingPublishableKey"
        const val ARG_ACCESS_TOKEN = "xueqingAccessToken"
        const val OPERATION_ID = "78000000-0000-0000-0000-000000000001"
        const val CASE_TITLE = "Android E2E · 虚构概括问题"
        const val ACTION_TEXT = "Android E2E · 下一次只复核限制条件"
    }
}
