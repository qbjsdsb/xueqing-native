package com.xueqing.app.presentation

import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import java.util.UUID
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Test

class CaptureContextSelectionTest {
    private val selected = PersonalTeachingContext(
        organizationId = id(1),
        studentId = id(2),
        studentDisplayName = "虚构学生甲",
        subjectProfileId = id(3),
        subjectKey = "chinese",
        assignmentId = id(4),
    )

    @Test
    fun stale_explicit_selection_never_opens_the_only_remaining_student() {
        val otherStudent = selected.copy(studentId = id(5))
        assertNull(resolveCaptureContext(selected, listOf(otherStudent)))
    }

    @Test
    fun changed_scope_or_assignment_requires_reselection() {
        val changedContexts = listOf(
            selected.copy(organizationId = id(5)),
            selected.copy(subjectProfileId = id(5)),
            selected.copy(assignmentId = id(5)),
            selected.copy(subjectKey = "math"),
        )
        changedContexts.forEach { changed ->
            assertNull(resolveCaptureContext(selected, listOf(changed)))
        }
    }

    @Test
    fun matching_selection_uses_fresh_authoritative_metadata() {
        val refreshed = selected.copy(studentDisplayName = "更新后的虚构姓名")
        assertSame(refreshed, resolveCaptureContext(selected, listOf(refreshed)))
    }

    @Test
    fun unscoped_capture_only_selects_an_unambiguous_context() {
        assertNull(resolveCaptureContext(null, emptyList()))
        assertSame(selected, resolveCaptureContext(null, listOf(selected)))
        assertNull(resolveCaptureContext(null, listOf(selected, selected.copy(studentId = id(5)))))
    }

    @Test
    fun missing_or_ambiguous_explicit_context_fails_closed() {
        assertNull(resolveCaptureContext(selected, emptyList()))
        assertNull(resolveCaptureContext(selected, listOf(selected, selected)))
    }

    private fun id(value: Long): UUID = UUID(0L, value)
}
