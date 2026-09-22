package com.xueqing.app

import android.content.Context
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.StudentLearningFocusRemote

/**
 * Compatibility wrapper for the former fail-closed placeholder. Release reads
 * now share the one production runtime/session authority.
 */
object BuildVariantLearningReadBootstrap {
    fun todayRemote(context: Context): PersonalTodayActionsRemote =
        BuildVariantRuntimeHooks.todayRemote(context)

    fun focusRemote(context: Context): StudentLearningFocusRemote =
        BuildVariantRuntimeHooks.focusRemote(context)
}
