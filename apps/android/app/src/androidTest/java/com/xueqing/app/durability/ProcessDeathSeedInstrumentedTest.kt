package com.xueqing.app.durability

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.BuildVariantQuickCaptureBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
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
        val bootstrap = BuildVariantQuickCaptureBootstrap.remote().fetch() as PersonalBootstrapResult.Loaded
        val teachingContext = bootstrap.bootstrap.teachingContexts.single()
        val scope = DraftScope(
            environmentId = BuildVariantQuickCaptureBootstrap.ENVIRONMENT_ID,
            appUserId = bootstrap.bootstrap.actor.appUserId.toString(),
            organizationId = teachingContext.organizationId.toString(),
            studentId = teachingContext.studentId.toString(),
            subjectId = teachingContext.subjectProfileId.toString(),
            contextId = QuickCaptureViewModel.QUICK_CAPTURE_CONTEXT_ID,
        )

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
