package com.xueqing.app.presentation

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.DarkMode
import androidx.compose.ui.test.DeviceConfigurationOverride
import androidx.compose.ui.test.FontScale
import androidx.compose.ui.test.WindowSize
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.then
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import com.xueqing.app.presentation.design.XueqingTheme
import org.junit.Rule
import org.junit.Test

class QuickCaptureAdaptiveUiInstrumentedTest {
    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun compactPortraitAtNormalTextKeepsCaptureReachable() =
        assertCaptureReachable(DpSize(360.dp, 800.dp), fontScale = 1f, darkMode = false)

    @Test
    fun intentionallyShortPhoneAtTwoHundredPercentTextKeepsCaptureReachable() =
        assertCaptureReachable(DpSize(360.dp, 480.dp), fontScale = 2f, darkMode = true)

    @Test
    fun landscapePressureAtTwoHundredPercentTextKeepsCaptureReachable() =
        assertCaptureReachable(DpSize(800.dp, 360.dp), fontScale = 2f, darkMode = false)

    @Test
    fun expandedWindowAtTwoHundredPercentTextKeepsCaptureReachable() =
        assertCaptureReachable(DpSize(840.dp, 700.dp), fontScale = 2f, darkMode = true)

    @Test
    fun smallTabletAtTwoHundredPercentTextKeepsCaptureReachable() =
        assertCaptureReachable(DpSize(1024.dp, 640.dp), fontScale = 2f, darkMode = false)

    @Test
    fun largeTabletAtTwoHundredPercentTextKeepsCaptureReachable() =
        assertCaptureReachable(DpSize(1280.dp, 800.dp), fontScale = 2f, darkMode = true)

    private fun assertCaptureReachable(
        size: DpSize,
        fontScale: Float,
        darkMode: Boolean,
    ) {
        composeRule.setContent {
            DeviceConfigurationOverride(
                DeviceConfigurationOverride.WindowSize(size) then
                    DeviceConfigurationOverride.FontScale(fontScale) then
                    DeviceConfigurationOverride.DarkMode(darkMode),
            ) {
                XueqingTheme {
                    Surface(
                        modifier = Modifier.fillMaxSize(),
                        color = MaterialTheme.colorScheme.background,
                    ) {
                        QuickCaptureScreen(
                            state = QuickCaptureUiState(
                            text = "大字号与窗口压力测试：这是一条较长的中文课堂观察，用来验证换行、滚动和提交入口不会被裁掉。",
                            teachingContextStatus = TeachingContextStatus.Ready,
                            studentDisplayName = "虚构学生甲",
                            subjectLabel = "语文",
                            draftStatus = LocalDraftStatus.SafeOnDevice,
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
            }
        }

        composeRule.onNodeWithTag("quick-capture-input")
            .performScrollTo()
            .assertIsDisplayed()
        composeRule.onNodeWithTag("quick-capture-submit")
            .performScrollTo()
            .assertIsDisplayed()
            .assertIsEnabled()
    }
}
