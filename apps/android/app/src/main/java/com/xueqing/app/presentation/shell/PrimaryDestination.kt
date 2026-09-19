package com.xueqing.app.presentation.shell

/**
 * Stable top-level Personal-workspace destinations.
 *
 * This type intentionally contains product-level destination identity only.
 * Navigation-library state and screen implementation stay outside it so either
 * can evolve independently.
 */
internal enum class PrimaryDestination(
    val label: String,
    val bootstrapGlyph: String,
) {
    Today("今日", "今"),
    Students("学生", "生"),
    Learning("学情", "学"),
}
