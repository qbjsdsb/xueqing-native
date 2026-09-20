package com.xueqing.app.durability

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.BuildVariantQuickCaptureBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.presentation.QuickCaptureViewModel
import java.io.ByteArrayInputStream
import java.util.UUID
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
        val database = DraftDatabase.get(context)
        val store = DraftStore(database.draftDao())
        val attachmentStore = AttachmentStagingStore(
            dao = database.attachmentStagingDao(),
            protectedFiles = ProtectedAttachmentFileStore(context),
        )
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
        val discarded = database.durableIntentDao().discardDraft(
            scopeKey = scope.storageKey,
            expectedEpoch = existing.epoch,
        )
        discarded?.let {
            attachmentStore.deleteProtectedFilesBestEffort(it.attachmentFileNames)
        }

        val fresh = store.open(scope)
        assertTrue(store.save(fresh, PROCESS_DEATH_SENTINEL))
        val staged = attachmentStore.stageDerivative(
            scope = scope,
            draftEpoch = fresh.epoch,
            assignmentId = teachingContext.assignmentId.toString(),
            attachmentId = PROCESS_DEATH_ATTACHMENT_ID,
            contentType = "image/jpeg",
            source = ByteArrayInputStream("process-death-protected-derivative".toByteArray()),
        )

        assertEquals(PROCESS_DEATH_SENTINEL, store.load(scope)?.text)
        assertEquals(
            staged.attachmentId,
            attachmentStore.readForDraft(scope, fresh.epoch).single().attachmentId,
        )
    }

    companion object {
        const val PROCESS_DEATH_SENTINEL = "进程重启证据：这条课堂观察必须从 Room 恢复。"
        val PROCESS_DEATH_ATTACHMENT_ID: UUID =
            UUID.fromString("71000000-0000-0000-0000-000000000099")
    }
}
