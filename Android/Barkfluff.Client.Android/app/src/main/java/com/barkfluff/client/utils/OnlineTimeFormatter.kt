package com.barkfluff.client.utils

import android.content.Context
import com.barkfluff.client.data.GlobalParam
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.TimeUnit

object OnlineTimeFormatter {

    fun formatLastSeen(context: Context, lastSeenMs: Long): String {
        return formatLastSeen(lastSeenMs, GlobalParam(context).relativeOnlineTime)
    }

    fun formatLastSeen(lastSeenMs: Long, relative: Boolean): String {
        if (lastSeenMs <= 0L) return "был(а) недавно"

        return if (relative) {
            formatRelative(lastSeenMs)
        } else {
            val time = SimpleDateFormat("H:mm", Locale.getDefault()).format(Date(lastSeenMs))
            "был(а) в $time"
        }
    }

    private fun formatRelative(lastSeenMs: Long): String {
        val diff = (System.currentTimeMillis() - lastSeenMs).coerceAtLeast(0L)

        return when {
            diff < TimeUnit.MINUTES.toMillis(1) -> "был(а) только что"
            diff < TimeUnit.HOURS.toMillis(1) -> {
                val minutes = TimeUnit.MILLISECONDS.toMinutes(diff)
                "был(а) ${pluralize(minutes, "минуту", "минуты", "минут")} назад"
            }
            diff < TimeUnit.DAYS.toMillis(1) -> {
                val hours = TimeUnit.MILLISECONDS.toHours(diff)
                "был(а) ${pluralize(hours, "час", "часа", "часов")} назад"
            }
            else -> {
                val days = TimeUnit.MILLISECONDS.toDays(diff)
                "был(а) ${pluralize(days, "день", "дня", "дней")} назад"
            }
        }
    }

    private fun pluralize(value: Long, one: String, few: String, many: String): String {
        val rem100 = (value % 100).toInt()
        val rem10 = (value % 10).toInt()
        val word = if (rem100 in 11..14) {
            many
        } else {
            when (rem10) {
                1 -> one
                2, 3, 4 -> few
                else -> many
            }
        }
        return "$value $word"
    }
}
