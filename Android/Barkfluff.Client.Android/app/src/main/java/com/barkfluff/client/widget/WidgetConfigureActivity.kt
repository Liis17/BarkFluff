package com.barkfluff.client.widget

import android.app.Activity
import android.appwidget.AppWidgetManager
import android.content.Intent
import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.FolderChatPickerActivity
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ActivityWidgetConfigureBinding
import com.barkfluff.client.databinding.ItemWidgetSelectedChatBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import kotlinx.coroutines.launch

/**
 * Экран настройки одного App Widget'а.
 *
 * Срабатывает при добавлении виджета с рабочего стола (ACTION_APPWIDGET_CONFIGURE)
 * либо открывается из WidgetsSettingsActivity в edit-mode (EXTRA_EDIT_MODE=true).
 */
class WidgetConfigureActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_EDIT_MODE = "edit_mode"
        private const val REQ_PICK_CHATS = 7001
    }

    private lateinit var binding: ActivityWidgetConfigureBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var adapter: SelectedChatsAdapter

    private var appWidgetId: Int = AppWidgetManager.INVALID_APPWIDGET_ID
    private var editMode: Boolean = false

    private var allChats: List<GrpcManager.ChatData> = emptyList()
    private val selectedChatIds = ArrayList<String>()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // По умолчанию RESULT_CANCELED — Android удаляет widget если пользователь не сохранил
        setResult(Activity.RESULT_CANCELED)

        binding = ActivityWidgetConfigureBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager

        appWidgetId = intent.extras?.getInt(
            AppWidgetManager.EXTRA_APPWIDGET_ID,
            AppWidgetManager.INVALID_APPWIDGET_ID
        ) ?: AppWidgetManager.INVALID_APPWIDGET_ID
        editMode = intent.getBooleanExtra(EXTRA_EDIT_MODE, false)

        if (appWidgetId == AppWidgetManager.INVALID_APPWIDGET_ID) {
            Toast.makeText(this, R.string.widget_not_found, Toast.LENGTH_SHORT).show()
            finish()
            return
        }

        binding.toolbar.setNavigationOnClickListener { finish() }

        adapter = SelectedChatsAdapter()
        binding.selectedChatsRecyclerView.layoutManager = LinearLayoutManager(this)
        binding.selectedChatsRecyclerView.adapter = adapter

        // Загружаем существующий конфиг (edit mode + reconfigure)
        WidgetRepository.getConfig(this, appWidgetId)?.let { cfg ->
            binding.nameInput.setText(cfg.name)
            selectedChatIds.addAll(cfg.chatIds.take(WidgetConfig.MAX_CHATS))
        }
        if (binding.nameInput.text.isNullOrBlank()) {
            binding.nameInput.setText(getString(R.string.widget_default_name))
        }

        binding.pickChatsButton.setOnClickListener {
            val intent = Intent(this, FolderChatPickerActivity::class.java)
            intent.putStringArrayListExtra(
                FolderChatPickerActivity.EXTRA_INITIAL_SELECTED,
                ArrayList(selectedChatIds)
            )
            startActivityForResult(intent, REQ_PICK_CHATS)
        }

        binding.saveButton.setOnClickListener { onSaveClicked() }

        loadChats()
        renderSelected()
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == REQ_PICK_CHATS && resultCode == Activity.RESULT_OK) {
            val picked = data?.getStringArrayListExtra(FolderChatPickerActivity.RESULT_SELECTED_CHAT_IDS)
                ?: return
            if (picked.size > WidgetConfig.MAX_CHATS) {
                Toast.makeText(this, R.string.widget_max_chats_reached, Toast.LENGTH_SHORT).show()
            }
            selectedChatIds.clear()
            selectedChatIds.addAll(picked.take(WidgetConfig.MAX_CHATS))
            renderSelected()
        }
    }

    private fun loadChats() {
        lifecycleScope.launch {
            val result = grpcManager.getChats()
            if (result.isSuccess) {
                allChats = result.getOrNull() ?: emptyList()
                adapter.notifyDataSetChanged()
            }
        }
    }

    private fun renderSelected() {
        binding.chatsCount.text = getString(R.string.widget_chats_count, selectedChatIds.size)
        adapter.notifyDataSetChanged()
    }

    private fun onSaveClicked() {
        if (selectedChatIds.isEmpty()) {
            Toast.makeText(this, R.string.widget_no_chats_selected, Toast.LENGTH_SHORT).show()
            return
        }
        val name = binding.nameInput.text?.toString()?.trim().orEmpty()
            .ifBlank { getString(R.string.widget_default_name) }

        val config = WidgetConfig(name = name, chatIds = ArrayList(selectedChatIds))
        WidgetRepository.saveConfig(this, appWidgetId, config)

        lifecycleScope.launch {
            WidgetUpdater.refreshWidget(this@WidgetConfigureActivity, appWidgetId)

            if (!editMode) {
                val resultValue = Intent().putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId)
                setResult(Activity.RESULT_OK, resultValue)
            }
            finish()
        }
    }

    private inner class SelectedChatsAdapter : RecyclerView.Adapter<SelectedChatsAdapter.VH>() {

        override fun getItemCount(): Int = selectedChatIds.size

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
            val b = ItemWidgetSelectedChatBinding.inflate(
                LayoutInflater.from(parent.context), parent, false
            )
            return VH(b)
        }

        override fun onBindViewHolder(holder: VH, position: Int) {
            val chatId = selectedChatIds[position]
            val chat = allChats.firstOrNull { it.id == chatId }
            holder.bind(chatId, chat)
        }

        inner class VH(val b: ItemWidgetSelectedChatBinding) : RecyclerView.ViewHolder(b.root) {
            fun bind(chatId: String, chat: GrpcManager.ChatData?) {
                val title = chat?.title?.takeIf { it.isNotBlank() } ?: getString(R.string.chat_title_default)
                b.chatTitle.text = title

                AvatarLoader.showPlaceholder(b.chatAvatarPlaceholder, title, chatId.hashCode().toLong())
                b.chatAvatar.visibility = View.GONE

                val fileId = chat?.picturePreviewFileId?.ifBlank { chat.pictureFileId }.orEmpty()
                if (fileId.isNotBlank()) {
                    AvatarLoader.loadByFileId(
                        imageView = b.chatAvatar,
                        placeholderView = b.chatAvatarPlaceholder,
                        fileId = fileId,
                        displayName = title,
                        userId = chatId.hashCode().toLong(),
                        size = 64
                    ) {
                        grpcManager.getFileDownloadUrl(fileId).getOrNull()
                    }
                }

                b.removeButton.setOnClickListener {
                    val idx = selectedChatIds.indexOf(chatId)
                    if (idx >= 0) {
                        selectedChatIds.removeAt(idx)
                        renderSelected()
                    }
                }
            }
        }
    }
}
