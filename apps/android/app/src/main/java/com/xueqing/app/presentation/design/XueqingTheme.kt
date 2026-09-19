package com.xueqing.app.presentation.design

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable

/**
 * Single Android design-system entry point.
 *
 * Keep screen/business semantics outside this file. Future visual-system changes
 * (color, typography, shapes) should be centralized here so feature surfaces can
 * evolve without touching application/domain behavior.
 */
@Composable
internal fun XueqingTheme(
    content: @Composable () -> Unit,
) {
    val colorScheme = if (isSystemInDarkTheme()) {
        darkColorScheme()
    } else {
        lightColorScheme()
    }

    MaterialTheme(
        colorScheme = colorScheme,
        content = content,
    )
}
