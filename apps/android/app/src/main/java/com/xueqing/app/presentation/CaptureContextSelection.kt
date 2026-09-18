package com.xueqing.app.presentation

import com.xueqing.app.application.bootstrap.PersonalTeachingContext

/** An explicit selection must never fall back to a different student or scope. */
internal fun resolveCaptureContext(
    requested: PersonalTeachingContext?,
    available: List<PersonalTeachingContext>,
): PersonalTeachingContext? = if (requested == null) {
    available.singleOrNull()
} else {
    available.singleOrNull { it.sameTeachingContextAs(requested) }
}

internal fun PersonalTeachingContext.sameTeachingContextAs(
    other: PersonalTeachingContext,
): Boolean =
    organizationId == other.organizationId &&
        studentId == other.studentId &&
        subjectProfileId == other.subjectProfileId &&
        assignmentId == other.assignmentId &&
        subjectKey == other.subjectKey
