package com.xueqing.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test

class PrimaryDestinationTest {
    @Test
    fun personalWorkspaceKeepsThreeTopLevelDestinations() {
        assertEquals(
            listOf("今日", "学生", "学情"),
            PrimaryDestination.entries.map { it.label },
        )
    }

    @Test
    fun recordIsNotATopLevelDestination() {
        assertFalse(PrimaryDestination.entries.any { it.label == "记录" })
    }
}
