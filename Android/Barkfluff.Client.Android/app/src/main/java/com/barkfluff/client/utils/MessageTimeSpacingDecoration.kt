package com.barkfluff.client.utils

import android.graphics.Rect
import android.view.View
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.adapter.MessageAdapter
import com.barkfluff.client.adapter.MessageType

/**
 * ItemDecoration, добавляющая увеличенный вертикальный отступ между группами сообщений,
 * разделёнными по времени. Если между двумя соседними сообщениями прошло больше
 * [timeGapThresholdMs] миллисекунд, к верхнему отступу элемента добавляется
 * [largeSpacingPx]; иначе добавляется [smallSpacingPx].
 *
 * Разделители дат и непрочитанных сообщений не участвуют в расчёте отступов —
 * они уже имеют собственные паддинги в layout-файлах.
 */
class MessageTimeSpacingDecoration(
    private val smallSpacingPx: Int,
    private val largeSpacingPx: Int,
    private val timeGapThresholdMs: Long = 10 * 60 * 1000L // 10 минут
) : RecyclerView.ItemDecoration() {

    override fun getItemOffsets(
        outRect: Rect,
        view: View,
        parent: RecyclerView,
        state: RecyclerView.State
    ) {
        val adapter = parent.adapter as? MessageAdapter ?: return
        val position = parent.getChildAdapterPosition(view)
        if (position == RecyclerView.NO_POSITION) return

        val currentItem = adapter.currentList.getOrNull(position) ?: return

        // Отступы применяются только к обычным сообщениям
        if (currentItem.type != MessageType.MESSAGE) return

        // Ищем предыдущее обычное сообщение (пропускаем разделители)
        var prevMessageItem: com.barkfluff.client.adapter.MessageItem? = null
        for (i in position - 1 downTo 0) {
            val item = adapter.currentList.getOrNull(i)
            if (item != null && item.type == MessageType.MESSAGE) {
                prevMessageItem = item
                break
            }
        }

        if (prevMessageItem == null) return

        val timeDiff = kotlin.math.abs(currentItem.timestamp - prevMessageItem.timestamp)
        outRect.top = if (timeDiff > timeGapThresholdMs) largeSpacingPx else smallSpacingPx
    }
}
