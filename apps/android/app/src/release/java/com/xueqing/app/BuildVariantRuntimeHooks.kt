package com.xueqing.app

import android.content.Context
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.learning.PersonalTodayActionsRemote
import com.xueqing.app.application.learning.StudentLearningFocusRemote
import com.xueqing.app.application.session.ClientSessionController
import com.xueqing.app.application.session.UnavailableClientSessionController
import com.xueqing.app.durability.AttachmentSyncRuntime
import com.xueqing.app.durability.ObservationSyncRuntime
import com.xueqing.app.infrastructure.auth.ProviderAuthCoordinator
import com.xueqing.app.infrastructure.production.ProductionClientRuntime

object BuildVariantRuntimeHooks {
    private val unavailableSessionController =
        UnavailableClientSessionController()
    @Volatile
    private var initialized = false

    @Volatile
    private var runtime: ProductionClientRuntime? = null

    @Synchronized
    fun onApplicationCreate(context: Context) {
        if (initialized) return
        initialized = true

        runtime = runCatching {
            ProductionClientRuntime.create(context).also { production ->
                production.startSessionRestore()
                production.installSyncRuntimes()
            }
        }.getOrNull()

        if (runtime == null) {
            ObservationSyncRuntime.clear()
            AttachmentSyncRuntime.clear()
        }
    }

    fun environmentId(context: Context): String =
        runtime(context)?.profile?.environmentId ?: "configuration-unavailable"

    fun bootstrapRemote(context: Context): PersonalBootstrapRemote =
        runtime(context)?.bootstrapRemote
            ?: ProductionClientRuntime.unavailableBootstrapRemote()

    fun todayRemote(context: Context): PersonalTodayActionsRemote =
        runtime(context)?.todayRemote
            ?: ProductionClientRuntime.unavailableTodayRemote()

    fun focusRemote(context: Context): StudentLearningFocusRemote =
        runtime(context)?.focusRemote
            ?: ProductionClientRuntime.unavailableFocusRemote()

    fun authCoordinator(context: Context): ProviderAuthCoordinator? =
        runtime(context)?.authCoordinator

    fun sessionController(context: Context): ClientSessionController? =
        runtime(context) ?: unavailableSessionController

    private fun runtime(context: Context): ProductionClientRuntime? {
        if (!initialized) {
            onApplicationCreate(context.applicationContext)
        }
        return runtime
    }
}
