package com.barkfluff.client.adapter

import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ItemUserBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader

/**
 * Адаптер для отображения списка пользователей в результатах поиска
 */
class UserAdapter(
    private val onUserClick: (GrpcManager.UserData) -> Unit,
    private val onActionClick: (GrpcManager.UserData) -> Unit,
    private val getFileUrlCallback: suspend (String) -> String?
) : ListAdapter<UserAdapter.UserDisplayItem, UserAdapter.UserViewHolder>(UserDiffCallback()) {

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): UserViewHolder {
        val binding = ItemUserBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return UserViewHolder(binding)
    }

    override fun onBindViewHolder(holder: UserViewHolder, position: Int) {
        holder.bind(getItem(position))
    }

    inner class UserViewHolder(
        private val binding: ItemUserBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        fun bind(item: UserDisplayItem) {
            binding.userFullName.text = item.displayFullName
            binding.userUsername.text = item.displayUsername

            // Загрузка аватара
            AvatarLoader.loadByFileId(
                imageView = binding.userAvatar,
                placeholderView = binding.userAvatarPlaceholder,
                fileId = item.displayAvatarFileId,
                displayName = item.displayFullName,
                userId = item.userData.userId
            ) {
                val fileId = item.displayAvatarFileId
                if (fileId != null) {
                    getFileUrlCallback(fileId)
                } else {
                    null
                }
            }

            // Клик по элементу
            binding.root.setOnClickListener {
                onUserClick(item.userData)
            }

            // Клик по кнопке действия
            binding.actionButton.setOnClickListener {
                onActionClick(item.userData)
            }
        }
    }

    data class UserDisplayItem(
        val userData: GrpcManager.UserData,
        val displayAvatarFileId: String?,
        val displayFullName: String,
        val displayUsername: String
    )

    class UserDiffCallback : DiffUtil.ItemCallback<UserDisplayItem>() {
        override fun areItemsTheSame(oldItem: UserDisplayItem, newItem: UserDisplayItem): Boolean {
            return oldItem.userData.userId == newItem.userData.userId
        }

        override fun areContentsTheSame(oldItem: UserDisplayItem, newItem: UserDisplayItem): Boolean {
            return oldItem == newItem
        }
    }
}
