package com.xueqing.app

import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult

/**
 * Release builds intentionally have no hard-coded session or fixture identity.
 * The later Session/Auth conformance work replaces this boundary with the live
 * authenticated composition.
 */
object BuildVariantQuickCaptureBootstrap {
    fun remote(): PersonalBootstrapRemote = PersonalBootstrapRemote {
        PersonalBootstrapResult.AuthenticationRequired
    }
}
