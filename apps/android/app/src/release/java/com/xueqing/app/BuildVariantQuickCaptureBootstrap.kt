package com.xueqing.app

import android.content.Context
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote

/**
 * Compatibility wrapper for the former release stub. The authoritative release
 * composition now lives in BuildVariantRuntimeHooks/ProductionClientRuntime.
 */
object BuildVariantQuickCaptureBootstrap {
    fun environmentId(context: Context): String =
        BuildVariantRuntimeHooks.environmentId(context)

    fun remote(context: Context): PersonalBootstrapRemote =
        BuildVariantRuntimeHooks.bootstrapRemote(context)
}
