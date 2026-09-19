package com.xueqing.app.infrastructure.remote

import com.xueqing.app.application.learning.ActionDueBucket
import com.xueqing.app.application.learning.LearningReadFailureKind
import com.xueqing.app.application.learning.PersonalTodayActionsResult
import com.xueqing.app.application.learning.StudentLearningFocusResult
import com.xueqing.app.application.learning.StudentLearningScope
import java.util.UUID
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SupabaseLearningReadAdaptersTest {
    @Test
    fun `focus calls frozen rpc with exact teaching scope and validates projection`() {
        var observed: RpcCall? = null
        val adapter = SupabaseStudentLearningFocusAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RpcTransport { call ->
                observed = call
                RpcTransportResult.Response(200, focusPayload())
            },
        )

        val result = adapter.fetch(scope(), uuid(ACTOR_ID))

        assertTrue(result is StudentLearningFocusResult.Loaded)
        result as StudentLearningFocusResult.Loaded
        assertEquals("get_student_learning_focus_v1", observed?.functionName)
        assertTrue(observed!!.bodyJson.contains(ORGANIZATION_ID))
        assertTrue(observed!!.bodyJson.contains(STUDENT_ID))
        assertTrue(observed!!.bodyJson.contains(PROFILE_ID))
        assertEquals(uuid(ACTOR_ID), result.snapshot.actorAppUserId)
        assertEquals(uuid(ASSIGNMENT_ID), result.snapshot.assignmentId)
        assertEquals(1, result.snapshot.cases.size)
        assertEquals(ActionDueBucket.Today, result.snapshot.cases.single().primaryAction.dueBucket)
        assertEquals(7L, result.snapshot.cases.single().version)
        assertEquals(11L, result.snapshot.cases.single().primaryAction.version)
    }

    @Test
    fun `focus fails closed on actor scope assignment due bucket and ordering drift`() {
        val wrongActor = focusPayload().replace(
            "\"actor_app_user_id\":\"$ACTOR_ID\"",
            "\"actor_app_user_id\":\"$OTHER_ID\"",
        )
        val wrongScope = focusPayload().replace(
            "\"student_id\":\"$STUDENT_ID\"",
            "\"student_id\":\"$OTHER_ID\"",
        )
        val wrongAssignment = focusPayload().replace(
            "\"owner_assignment_id\":\"$ASSIGNMENT_ID\"",
            "\"owner_assignment_id\":\"$OTHER_ID\"",
        )
        val wrongBucket = focusPayload().replace(
            "\"due_bucket\":\"today\"",
            "\"due_bucket\":\"future\"",
        )

        for (payload in listOf(wrongActor, wrongScope, wrongAssignment, wrongBucket)) {
            val result = focusAdapter(payload).fetch(scope(), uuid(ACTOR_ID))
            assertEquals(
                LearningReadFailureKind.InvalidResponse,
                (result as StudentLearningFocusResult.Failed).failure.kind,
            )
        }

        val outOfOrder = focusPayload(
            cases = """
                ${caseItem(caseId = CASE_ID, updatedAt = "2026-09-20T09:00:00Z")},
                ${caseItem(
                    caseId = CASE_ID_2,
                    actionId = ACTION_ID_2,
                    updatedAt = "2026-09-20T10:00:00Z",
                )}
            """.trimIndent(),
        )
        val result = focusAdapter(outOfOrder).fetch(scope(), uuid(ACTOR_ID))
        assertEquals(
            LearningReadFailureKind.InvalidResponse,
            (result as StudentLearningFocusResult.Failed).failure.kind,
        )
    }

    @Test
    fun `today validates authoritative order business dates identities and versions`() {
        var observed: RpcCall? = null
        val adapter = SupabasePersonalTodayActionsAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RpcTransport { call ->
                observed = call
                RpcTransportResult.Response(200, todayPayload())
            },
        )

        val result = adapter.fetch(uuid(ACTOR_ID))

        assertTrue(result is PersonalTodayActionsResult.Loaded)
        result as PersonalTodayActionsResult.Loaded
        assertEquals("get_personal_today_actions_v1", observed?.functionName)
        assertEquals("{}", observed?.bodyJson)
        assertEquals(2, result.snapshot.actions.size)
        assertEquals(ActionDueBucket.Overdue, result.snapshot.actions[0].dueBucket)
        assertEquals(ActionDueBucket.Today, result.snapshot.actions[1].dueBucket)
        assertEquals(7L, result.snapshot.actions[0].caseVersion)
        assertEquals(11L, result.snapshot.actions[0].actionVersion)
    }

    @Test
    fun `today fails closed on bucket order duplicate case and inconsistent organization semantics`() {
        val wrongOrder = todayPayload(
            actions = """
                ${todayItem(
                    caseId = CASE_ID_2,
                    actionId = ACTION_ID_2,
                    dueOn = "2026-09-20",
                    dueBucket = "today",
                    updatedAt = "2026-09-20T09:00:00Z",
                )},
                ${todayItem(
                    caseId = CASE_ID,
                    actionId = ACTION_ID,
                    dueOn = "2026-09-19",
                    dueBucket = "overdue",
                    updatedAt = "2026-09-20T10:00:00Z",
                )}
            """.trimIndent(),
        )
        val duplicateCase = todayPayload(
            actions = """
                ${todayItem(
                    caseId = CASE_ID,
                    actionId = ACTION_ID,
                    dueOn = "2026-09-19",
                    dueBucket = "overdue",
                )},
                ${todayItem(
                    caseId = CASE_ID,
                    actionId = ACTION_ID_2,
                    dueOn = "2026-09-20",
                    dueBucket = "today",
                )}
            """.trimIndent(),
        )
        val inconsistentOrganization = todayPayload(
            actions = """
                ${todayItem(
                    caseId = CASE_ID,
                    actionId = ACTION_ID,
                    dueOn = "2026-09-19",
                    dueBucket = "overdue",
                    organizationName = "虚构机构甲",
                )},
                ${todayItem(
                    caseId = CASE_ID_2,
                    actionId = ACTION_ID_2,
                    dueOn = "2026-09-20",
                    dueBucket = "today",
                    organizationName = "虚构机构乙",
                )}
            """.trimIndent(),
        )

        for (payload in listOf(wrongOrder, duplicateCase, inconsistentOrganization)) {
            val result = todayAdapter(payload).fetch(uuid(ACTOR_ID))
            assertEquals(
                LearningReadFailureKind.InvalidResponse,
                (result as PersonalTodayActionsResult.Failed).failure.kind,
            )
        }
    }

    @Test
    fun `read failures preserve auth access invariant transient and unknown protocol meanings`() {
        val missingSession = SupabasePersonalTodayActionsAdapter(
            sessionTokenSource = SessionTokenSource { null },
            transport = RpcTransport { error("provider must not be called") },
        ).fetch(uuid(ACTOR_ID))
        assertEquals(
            LearningReadFailureKind.AuthenticationRequired,
            (missingSession as PersonalTodayActionsResult.Failed).failure.kind,
        )

        val accessDenied = todayAdapter(
            body = """{"code":"P0001","message":"XQ_TEACHING_CONTEXT_UNAVAILABLE"}""",
            status = 400,
        ).fetch(uuid(ACTOR_ID))
        assertEquals(
            LearningReadFailureKind.AccessDenied,
            (accessDenied as PersonalTodayActionsResult.Failed).failure.kind,
        )

        val invariant = todayAdapter(
            body = """{"code":"P0001","message":"XQ_CASE_PRIMARY_ACTION_INVARIANT"}""",
            status = 400,
        ).fetch(uuid(ACTOR_ID))
        assertEquals(
            LearningReadFailureKind.ServerInvariant,
            (invariant as PersonalTodayActionsResult.Failed).failure.kind,
        )

        val transient = SupabasePersonalTodayActionsAdapter(
            sessionTokenSource = SessionTokenSource { "token" },
            transport = RpcTransport { RpcTransportResult.Timeout },
        ).fetch(uuid(ACTOR_ID))
        assertEquals(
            LearningReadFailureKind.Transient,
            (transient as PersonalTodayActionsResult.Failed).failure.kind,
        )

        val unknown = todayAdapter(
            body = """{"code":"P0001","message":"XQ_FUTURE_ERROR"}""",
            status = 400,
        ).fetch(uuid(ACTOR_ID))
        assertEquals(
            LearningReadFailureKind.InvalidResponse,
            (unknown as PersonalTodayActionsResult.Failed).failure.kind,
        )
    }

    private fun focusAdapter(
        body: String,
        status: Int = 200,
    ) = SupabaseStudentLearningFocusAdapter(
        sessionTokenSource = SessionTokenSource { "token" },
        transport = RpcTransport { RpcTransportResult.Response(status, body) },
    )

    private fun todayAdapter(
        body: String,
        status: Int = 200,
    ) = SupabasePersonalTodayActionsAdapter(
        sessionTokenSource = SessionTokenSource { "token" },
        transport = RpcTransport { RpcTransportResult.Response(status, body) },
    )

    private fun scope() = StudentLearningScope(
        organizationId = uuid(ORGANIZATION_ID),
        studentId = uuid(STUDENT_ID),
        subjectProfileId = uuid(PROFILE_ID),
    )

    private fun focusPayload(
        cases: String = caseItem(),
    ) = """
        {
          "contract":"student_learning_focus_v1",
          "generated_at_server":"2026-09-20T10:30:00Z",
          "actor_app_user_id":"$ACTOR_ID",
          "organization_id":"$ORGANIZATION_ID",
          "organization_name":"虚构机构甲",
          "organization_time_zone":"Asia/Shanghai",
          "organization_business_date":"2026-09-20",
          "student_id":"$STUDENT_ID",
          "student_display_name":"虚构学生甲",
          "subject_profile_id":"$PROFILE_ID",
          "subject_key":"chinese",
          "assignment_id":"$ASSIGNMENT_ID",
          "cases":[
            $cases
          ],
          "has_more":false
        }
    """.trimIndent()

    private fun caseItem(
        caseId: String = CASE_ID,
        actionId: String = ACTION_ID,
        updatedAt: String = "2026-09-20T10:00:00Z",
    ) = """
        {
          "case_id":"$caseId",
          "title":"概括题压缩仍不稳定",
          "state":"intervening",
          "case_version":7,
          "responsible_teacher_app_user_id":"$ACTOR_ID",
          "owner_assignment_id":"$ASSIGNMENT_ID",
          "created_at_server":"2026-09-18T08:00:00Z",
          "updated_at_server":"$updatedAt",
          "primary_action":{
            "action_id":"$actionId",
            "action_text":"复核陌生材料",
            "due_on":"2026-09-20",
            "due_bucket":"today",
            "action_version":11
          }
        }
    """.trimIndent()

    private fun todayPayload(
        actions: String = """
            ${todayItem(
                caseId = CASE_ID,
                actionId = ACTION_ID,
                dueOn = "2026-09-19",
                dueBucket = "overdue",
                updatedAt = "2026-09-20T10:00:00Z",
            )},
            ${todayItem(
                caseId = CASE_ID_2,
                actionId = ACTION_ID_2,
                dueOn = "2026-09-20",
                dueBucket = "today",
                updatedAt = "2026-09-20T09:00:00Z",
            )}
        """.trimIndent(),
    ) = """
        {
          "contract":"personal_today_actions_v1",
          "generated_at_server":"2026-09-20T10:30:00Z",
          "actor_app_user_id":"$ACTOR_ID",
          "actions":[
            $actions
          ],
          "has_more":false
        }
    """.trimIndent()

    private fun todayItem(
        caseId: String,
        actionId: String,
        dueOn: String,
        dueBucket: String,
        updatedAt: String = "2026-09-20T10:00:00Z",
        organizationName: String = "虚构机构甲",
    ) = """
        {
          "organization_id":"$ORGANIZATION_ID",
          "organization_name":"$organizationName",
          "organization_time_zone":"Asia/Shanghai",
          "organization_business_date":"2026-09-20",
          "student_id":"$STUDENT_ID",
          "student_display_name":"虚构学生甲",
          "subject_profile_id":"$PROFILE_ID",
          "subject_key":"chinese",
          "assignment_id":"$ASSIGNMENT_ID",
          "case_id":"$caseId",
          "case_title":"概括题压缩仍不稳定",
          "case_state":"intervening",
          "case_version":7,
          "action_id":"$actionId",
          "action_text":"复核陌生材料",
          "due_on":"$dueOn",
          "due_bucket":"$dueBucket",
          "action_version":11,
          "case_updated_at_server":"$updatedAt"
        }
    """.trimIndent()

    private fun uuid(value: String) = UUID.fromString(value)

    private companion object {
        const val ACTOR_ID = "10000000-0000-0000-0000-000000000001"
        const val ORGANIZATION_ID = "20000000-0000-0000-0000-000000000001"
        const val STUDENT_ID = "30000000-0000-0000-0000-000000000001"
        const val PROFILE_ID = "40000000-0000-0000-0000-000000000001"
        const val ASSIGNMENT_ID = "50000000-0000-0000-0000-000000000001"
        const val CASE_ID = "60000000-0000-0000-0000-000000000001"
        const val CASE_ID_2 = "60000000-0000-0000-0000-000000000002"
        const val ACTION_ID = "70000000-0000-0000-0000-000000000001"
        const val ACTION_ID_2 = "70000000-0000-0000-0000-000000000002"
        const val OTHER_ID = "90000000-0000-0000-0000-000000000001"
    }
}
