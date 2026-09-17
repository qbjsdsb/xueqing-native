package com.xueqing.app.durability

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.presentation.QuickCaptureViewModel
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class ProcessDeathSeedInstrumentedTest {
    @Test
    fun leavesKnownDraftForForceStopRestartVerification() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val store = DraftStore(DraftDatabase.get(context).draftDao())
        val scope = QuickCaptureViewModel.FIXTURE_SCOPE

        val existing = store.open(scope)
        store.discard(existing)

        val fresh = store.open(scope)
        assertTrue(store.save(fresh, PROCESS_DEATH_SENTINEL))
        assertEquals(PROCESS_DEATH_SENTINEL, store.load(scope)?.text)
    }

    companion object {
        const val PROCESS_DEATH_SENTINEL = "进程重启证据：这条课堂观察必须从 Room 恢复。"
    }
}
