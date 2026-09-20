package com.xueqing.app.presentation

import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import com.xueqing.app.presentation.design.XueqingTheme
import org.junit.Rule
import org.junit.Test

class QuickCaptureAttachmentUiInstrumentedTest {
    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun attachmentFailureLeavesTextVisibleAndSubmittable() {
        composeRule.setContent {
            XueqingTheme {
                QuickCaptureScreen(
                    state = QuickCaptureUiState(
                        text = "照片失败也不能影响这条课堂观察。",
                        teachingContextStatus = TeachingContextStatus.Ready,
                        studentDisplayName = "虚构学生甲",
                        subjectLabel = "语文",
                        draftStatus = LocalDraftStatus.SafeOnDevice,
                        attachmentStatus = AttachmentDraftStatus.Failed,
                        attachmentErrorCode = "source_unavailable",
                    ),
                    onTextChanged = {},
                    onClose = {},
                    onChooseStudent = {},
                    onPhotoSelected = {},
                    onRemovePhoto = {},
                    onDiscard = {},
                    onSubmit = {},
                )
            }
        }

        composeRule.onNodeWithTag("quick-capture-input")
            .assertTextContains("照片失败也不能影响这条课堂观察。")
        composeRule.onNodeWithText("照片读取失败 · 文字草稿不受影响")
            .assertExists()
        composeRule.onNodeWithText("提交记录")
            .assertIsEnabled()
    }

    @Test
    fun protectingPhotoTemporarilyBlocksEpochRetiringActions() {
        composeRule.setContent {
            XueqingTheme {
                QuickCaptureScreen(
                    state = QuickCaptureUiState(
                        text = "文字仍然保留并可以继续编辑。",
                        teachingContextStatus = TeachingContextStatus.Ready,
                        studentDisplayName = "虚构学生甲",
                        subjectLabel = "语文",
                        draftStatus = LocalDraftStatus.SafeOnDevice,
                        attachmentStatus = AttachmentDraftStatus.Protecting,
                    ),
                    onTextChanged = {},
                    onClose = {},
                    onChooseStudent = {},
                    onPhotoSelected = {},
                    onRemovePhoto = {},
                    onDiscard = {},
                    onSubmit = {},
                )
            }
        }

        composeRule.onNodeWithText("正在保护照片…").assertExists()
        composeRule.onNodeWithText("添加照片").assertIsNotEnabled()
        composeRule.onNodeWithText("丢弃草稿").assertIsNotEnabled()
        composeRule.onNodeWithText("提交记录").assertIsNotEnabled()
    }
}
