package com.xueqing.app.presentation.design

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

/**
 * Quiet Ink Editorial ("素墨手记") Android design-system palette.
 *
 * Aligns with the Linux desktop client: warm alabaster paper in light mode,
 * calm neutral graphite in dark mode, hairline dividers, and a single
 * restrained studio slate-blue accent.
 */
private val XueqingLightColorScheme = lightColorScheme(
    primary = Color(0xFF2C5E8A),
    onPrimary = Color(0xFFFFFFFF),
    primaryContainer = Color(0xFFE3EEF8),
    onPrimaryContainer = Color(0xFF153450),
    secondary = Color(0xFF5A626C),
    onSecondary = Color(0xFFFFFFFF),
    secondaryContainer = Color(0xFFECEAE6),
    onSecondaryContainer = Color(0xFF22252A),
    tertiary = Color(0xFFA66B14),
    onTertiary = Color(0xFFFFFFFF),
    tertiaryContainer = Color(0xFFF9EFE0),
    onTertiaryContainer = Color(0xFF5C3A06),
    background = Color(0xFFFAF9F7),
    onBackground = Color(0xFF1C1D1F),
    surface = Color(0xFFFAF9F7),
    onSurface = Color(0xFF1C1D1F),
    surfaceVariant = Color(0xFFF1EFEA),
    onSurfaceVariant = Color(0xFF636870),
    surfaceContainerLowest = Color(0xFFFFFFFF),
    surfaceContainerLow = Color(0xFFF5F3EF),
    surfaceContainer = Color(0xFFF1EFEA),
    surfaceContainerHigh = Color(0xFFEAE7E1),
    outline = Color(0xFFB5B2AA),
    outlineVariant = Color(0xFFE5E2DC),
    error = Color(0xFFB83B3B),
    onError = Color(0xFFFFFFFF),
)

private val XueqingDarkColorScheme = darkColorScheme(
    primary = Color(0xFF88B4DC),
    onPrimary = Color(0xFF0F263B),
    primaryContainer = Color(0xFF1F3E5B),
    onPrimaryContainer = Color(0xFFDCEBF8),
    secondary = Color(0xFF9AA0A8),
    onSecondary = Color(0xFF1A1C20),
    secondaryContainer = Color(0xFF272A30),
    onSecondaryContainer = Color(0xFFE3E6EA),
    tertiary = Color(0xFFD9A24E),
    onTertiary = Color(0xFF332004),
    tertiaryContainer = Color(0xFF4D340B),
    onTertiaryContainer = Color(0xFFF9E6C4),
    background = Color(0xFF141517),
    onBackground = Color(0xFFEAECEF),
    surface = Color(0xFF141517),
    onSurface = Color(0xFFEAECEF),
    surfaceVariant = Color(0xFF202226),
    onSurfaceVariant = Color(0xFF969CA6),
    surfaceContainerLowest = Color(0xFF101113),
    surfaceContainerLow = Color(0xFF1A1C20),
    surfaceContainer = Color(0xFF202227),
    surfaceContainerHigh = Color(0xFF282B31),
    outline = Color(0xFF464A52),
    outlineVariant = Color(0xFF2A2D33),
    error = Color(0xFFE07070),
    onError = Color(0xFF421212),
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
