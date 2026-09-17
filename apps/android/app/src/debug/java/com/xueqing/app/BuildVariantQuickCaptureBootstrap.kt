package com.xueqing.app

import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapActor
import com.xueqing.app.application.bootstrap.PersonalBootstrapOrganization
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import java.time.Instant
import java.util.UUID

/**
 * Debug-only fictional composition matching backend/supabase/seed.sql.
 * No credentials or real data are present. Release builds do not receive it.
 */
object BuildVariantQuickCaptureBootstrap {
    fun remote(): PersonalBootstrapRemote = PersonalBootstrapRemote {
        PersonalBootstrapResult.Loaded(
            PersonalBootstrap(
                generatedAtServer = Instant.EPOCH,
                actor = PersonalBootstrapActor(
                    appUserId = UUID.fromString("10000000-0000-0000-0000-000000000001"),
                    displayName = "虚构教师甲",
                ),
                organizations = listOf(
                    PersonalBootstrapOrganization(
                        organizationId = UUID.fromString("20000000-0000-0000-0000-000000000001"),
                        name = "虚构机构甲",
                        canTeach = true,
                    ),
                ),
                teachingContexts = listOf(
                    PersonalTeachingContext(
                        organizationId = UUID.fromString("20000000-0000-0000-0000-000000000001"),
                        studentId = UUID.fromString("30000000-0000-0000-0000-000000000001"),
                        studentDisplayName = "虚构学生甲",
                        subjectProfileId = UUID.fromString("40000000-0000-0000-0000-000000000001"),
                        subjectKey = "chinese",
                        assignmentId = UUID.fromString("50000000-0000-0000-0000-000000000001"),
                    ),
                ),
            ),
        )
    }
}
