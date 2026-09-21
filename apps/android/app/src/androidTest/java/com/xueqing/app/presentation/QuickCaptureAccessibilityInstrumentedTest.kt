package com.xueqing.app.presentation

import androidx.compose.ui.test.junit4.accessibility.enableAccessibilityChecks
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.tryPerformAccessibilityChecks
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.filters.SdkSuppress
import com.xueqing.app.MainActivity
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
@SdkSuppress(minSdkVersion = 34)
class QuickCaptureAccessibilityInstrumentedTest {
    @get:Rule
    val composeRule = createAndroidComposeRule<MainActivity>()

    @Test
    fun corePersonalCaptureJourneyPassesAutomatedAccessibilityChecks() {
        composeRule.enableAccessibilityChecks()
        composeRule.onRoot().tryPerformAccessibilityChecks()

        composeRule.onNodeWithText("学生").performClick()
        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithTag(
                "student-row-20000000-0000-0000-0000-000000000001:30000000-0000-0000-0000-000000000001",
            ).fetchSemanticsNodes(atLeastOneRootRequired = false).isNotEmpty()
        }
        composeRule.onRoot().tryPerformAccessibilityChecks()

        composeRule.onNodeWithTag(
            "student-row-20000000-0000-0000-0000-000000000001:30000000-0000-0000-0000-000000000001",
        ).performClick()
        composeRule.onRoot().tryPerformAccessibilityChecks()

        composeRule.onNodeWithTag(
            "student-subject-record-40000000-0000-0000-0000-000000000001",
        ).performClick()
        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithTag("quick-capture-input")
                .fetchSemanticsNodes(atLeastOneRootRequired = false)
                .isNotEmpty()
        }
        composeRule.onRoot().tryPerformAccessibilityChecks()
    }
}
