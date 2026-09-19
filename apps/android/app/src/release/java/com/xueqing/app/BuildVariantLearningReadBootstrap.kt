package com.xueqing.app

import com.xueqing.app.application.learning.LearningReadFailure
import com.xueqing.app.application.learning.LearningReadFailureKind
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.PersonalTodayActionsResult
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.learning.StudentLearningFocusResult

/**
 * Release composition remains fail-closed until production Session/Auth wiring
 * supplies the live provider adapters. No fixture identity leaks into release.
 */
object BuildVariantLearningReadBootstrap {
    fun todayRemote(): PersonalTodayActionsRemote = PersonalTodayActionsRemote {
        PersonalTodayActionsResult.Failed(authenticationRequired())
    }

    fun focusRemote(): StudentLearningFocusRemote = StudentLearningFocusRemote { _, _ ->
        StudentLearningFocusResult.Failed(authenticationRequired())
    }

    private fun authenticationRequired() = LearningReadFailure(
        LearningReadFailureKind.AuthenticationRequired,
        "XQ_AUTH_REQUIRED",
    )
}
