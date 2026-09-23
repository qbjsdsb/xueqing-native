package com.xueqing.app.presentation

import android.content.Context
import android.net.Uri
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ViewModelStore
import androidx.room.Room
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextReplacement
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.xueqing.app.MainActivity
import com.xueqing.app.application.bootstrap.PersonalBootstrap
import com.xueqing.app.application.bootstrap.PersonalBootstrapActor
import com.xueqing.app.application.bootstrap.PersonalBootstrapRemote
import com.xueqing.app.application.bootstrap.PersonalBootstrapResult
import com.xueqing.app.application.bootstrap.PersonalTeachingContext
import com.xueqing.app.durability.AttachmentStagingStore
import com.xueqing.app.durability.DraftDatabase
import com.xueqing.app.durability.DraftScope
import com.xueqing.app.durability.DraftStore
import com.xueqing.app.durability.ProtectedAttachmentFileStore
import com.xueqing.app.infrastructure.media.ImageDerivativeFactory
import com.xueqing.app.infrastructure.media.PhotoAttachmentStager
import java.time.Instant
import java.util.UUID
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class QuickCaptureMediaReturnInstrumentedTest {
    @get:Rule
    val composeRule = createAndroidComposeRule<MainActivity>()

    @Test
    fun photoPickerCancellationReturnsToSameScopedProtectedDraft() {
        openStudentScopedCapture()
        val text = "相册往返测试：离开应用选择照片再返回，学生学科和已保存文字都不能变化。"

        composeRule.onNodeWithTag("quick-capture-input").performTextReplacement(text)
        awaitLocalSafe()
        composeRule.onNodeWithTag("quick-capture-add-photo").performClick()

        composeRule.waitUntil(timeoutMillis = 8_000) {
            composeRule.activityRule.scenario.state != Lifecycle.State.RESUMED
        }

        InstrumentationRegistry.getInstrumentation()
            .uiAutomation
            .executeShellCommand("input keyevent KEYCODE_BACK")
            .close()

        composeRule.waitUntil(timeoutMillis = 8_000) {
            composeRule.activityRule.scenario.state == Lifecycle.State.RESUMED
        }
        composeRule.waitForIdle()

        composeRule.onNodeWithTag("quick-capture-teaching-context")
            .assertTextContains("虚构学生甲 · 语文")
        composeRule.onNodeWithTag("quick-capture-input").assertTextContains(text)
        awaitLocalSafe()

        composeRule.onNodeWithText("丢弃草稿").performClick()
        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithText(text)
                .fetchSemanticsNodes(atLeastOneRootRequired = false)
                .isEmpty()
        }
    }

    @Test
    fun localMediaStagingFailureKeepsPersistedTextAndScope() = runBlocking {
        val context = ApplicationProvider.getApplicationContext<Context>()
        val database = Room.inMemoryDatabaseBuilder(context, DraftDatabase::class.java).build()
        val owner = ViewModelStore()
        val actorId = UUID(0, 91)
        val teachingContext = PersonalTeachingContext(
            organizationId = UUID(0, 92),
            studentId = UUID(0, 93),
            studentDisplayName = "虚构学生媒体测试",
            subjectProfileId = UUID(0, 94),
            subjectKey = "chinese",
            assignmentId = UUID(0, 95),
        )
        val draftScope = DraftScope(
            environmentId = "media-return-test",
            appUserId = actorId.toString(),
            organizationId = teachingContext.organizationId.toString(),
            studentId = teachingContext.studentId.toString(),
            subjectId = teachingContext.subjectProfileId.toString(),
            contextId = QuickCaptureViewModel.QUICK_CAPTURE_CONTEXT_ID,
        )

        try {
            val attachmentStore = AttachmentStagingStore(
                dao = database.attachmentStagingDao(),
                protectedFiles = ProtectedAttachmentFileStore(context),
            )
            val vm = withContext(Dispatchers.Main) {
                QuickCaptureViewModel(
                    store = DraftStore(database.draftDao()),
                    durableIntentDao = database.durableIntentDao(),
                    attachmentStagingStore = attachmentStore,
                    photoAttachmentStager = PhotoAttachmentStager(
                        contentResolver = context.contentResolver,
                        derivativeFactory = ImageDerivativeFactory(),
                        attachmentStagingStore = attachmentStore,
                    ),
                    bootstrapRemote = PersonalBootstrapRemote {
                        PersonalBootstrapResult.Loaded(
                            PersonalBootstrap(
                                generatedAtServer = Instant.EPOCH,
                                actor = PersonalBootstrapActor(actorId, "虚构教师"),
                                organizations = emptyList(),
                                teachingContexts = listOf(teachingContext),
                            ),
                        )
                    },
                    environmentId = "media-return-test",
                    onOutboxCommitted = {},
                    onAttachmentCleanupNeeded = {},
                ).also { owner.put("media-return", it) }
            }

            withTimeout(10_000) {
                vm.uiState.first { it.teachingContextStatus == TeachingContextStatus.Ready }
            }

            val text = "媒体失败测试：照片失败也不能影响已经保护好的课堂观察。"
            withContext(Dispatchers.Main) { vm.onTextChanged(text) }
            withTimeout(10_000) {
                vm.uiState.first { it.draftStatus == LocalDraftStatus.SafeOnDevice && it.text == text }
            }

            withContext(Dispatchers.Main) {
                vm.onPhotoSelected(Uri.parse("content://com.xueqing.missing/not-found"))
            }
            val failed = withTimeout(10_000) {
                vm.uiState.first { it.attachmentStatus == AttachmentDraftStatus.Failed }
            }

            assertEquals(text, failed.text)
            assertEquals(LocalDraftStatus.SafeOnDevice, failed.draftStatus)
            assertEquals(0, failed.attachmentCount)
            assertNotNull(failed.attachmentErrorCode)

            val reopened = withContext(Dispatchers.IO) {
                DraftStore(database.draftDao()).open(draftScope)
            }
            assertEquals(text, reopened.recovered?.text)
        } finally {
            withContext(Dispatchers.Main) { owner.clear() }
            database.close()
        }
    }

    private fun openStudentScopedCapture() {
        composeRule.onNodeWithText("学生").performClick()
        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithTag(
                "student-row-20000000-0000-0000-0000-000000000001:30000000-0000-0000-0000-000000000001",
            ).fetchSemanticsNodes(atLeastOneRootRequired = false).isNotEmpty()
        }
        composeRule.onNodeWithTag(
            "student-row-20000000-0000-0000-0000-000000000001:30000000-0000-0000-0000-000000000001",
        ).performClick()
        composeRule.onNodeWithTag(
            "student-subject-record-40000000-0000-0000-0000-000000000001",
        ).performClick()
        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithTag("quick-capture-input")
                .fetchSemanticsNodes(atLeastOneRootRequired = false)
                .isNotEmpty()
        }
    }

    private fun awaitLocalSafe() {
        composeRule.waitUntil(timeoutMillis = 5_000) {
            val safe = composeRule.onAllNodesWithText("草稿已保存在本机")
                .fetchSemanticsNodes(atLeastOneRootRequired = false)
                .isNotEmpty()
            val recovered = composeRule.onAllNodesWithText("已恢复本机草稿")
                .fetchSemanticsNodes(atLeastOneRootRequired = false)
                .isNotEmpty()
            safe || recovered
        }
    }
}
