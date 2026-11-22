package com.xueqing.app.presentation.design

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp
import com.xueqing.app.presentation.shell.PrimaryDestination

@Composable
internal fun SectionTitle(
    text: String,
    modifier: Modifier = Modifier,
) {
    Text(
        text = text,
        style = MaterialTheme.typography.titleMedium,
        modifier = modifier.padding(bottom = 8.dp),
    )
}

@Composable
internal fun EmptyMessage(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.bodyLarge,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(vertical = 12.dp),
    )
}

@Composable
internal fun StatusMessage(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.bodyMedium,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(vertical = 12.dp),
    )
}

/**
 * Crisp dependency-free 24dp vector navigation glyphs matching the restrained
 * "青墨书卷" aesthetic without adding extra icon-pack artifacts.
 */
@Composable
internal fun PrimaryDestinationIcon(
    destination: PrimaryDestination,
    modifier: Modifier = Modifier,
) {
    val tint = LocalContentColor.current
    Canvas(modifier = modifier.size(22.dp)) {
        val strokeWidth = 1.8.dp.toPx()
        val stroke = Stroke(width = strokeWidth, cap = StrokeCap.Round)
        val w = size.width
        val h = size.height

        when (destination) {
            PrimaryDestination.Today -> {
                // Checklist / daily cadence icon
                drawRoundRect(
                    color = tint,
                    topLeft = Offset(w * 0.14f, h * 0.16f),
                    size = Size(w * 0.72f, h * 0.68f),
                    cornerRadius = CornerRadius(3.dp.toPx(), 3.dp.toPx()),
                    style = stroke,
                )
                drawLine(tint, Offset(w * 0.30f, h * 0.40f), Offset(w * 0.70f, h * 0.40f), strokeWidth, StrokeCap.Round)
                drawLine(tint, Offset(w * 0.30f, h * 0.60f), Offset(w * 0.58f, h * 0.60f), strokeWidth, StrokeCap.Round)
            }

            PrimaryDestination.Students -> {
                // Student avatar / roster icon
                drawCircle(
                    color = tint,
                    radius = w * 0.17f,
                    center = Offset(w * 0.50f, h * 0.33f),
                    style = stroke,
                )
                drawArc(
                    color = tint,
                    startAngle = 180f,
                    sweepAngle = 180f,
                    useCenter = false,
                    topLeft = Offset(w * 0.18f, h * 0.56f),
                    size = Size(w * 0.64f, h * 0.48f),
                    style = stroke,
                )
            }

            PrimaryDestination.Learning -> {
                // Longitudinal trend / paper chart icon
                drawLine(tint, Offset(w * 0.16f, h * 0.80f), Offset(w * 0.84f, h * 0.80f), strokeWidth, StrokeCap.Round)
                drawLine(tint, Offset(w * 0.24f, h * 0.64f), Offset(w * 0.44f, h * 0.44f), strokeWidth, StrokeCap.Round)
                drawLine(tint, Offset(w * 0.44f, h * 0.44f), Offset(w * 0.60f, h * 0.54f), strokeWidth, StrokeCap.Round)
                drawLine(tint, Offset(w * 0.60f, h * 0.54f), Offset(w * 0.80f, h * 0.24f), strokeWidth, StrokeCap.Round)
            }
        }
    }
}
