package com.therobm.thump.detail

/**
 * Shared formatters used across all the detail screens. Plain top-level functions because
 * they read no state.
 */

/**
 * Format a duration given in whole seconds as either "M:SS" or "H:MM:SS".
 */
fun formatDurationSeconds(totalSeconds: Int?): String {
    if (totalSeconds == null || totalSeconds <= 0) {
        return ""
    }
    val hours: Int = totalSeconds / 3600
    val minutes: Int = (totalSeconds % 3600) / 60
    val seconds: Int = totalSeconds % 60
    val builder: StringBuilder = StringBuilder()
    if (hours > 0) {
        builder.append(hours)
        builder.append(':')
        if (minutes < 10) {
            builder.append('0')
        }
        builder.append(minutes)
    } else {
        builder.append(minutes)
    }
    builder.append(':')
    if (seconds < 10) {
        builder.append('0')
    }
    builder.append(seconds)
    return builder.toString()
}

fun textOrFallback(input: String?, fallback: String): String {
    if (input == null || input.isEmpty()) {
        return fallback
    }
    return input
}
