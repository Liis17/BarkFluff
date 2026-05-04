package com.barkfluff.client.adapter

import android.content.Context
import android.graphics.Canvas
import android.graphics.PorterDuff
import android.graphics.PorterDuffColorFilter
import android.graphics.drawable.Drawable
import android.view.HapticFeedbackConstants
import android.view.View
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.ItemTouchHelper
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import kotlin.math.abs
import kotlin.math.min

/**
 * Свайп влево по сообщению — триггер reply.
 * Поведение: bubble сдвигается за пальцем, при достижении порога — haptic + onSwipeTriggered,
 * после отпускания возвращается на место (визуально через notifyItemChanged).
 */
class ReplySwipeCallback(
    private val context: Context,
    private val onSwipeTriggered: (position: Int) -> Unit
) : ItemTouchHelper.SimpleCallback(0, ItemTouchHelper.LEFT) {

    private val triggerDistancePx: Float = 64f * context.resources.displayMetrics.density
    private val maxDragPx: Float = 96f * context.resources.displayMetrics.density

    private val replyIcon: Drawable? = ContextCompat.getDrawable(context, R.drawable.ic_reply)?.mutate()?.apply {
        val tv = android.util.TypedValue()
        context.theme.resolveAttribute(androidx.appcompat.R.attr.colorPrimary, tv, true)
        colorFilter = PorterDuffColorFilter(tv.data, PorterDuff.Mode.SRC_IN)
    }

    private val triggeredHolders = mutableSetOf<Int>()

    override fun getMovementFlags(recyclerView: RecyclerView, viewHolder: RecyclerView.ViewHolder): Int {
        if (viewHolder !is MessageAdapter.SentMessageViewHolder &&
            viewHolder !is MessageAdapter.ReceivedMessageViewHolder) {
            return 0
        }
        return makeMovementFlags(0, ItemTouchHelper.LEFT)
    }

    override fun onMove(
        recyclerView: RecyclerView,
        viewHolder: RecyclerView.ViewHolder,
        target: RecyclerView.ViewHolder
    ): Boolean = false

    override fun onSwiped(viewHolder: RecyclerView.ViewHolder, direction: Int) {
        // Не убираем элемент. Триггер срабатывает в onChildDraw при пересечении порога.
    }

    override fun getSwipeThreshold(viewHolder: RecyclerView.ViewHolder): Float = 10f

    override fun getSwipeEscapeVelocity(defaultValue: Float): Float = Float.MAX_VALUE

    override fun getSwipeVelocityThreshold(defaultValue: Float): Float = Float.MAX_VALUE

    override fun onChildDraw(
        c: Canvas,
        recyclerView: RecyclerView,
        viewHolder: RecyclerView.ViewHolder,
        dX: Float,
        dY: Float,
        actionState: Int,
        isCurrentlyActive: Boolean
    ) {
        val itemView = viewHolder.itemView

        val clampedDx = if (dX < 0) {
            -min(abs(dX), maxDragPx)
        } else 0f

        itemView.translationX = clampedDx

        if (actionState == ItemTouchHelper.ACTION_STATE_SWIPE && isCurrentlyActive) {
            val absDx = abs(clampedDx)
            val progress = (absDx / triggerDistancePx).coerceIn(0f, 1f)

            replyIcon?.let { icon ->
                val iconSize = (24f * context.resources.displayMetrics.density).toInt()
                val iconCenterY = itemView.top + itemView.height / 2
                val iconRight = itemView.right - (8f * context.resources.displayMetrics.density).toInt()
                val iconLeft = iconRight - iconSize
                icon.setBounds(iconLeft, iconCenterY - iconSize / 2, iconRight, iconCenterY + iconSize / 2)
                icon.alpha = (progress * 255).toInt()
                icon.draw(c)
            }

            val pos = viewHolder.bindingAdapterPosition
            if (absDx >= triggerDistancePx && !triggeredHolders.contains(pos)) {
                triggeredHolders.add(pos)
                itemView.performHapticFeedback(HapticFeedbackConstants.LONG_PRESS)
                onSwipeTriggered(pos)
            } else if (absDx < triggerDistancePx) {
                triggeredHolders.remove(pos)
            }
        }
    }

    override fun clearView(recyclerView: RecyclerView, viewHolder: RecyclerView.ViewHolder) {
        super.clearView(recyclerView, viewHolder)
        viewHolder.itemView.translationX = 0f
        triggeredHolders.remove(viewHolder.bindingAdapterPosition)
    }
}
