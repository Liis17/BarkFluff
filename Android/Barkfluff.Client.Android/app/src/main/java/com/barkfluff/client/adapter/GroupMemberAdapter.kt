package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemGroupMemberBinding
import com.barkfluff.client.utils.AvatarLoader

/**
 * Адаптер списка участников группового чата.
 */
class GroupMemberAdapter(
    private val getFileUrl: suspend (String) -> String?,
    private val onRemove: (MemberItem) -> Unit
) : ListAdapter<GroupMemberAdapter.MemberItem, GroupMemberAdapter.MemberViewHolder>(DiffCallback()) {

    data class MemberItem(
        val userId: Long,
        val name: String,
        val avatarFileId: String?,
        val canRemove: Boolean
    )

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): MemberViewHolder {
        val binding = ItemGroupMemberBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return MemberViewHolder(binding)
    }

    override fun onBindViewHolder(holder: MemberViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    inner class MemberViewHolder(
        private val binding: ItemGroupMemberBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: MemberItem) {
            binding.memberName.text = item.name

            AvatarLoader.loadByFileId(
                imageView = binding.memberAvatar,
                placeholderView = binding.memberAvatarPlaceholder,
                fileId = item.avatarFileId,
                displayName = item.name,
                userId = item.userId
            ) { item.avatarFileId?.let { fid -> getFileUrl(fid) } }

            binding.memberRemoveButton.visibility = if (item.canRemove) View.VISIBLE else View.GONE
            binding.memberRemoveButton.setOnClickListener { onRemove(item) }
        }
    }

    class DiffCallback : DiffUtil.ItemCallback<MemberItem>() {
        override fun areItemsTheSame(oldItem: MemberItem, newItem: MemberItem): Boolean =
            oldItem.userId == newItem.userId

        override fun areContentsTheSame(oldItem: MemberItem, newItem: MemberItem): Boolean =
            oldItem == newItem
    }
}
