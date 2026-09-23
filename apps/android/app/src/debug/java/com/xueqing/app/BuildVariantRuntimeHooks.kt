package com.xueqing.app

import android.content.Context
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.session.ClientSessionController
import java.io.OutputStream

object BuildVariantRuntimeHooks {
    fun onApplicationCreate(context: Context) = Unit

    fun environmentId(context: Context): String =
        BuildVariantQuickCaptureBootstrap.ENVIRONMENT_ID

    fun bootstrapRemote(context: Context): PersonalBootstrapRemote =
        BuildVariantQuickCaptureBootstrap.remote()

    fun todayRemote(context: Context): PersonalTodayActionsRemote =
        BuildVariantLearningReadBootstrap.todayRemote()

    fun focusRemote(context: Context): StudentLearningFocusRemote =
        BuildVariantLearningReadBootstrap.focusRemote()

    fun sessionController(context: Context): ClientSessionController? = null

    fun writeDiagnostics(context: Context, output: OutputStream) {
        error("Diagnostics export is available only in the production client composition.")
    }
}
