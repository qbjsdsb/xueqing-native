package com.xueqing.app.presentation.design

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

/**
 * Single Android design-system entry point ("青墨书卷" Teal-Ink & Paper palette).
 *
 * Restrained educational palette aligned with the Linux GTK4 desktop client:
 * deep teal ink accent, warm paper light surface, calm slate-ink dark surface,
 * and WCAG AA+ contrast across normal and 200% font scales.
 */
private val XueqingLightColorScheme = lightColorScheme(
    primary = Color(0xFF0F766E),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFCCFBF1),
    onPrimaryContainer = Color(0xFF0A3D39),
    secondary = Color(0xFF475569),
    onSecondary = Color(0xFFFFFFFF),
    secondaryContainer = Color(0xFFE2E8F0),
    onSecondaryContainer = Color(0xFF1E293B),
    tertiary = Color(0xFFB45309),
    onTertiary = Color(0xFFFFFFFF),
    tertiaryContainer = Color(0xFFFEF3C7),
    onTertiaryContainer = Color(0xFF78350F),
    background = Color(0xFFF8F9F7),
    onBackground = Color(0xFF182026),
    surface = Color(0xFFF8F9F7),
    onSurface = Color(0xFF182026),
    surfaceVariant = Color(0xFFEDF2F0),
    onSurfaceVariant = Color(0xFF475569),
    surfaceContainerLowest = Color(0xFFFFFFFF),
    surfaceContainerLow = Color(0xFFF3F6F5),
    surfaceContainer = Color(0xFFEDF2F0),
    surfaceContainerHigh = Color(0xFFE5ECE9),
    outline = Color(0xFF94A3B8),
    outlineVariant = Color(0xFFD5DFDC),
    error = Color(0xFFB91C1C),
    onError = Color(0xFFFFFFFF),
)

private val XueqingDarkColorScheme = darkColorScheme(
    primary = Color(0xFF2DD4BF),
    onPrimary = Color(0xFF052E2B),
    primaryContainer = Color(0xFF115E59),
    onPrimaryContainer = Color(0xFFCCFBF1),
    secondary = Color(0xFF94A3B8),
    onSecondary = Color(0xFF0F172A),
    secondaryContainer = Color(0xFF283442),
    onSecondaryContainer = Color(0xFFE2E8F0),
    tertiary = Color(0xFFF59E0B),
    onTertiary = Color(0xFF3B1B04),
    tertiaryContainer = Color(0xFF5C3409),
    onTertiaryContainer = Color(0xFFFEF3C7),
    background = Color(0xFF12161A),
    onBackground = Color(0xFFE6ECEF),
    surface = Color(0xFF12161A),
    onSurface = Color(0xFFE6ECEF),
    surfaceVariant = Color(0xFF1F272E),
    onSurfaceVariant = Color(0xFFA6B4C0),
    surfaceContainerLowest = Color(0xFF0E1216),
    surfaceContainerLow = Color(0xFF181E24),
    surfaceContainer = Color(0xFF1E262E),
    surfaceContainerHigh = Color(0xFF26303A),
    outline = Color(0xFF4A5968),
    outlineVariant = Color(0xFF2D3844),
    error = Color(0xFFF87171),
    onError = Color(0xFF450A0A),
)

@Composable
internal fun XueqingTheme(
    content: @Composable () -> Unit,
) {
    val colorScheme = if (isSystemInDarkTheme()) {
        XueqingDarkColorScheme
    } else {
        XueqingLightColorScheme
    }

    MaterialTheme(
        colorScheme = colorScheme,
        content = content,
    )
}
