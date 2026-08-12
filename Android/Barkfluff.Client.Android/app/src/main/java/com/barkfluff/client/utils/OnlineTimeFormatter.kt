package com.barkfluff.client.utils

import android.content.Context
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.R
import java.text.SimpleDateFormat
import java.util.Date
import java.util.concurrent.TimeUnit

object OnlineTimeFormatter {

    fun formatLastSeen(context: Context, lastSeenMs: Long): String {
        if (lastSeenMs <= 0L) return context.getString(R.string.status_recently_seen)

        return if (GlobalParam(context).relativeOnlineTime) {
            formatRelative(context, lastSeenMs)
        } else {
            val locale = context.resources.configuration.locales[0]
            val time = SimpleDateFormat("H:mm", locale).format(Date(lastSeenMs))
            context.getString(R.string.online_last_seen_at, time)
        }
    }

    private fun formatRelative(context: Context, lastSeenMs: Long): String {
        val diff = (System.currentTimeMillis() - lastSeenMs).coerceAtLeast(0L)

        return when {
            diff < TimeUnit.MINUTES.toMillis(1) -> context.getString(R.string.online_just_now)
            diff < TimeUnit.HOURS.toMillis(1) -> {
                val minutes = TimeUnit.MILLISECONDS.toMinutes(diff)
                context.resources.getQuantityString(R.plurals.online_minutes, minutes.toInt(), minutes)
            }
            diff < TimeUnit.DAYS.toMillis(1) -> {
                val hours = TimeUnit.MILLISECONDS.toHours(diff)
                context.resources.getQuantityString(R.plurals.online_hours, hours.toInt(), hours)
            }
            else -> {
                val days = TimeUnit.MILLISECONDS.toDays(diff)
                context.resources.getQuantityString(R.plurals.online_days, days.toInt(), days)
            }
        }
    }
}
