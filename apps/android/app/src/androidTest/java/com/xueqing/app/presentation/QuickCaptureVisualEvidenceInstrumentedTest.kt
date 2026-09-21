package com.xueqing.app.presentation

import android.graphics.Bitmap
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.DarkMode
import androidx.compose.ui.test.DeviceConfigurationOverride
import androidx.compose.ui.test.FontScale
import androidx.compose.ui.test.WindowSize
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.printToString
import androidx.compose.ui.test.then
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.test.platform.app.InstrumentationRegistry
import com.xueqing.app.presentation.design.XueqingTheme
import java.io.File
import java.io.FileOutputStream
import org.junit.Rule
import org.junit.Test

class QuickCaptureVisualEvidenceInstrumentedTest {
    @get:Rule
    val composeRule = createComposeRule()

    @Test
    fun compactLightNormalEvidence() =
        captureEvidence("compact-light-normal", DpSize(360.dp, 800.dp), 1f, false)

    @Test
    fun shortDarkTwoHundredPercentEvidence() =
        captureEvidence("short-dark-200", DpSize(360.dp, 480.dp), 2f, true)

    @Test
    fun landscapeLightTwoHundredPercentEvidence() =
        captureEvidence("landscape-light-200", DpSize(800.dp, 360.dp), 2f, false)

    @Test
    fun tabletDarkTwoHundredPercentEvidence() =
        captureEvidence("tablet-dark-200", DpSize(1280.dp, 800.dp), 2f, true)

    private fun captureEvidence(
        name: String,
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
                    QuickCaptureScreen(
                        state = QuickCaptureUiState(
                            text = "视觉验收：概括题仍会遗漏限制条件，但学生已经能稳定定位关键句。",
                            teachingContextStatus = TeachingContextStatus.Ready,
                            studentDisplayName = "虚构学生甲",
                            subjectLabel = "语文",
                            draftStatus = LocalDraftStatus.SafeOnDevice,
                            attachmentStatus = AttachmentDraftStatus.SafeOnDevice,
                            attachmentCount = 1,
                            submissionStatus = ObservationSubmissionStatus.WaitingToRetry,
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
        composeRule.waitForIdle()

        val targetContext = InstrumentationRegistry.getInstrumentation().targetContext
        val evidenceDir = File(targetContext.filesDir, "android-ux-evidence")
        check(evidenceDir.exists() || evidenceDir.mkdirs()) {
            "Could not create Android UX evidence directory."
        }

        val root = composeRule.onRoot(useUnmergedTree = true)
        val image = root.captureToImage(timeoutMillis = 5_000).asAndroidBitmap()
        FileOutputStream(File(evidenceDir, "$name.png")).use { output ->
            check(image.compress(Bitmap.CompressFormat.PNG, 100, output)) {
                "Could not write $name.png"
            }
        }
        File(evidenceDir, "$name-semantics.txt").writeText(
            root.printToString(maxDepth = 40),
            Charsets.UTF_8,
        )
    }
}
