package com.barkfluff.client.adapter

import android.content.Context
import android.graphics.Canvas
import android.graphics.PorterDuff
import android.graphics.PorterDuffColorFilter
import android.graphics.drawable.Drawable
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.HorizontalScrollView
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
    internal var isReplySwipeBlocked = false

    override fun getMovementFlags(recyclerView: RecyclerView, viewHolder: RecyclerView.ViewHolder): Int {
        if (isReplySwipeBlocked) return 0
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

class ReplySwipeTableTouchGate(
    private val swipeCallback: ReplySwipeCallback
) : RecyclerView.SimpleOnItemTouchListener() {

    override fun onInterceptTouchEvent(recyclerView: RecyclerView, event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                swipeCallback.isReplySwipeBlocked = isTouchOnScrollableTable(recyclerView, event)
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                swipeCallback.isReplySwipeBlocked = false
            }
        }
        return false
    }

    private fun isTouchOnScrollableTable(recyclerView: RecyclerView, event: MotionEvent): Boolean {
        val itemView = recyclerView.findChildViewUnder(event.x, event.y) ?: return false
        val recyclerLocation = IntArray(2)
        recyclerView.getLocationOnScreen(recyclerLocation)
        val touchX = (recyclerLocation[0] + event.x).toInt()
        val touchY = (recyclerLocation[1] + event.y).toInt()
        return findScrollableTableAt(itemView, touchX, touchY)
    }

    private fun findScrollableTableAt(view: View, touchX: Int, touchY: Int): Boolean {
        if (view is HorizontalScrollView &&
            (view.canScrollHorizontally(-1) || view.canScrollHorizontally(1))
        ) {
            val location = IntArray(2)
            view.getLocationOnScreen(location)
            if (touchX in location[0]..(location[0] + view.width) &&
                touchY in location[1]..(location[1] + view.height)
            ) {
                return true
            }
        }
        if (view !is ViewGroup) return false
        for (index in 0 until view.childCount) {
            if (findScrollableTableAt(view.getChildAt(index), touchX, touchY)) return true
        }
        return false
    }
}
