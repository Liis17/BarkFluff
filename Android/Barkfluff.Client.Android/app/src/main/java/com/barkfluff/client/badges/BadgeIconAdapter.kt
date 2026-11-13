package com.barkfluff.client.badges

import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.domain.model.UserBadgeInfo

internal class BadgeIconAdapter(
    private val onClick: (UserBadgeInfo) -> Unit,
) : ListAdapter<UserBadgeInfo, BadgeIconAdapter.Holder>(DIFF) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): Holder {
        val context = parent.context
        val imageSize = context.dp(40)
        val container = FrameLayout(context).apply {
            layoutParams = RecyclerView.LayoutParams(context.dp(48), context.dp(48)).apply {
                marginEnd = context.dp(4)
            }
            foreground = TypedValue().let { value ->
                context.theme.resolveAttribute(android.R.attr.selectableItemBackgroundBorderless, value, true)
                context.getDrawable(value.resourceId)
            }
            isClickable = true
            isFocusable = true
        }
        val image = ImageView(context).apply {
            layoutParams = FrameLayout.LayoutParams(imageSize, imageSize, Gravity.CENTER)
            scaleType = ImageView.ScaleType.FIT_CENTER
            setImageResource(R.drawable.ic_verified)
        }
        container.addView(image)
        return Holder(container, image)
    }

    override fun onBindViewHolder(holder: Holder, position: Int) {
        val badge = getItem(position)
        holder.image.contentDescription = badge.name
        holder.itemView.contentDescription = badge.name
        BadgeImageLoader.loadMini(holder.image, badge)
        holder.itemView.setOnClickListener { onClick(badge) }
    }

    class Holder(itemView: View, val image: ImageView) : RecyclerView.ViewHolder(itemView)

    private companion object {
        val DIFF = object : DiffUtil.ItemCallback<UserBadgeInfo>() {
            override fun areItemsTheSame(oldItem: UserBadgeInfo, newItem: UserBadgeInfo) =
                oldItem.badgeId == newItem.badgeId

            override fun areContentsTheSame(oldItem: UserBadgeInfo, newItem: UserBadgeInfo) =
                oldItem == newItem
        }
    }
}

private fun android.content.Context.dp(value: Int): Int =
    (value * resources.displayMetrics.density).toInt()
