package com.xueqing.app.presentation

import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.assertTextEquals
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextReplacement
import androidx.lifecycle.Lifecycle
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.MainActivity
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class QuickCaptureLifecycleInstrumentedTest {
    @get:Rule
    val composeRule = createAndroidComposeRule<MainActivity>()

    @Test
    fun typedObservationSurvivesActivityRecreation() {
        openQuickCapture()
        val text = "生命周期测试：概括题仍有原句搬运，但关键句定位已经稳定。"

        composeRule.onNodeWithTag("quick-capture-input").performTextReplacement(text)
        awaitLocalSafe()

        composeRule.activityRule.scenario.recreate()

        composeRule.onNodeWithTag("quick-capture-input").assertTextContains(text)
        awaitLocalSafe()
    }

    @Test
    fun typedObservationSurvivesBackgroundForegroundTransition() {
        openQuickCapture()
        val text = "前后台测试：课堂切换到相机后返回，文字不能消失。"

        composeRule.onNodeWithTag("quick-capture-input").performTextReplacement(text)
        awaitLocalSafe()

        composeRule.activityRule.scenario.moveToState(Lifecycle.State.CREATED)
        composeRule.activityRule.scenario.moveToState(Lifecycle.State.RESUMED)

        composeRule.onNodeWithTag("quick-capture-input").assertTextContains(text)
        awaitLocalSafe()
    }

    @Test
    fun discardAfterLifecycleFlushDoesNotReturnAfterActivityRecreation() {
        openQuickCapture()
        val text = "丢弃测试：生命周期 flush 之后显式删除，Activity 重建后也不能复活。"

        composeRule.onNodeWithTag("quick-capture-input").performTextReplacement(text)
        awaitLocalSafe()

        // Exercise the lifecycle flush path before the destructive barrier. A
        // queued/finished flush must not weaken the later explicit discard.
        composeRule.activityRule.scenario.moveToState(Lifecycle.State.CREATED)
        composeRule.activityRule.scenario.moveToState(Lifecycle.State.RESUMED)
        composeRule.onNodeWithTag("quick-capture-input").assertTextContains(text)
        awaitLocalSafe()

        composeRule.onNodeWithText("丢弃草稿").performClick()
        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithText(text)
                .fetchSemanticsNodes(atLeastOneRootRequired = false)
                .isEmpty()
        }
        composeRule.onNodeWithTag("quick-capture-input").assertTextEquals("")
        awaitLocalSafe()

        composeRule.activityRule.scenario.recreate()

        composeRule.onNodeWithTag("quick-capture-input").assertTextEquals("")
        awaitLocalSafe()
    }

    private fun openQuickCapture() {
        composeRule.onNodeWithText("记录").performClick()
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
