package com.xueqing.app.presentation

import androidx.compose.ui.test.assertTextContains
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.xueqing.app.MainActivity
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class StudentReferenceInstrumentedTest {
    @get:Rule
    val composeRule = createAndroidComposeRule<MainActivity>()

    @Test
    fun studentDetailOpensSubjectScopedQuickCapture() {
        composeRule.onNodeWithText("学生").performClick()

        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithText("虚构学生甲")
                .fetchSemanticsNodes(atLeastOneRootRequired = false)
                .isNotEmpty()
        }
        composeRule.onNodeWithTag(
            "student-row-20000000-0000-0000-0000-000000000001:30000000-0000-0000-0000-000000000001",
        ).performClick()

        composeRule.onNodeWithText("学生详情").assertTextContains("学生详情")
        composeRule.onNodeWithTag(
            "student-subject-record-40000000-0000-0000-0000-000000000001",
        ).performClick()

        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithText("虚构学生甲 · 语文")
                .fetchSemanticsNodes(atLeastOneRootRequired = false)
                .isNotEmpty()
        }
        composeRule.onNodeWithTag("quick-capture-teaching-context")
            .assertTextContains("虚构学生甲 · 语文")
    }
    @Test
    fun todayAndCurrentFocusExposeAuthoritativeReadState() {
        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithTag(
                "today-action-70000000-0000-0000-0000-000000000001",
            ).fetchSemanticsNodes(atLeastOneRootRequired = false).isNotEmpty()
        }
        composeRule.onNodeWithTag(
            "today-action-70000000-0000-0000-0000-000000000001",
        ).assertTextContains("复核陌生材料中的限制条件")

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
            "student-subject-focus-40000000-0000-0000-0000-000000000001",
        ).performClick()

        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodesWithTag(
                "current-focus-60000000-0000-0000-0000-000000000001",
            ).fetchSemanticsNodes(atLeastOneRootRequired = false).isNotEmpty()
        }
        composeRule.onNodeWithTag(
            "current-focus-60000000-0000-0000-0000-000000000001",
        ).assertExists()
        composeRule.onNodeWithText("跨段概括仍会漏掉限制条件").assertExists()
        composeRule.onNodeWithText("复核陌生材料中的限制条件").assertExists()
    }

}
