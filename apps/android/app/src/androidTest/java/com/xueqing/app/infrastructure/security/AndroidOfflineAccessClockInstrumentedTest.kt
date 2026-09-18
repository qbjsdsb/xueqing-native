package com.xueqing.app.infrastructure.security

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AndroidOfflineAccessClockInstrumentedTest {
    @Test
    fun exposes_boot_bound_elapsed_realtime_observation() {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val clock = AndroidOfflineAccessClock(context)

        val first = clock.observe()
        val second = clock.observe()

        assertTrue(first.wallClockEpochMillis > 0L)
        assertTrue(first.monotonicMillis >= 0L)
        assertTrue(first.bootSessionId.startsWith("android-boot:"))
        assertTrue(second.monotonicMillis >= first.monotonicMillis)
        assertTrue(second.bootSessionId == first.bootSessionId)
    }
}
