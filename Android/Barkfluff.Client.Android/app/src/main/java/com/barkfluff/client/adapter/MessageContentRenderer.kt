package com.barkfluff.client.adapter

import android.widget.LinearLayout
import android.widget.TextView
import com.barkfluff.client.utils.MarkdownRenderer
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Owns the content-only part of a message row. It has no knowledge of row state, navigation or
 * storage, so it can be reused by sent/received/quote renderers and exercised independently.
 */
class MessageContentRenderer {
    fun renderText(container: LinearLayout, template: TextView, text: String) {
        MarkdownRenderer.renderMessageInto(container, template, text)
    }

    fun clearText(container: LinearLayout, template: TextView) {
        MarkdownRenderer.clearMessageContent(container, template)
    }

    fun plainText(text: String): String = MarkdownRenderer.strip(text)

    fun formatTime(timestampMillis: Long): String {
        if (timestampMillis <= 0L) return ""
        return SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(timestampMillis))
    }
}
