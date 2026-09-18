package com.xueqing.app.presentation

import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapActor
import com.xueqing.app.application.bootstrap.PersonalBootstrapOrganization
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import java.time.Instant
import java.util.UUID
import org.junit.Assert.assertEquals
import org.junit.Test

class StudentDirectoryViewModelTest {
    @Test
    fun roster_groups_subjects_without_crossing_organization_scope() {
        val organizationOne = UUID.fromString("20000000-0000-0000-0000-000000000001")
        val organizationTwo = UUID.fromString("20000000-0000-0000-0000-000000000002")
        val studentOne = UUID.fromString("30000000-0000-0000-0000-000000000001")
        val studentTwo = UUID.fromString("30000000-0000-0000-0000-000000000002")

        val bootstrap = PersonalBootstrap(
            generatedAtServer = Instant.EPOCH,
            actor = PersonalBootstrapActor(
                appUserId = UUID.fromString("10000000-0000-0000-0000-000000000001"),
                displayName = "虚构教师甲",
            ),
            organizations = listOf(
                PersonalBootstrapOrganization(organizationOne, "机构甲", canTeach = true),
                PersonalBootstrapOrganization(organizationTwo, "机构乙", canTeach = true),
            ),
            teachingContexts = listOf(
                context(organizationOne, studentOne, "chinese", "50000000-0000-0000-0000-000000000001"),
                context(organizationOne, studentOne, "math", "50000000-0000-0000-0000-000000000002"),
                context(organizationTwo, studentOne, "chinese", "50000000-0000-0000-0000-000000000003"),
                context(organizationOne, studentTwo, "english", "50000000-0000-0000-0000-000000000004"),
            ),
        )

        val roster = buildStudentDirectory(bootstrap)

        assertEquals(3, roster.size)
        val twoSubjectStudent = roster.single { it.contexts.size == 2 }
        assertEquals("语文 · 数学", twoSubjectStudent.subjectSummary)
        assertEquals(organizationOne, twoSubjectStudent.contexts.first().organizationId)

        val organizationTwoStudent = roster.single {
            it.contexts.singleOrNull()?.organizationId == organizationTwo
        }
        assertEquals(studentOne, organizationTwoStudent.contexts.single().studentId)

        val secondStudent = roster.single {
            it.contexts.singleOrNull()?.studentId == studentTwo
        }
        assertEquals("英语", secondStudent.subjectSummary)
    }

    private fun context(
        organizationId: UUID,
        studentId: UUID,
        subjectKey: String,
        subjectProfileId: String,
    ) = PersonalTeachingContext(
        organizationId = organizationId,
        studentId = studentId,
        studentDisplayName = if (studentId.toString().endsWith("1")) "同名学生" else "另一位学生",
        subjectProfileId = UUID.fromString(subjectProfileId),
        subjectKey = subjectKey,
        assignmentId = UUID.randomUUID(),
    )
}
