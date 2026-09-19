package com.xueqing.app.infrastructure.security

import android.content.Context
import android.os.SystemClock
import android.provider.Settings
import com.xueqing.app.application.security.OfflineClockObservation

/**
 * Android clock adapter for the provider-neutral Offline Access Lease evaluator.
 *
 * BOOT_COUNT is read-only for ordinary applications and identifies whether the
 * elapsed-realtime time base still belongs to the boot session in which the
 * lease was validated.
 */
class AndroidOfflineAccessClock(
    context: Context,
    private val wallClockMillis: () -> Long = System::currentTimeMillis,
    private val monotonicMillis: () -> Long = SystemClock::elapsedRealtime,
) {
    private val resolver = context.applicationContext.contentResolver

    fun observe(): OfflineClockObservation = OfflineClockObservation(
        wallClockEpochMillis = wallClockMillis(),
        monotonicMillis = monotonicMillis(),
        bootSessionId = readBootSessionId(),
    )

    private fun readBootSessionId(): String {
        val bootCount = Settings.Global.getInt(
            resolver,
            Settings.Global.BOOT_COUNT,
            BOOT_COUNT_UNAVAILABLE,
        )
        return if (bootCount >= 0) {
            "android-boot:$bootCount"
        } else {
            // Blank is intentionally fail-closed in the lease evaluator.
            ""
        }
    }

    private companion object {
        const val BOOT_COUNT_UNAVAILABLE = -1
    }
}
