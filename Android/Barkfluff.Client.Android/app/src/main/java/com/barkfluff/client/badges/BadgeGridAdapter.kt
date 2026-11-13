package com.barkfluff.client.badges

import android.content.Context
import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ItemBadgeGridBinding
import com.barkfluff.client.domain.model.UserBadgeInfo

internal class BadgeGridAdapter(
    private val onClick: (UserBadgeInfo?) -> Unit,
) : ListAdapter<BadgeGridAdapter.Item, BadgeGridAdapter.Holder>(DIFF) {

    private var interactionsEnabled = true

    fun setInteractionsEnabled(enabled: Boolean) {
        interactionsEnabled = enabled
    }

    fun showBadges(badges: List<UserBadgeInfo>, primaryBadgeId: Int?) {
        submitList(
            if (badges.isEmpty()) emptyList()
            else listOf(Item(badge = null, selected = primaryBadgeId == null)) +
                badges.map { Item(it, it.badgeId == primaryBadgeId) }
        )
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): Holder = Holder(
        ItemBadgeGridBinding.inflate(LayoutInflater.from(parent.context), parent, false)
    )

    override fun onBindViewHolder(holder: Holder, position: Int) = holder.bind(getItem(position))

    inner class Holder(private val binding: ItemBadgeGridBinding) : RecyclerView.ViewHolder(binding.root) {
        fun bind(item: Item) {
            val badge = item.badge
            if (badge != null) {
                binding.badgeName.text = badge.name
                BadgeImageLoader.loadMini(binding.badgeImage, badge)
                binding.root.contentDescription = badge.name
            } else {
                binding.badgeName.setText(R.string.badge_no_primary)
                BadgeImageLoader.showNoBadge(binding.badgeImage)
                binding.root.contentDescription = binding.root.context.getString(R.string.badge_no_primary)
            }
            binding.badgeCheck.visibility = if (item.selected) android.view.View.VISIBLE else android.view.View.GONE
            binding.badgeCard.strokeWidth = if (item.selected) binding.root.context.dp(2) else 0
            binding.badgeCard.strokeColor = ContextCompat.getColor(binding.root.context, R.color.profile_settings_primary)
            binding.root.setOnClickListener {
                if (interactionsEnabled) onClick(badge)
            }
        }
    }

    data class Item(val badge: UserBadgeInfo?, val selected: Boolean)

    private companion object {
        val DIFF = object : DiffUtil.ItemCallback<Item>() {
            override fun areItemsTheSame(oldItem: Item, newItem: Item): Boolean =
                oldItem.badge?.badgeId == newItem.badge?.badgeId

            override fun areContentsTheSame(oldItem: Item, newItem: Item): Boolean = oldItem == newItem
        }
    }
}

private fun Context.dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()
