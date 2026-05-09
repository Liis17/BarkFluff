package com.barkfluff.client

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityFolderChatPickerBinding
import com.barkfluff.client.databinding.ItemPickerChatBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import kotlinx.coroutines.launch

/**
 * Полноэкранный экран мультивыбора чатов для добавления в папку.
 * Возвращает выбранные ID через RESULT_SELECTED_CHAT_IDS.
 */
class FolderChatPickerActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_INITIAL_SELECTED = "initial_selected"
        const val RESULT_SELECTED_CHAT_IDS = "selected_chat_ids"
        private const val TAG = "FolderChatPicker"
    }

    private lateinit var binding: ActivityFolderChatPickerBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var adapter: PickerAdapter

    private var allChats: List<GrpcManager.ChatData> = emptyList()
    private val selectedIds = LinkedHashSet<String>()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityFolderChatPickerBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        globalParam = GlobalParam(this)
        grpcManager = app.grpcManager

        intent.getStringArrayListExtra(EXTRA_INITIAL_SELECTED)?.let { selectedIds.addAll(it) }

        binding.toolbar.setNavigationOnClickListener { finish() }
        adapter = PickerAdapter()
        binding.chatsRecyclerView.layoutManager = LinearLayoutManager(this)
        binding.chatsRecyclerView.adapter = adapter

        binding.doneButton.setOnClickListener {
            val data = Intent().putStringArrayListExtra(RESULT_SELECTED_CHAT_IDS, ArrayList(selectedIds))
            setResult(Activity.RESULT_OK, data)
            finish()
        }

        loadChats()
        updateToolbarTitle()
    }

    private fun updateToolbarTitle() {
        val n = selectedIds.size
        binding.toolbar.title = if (n == 0) "Выбрать чаты" else "$n выбрано"
    }

    private fun loadChats() {
        binding.loadingIndicator.visibility = View.VISIBLE
        lifecycleScope.launch {
            val result = grpcManager.getChats()
            binding.loadingIndicator.visibility = View.GONE
            if (result.isFailure) {
                Toast.makeText(this@FolderChatPickerActivity, "Ошибка загрузки чатов", Toast.LENGTH_SHORT).show()
                return@launch
            }
            allChats = result.getOrNull() ?: emptyList()
            adapter.notifyDataSetChanged()
        }
    }

    private inner class PickerAdapter : RecyclerView.Adapter<PickerAdapter.VH>() {

        override fun getItemCount() = allChats.size

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
            val b = ItemPickerChatBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            return VH(b)
        }

        override fun onBindViewHolder(holder: VH, position: Int) {
            holder.bind(allChats[position])
        }

        inner class VH(val binding: ItemPickerChatBinding) : RecyclerView.ViewHolder(binding.root) {
            fun bind(chat: GrpcManager.ChatData) {
                val title = if (chat.title.isNotBlank()) chat.title else "Чат"
                binding.chatTitle.text = title

                val avatarFileId = chat.picturePreviewFileId.ifBlank { chat.pictureFileId }
                AvatarLoader.showPlaceholder(binding.chatAvatarPlaceholder, title, chat.id.hashCode().toLong())
                binding.chatAvatar.visibility = View.GONE
                if (avatarFileId.isNotBlank()) {
                    AvatarLoader.loadByFileId(
                        imageView = binding.chatAvatar,
                        placeholderView = binding.chatAvatarPlaceholder,
                        fileId = avatarFileId,
                        displayName = title,
                        userId = chat.id.hashCode().toLong(),
                        size = 64
                    ) {
                        val r = grpcManager.getFileDownloadUrl(avatarFileId)
                        if (r.isSuccess) r.getOrNull() else null
                    }
                }

                val isSelected = chat.id in selectedIds
                binding.checkBox.isChecked = isSelected
                binding.root.setOnClickListener {
                    if (chat.id in selectedIds) selectedIds.remove(chat.id) else selectedIds.add(chat.id)
                    binding.checkBox.isChecked = chat.id in selectedIds
                    updateToolbarTitle()
                }
            }
        }
    }
}
