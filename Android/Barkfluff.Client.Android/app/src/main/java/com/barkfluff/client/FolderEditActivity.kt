package com.barkfluff.client

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.util.TypedValue
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.databinding.ActivityFolderEditBinding
import com.barkfluff.client.databinding.ItemFolderIconBinding
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.launch

/**
 * Создание/редактирование одной папки чатов.
 * Поля: имя, иконка (сетка из 20 эмодзи), список чатов (через FolderChatPickerActivity).
 */
class FolderEditActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_FOLDER_ID = "folder_id"        // null/missing = create
        const val EXTRA_FOLDER_NAME = "folder_name"
        const val EXTRA_FOLDER_ICON = "folder_icon"
        const val EXTRA_FOLDER_CHATS = "folder_chats"   // ArrayList<String>

        private val DEFAULT_ICONS = listOf(
            "📥", "⭐", "💼", "👥", "🎮", "✈️", "📚", "🎵",
            "💬", "❤️", "🔥", "🛒", "🏠", "☕", "🎂", "📷",
            "🎬", "🌐", "📰", "📌"
        )
    }

    private lateinit var binding: ActivityFolderEditBinding
    private lateinit var grpcManager: GrpcManager

    private var folderId: String? = null
    private var selectedIcon: String = ""
    private var selectedChatIds: MutableList<String> = mutableListOf()

    private val chatPickerLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            val ids = result.data?.getStringArrayListExtra(FolderChatPickerActivity.RESULT_SELECTED_CHAT_IDS) ?: arrayListOf()
            selectedChatIds = ids.toMutableList()
            updateChatsCount()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityFolderEditBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager

        folderId = intent.getStringExtra(EXTRA_FOLDER_ID)
        val initialName = intent.getStringExtra(EXTRA_FOLDER_NAME).orEmpty()
        selectedIcon = intent.getStringExtra(EXTRA_FOLDER_ICON).orEmpty()
        selectedChatIds = intent.getStringArrayListExtra(EXTRA_FOLDER_CHATS)?.toMutableList() ?: mutableListOf()

        binding.toolbar.title = if (folderId == null) "Новая папка" else "Редактирование папки"
        binding.toolbar.setNavigationOnClickListener { finish() }
        binding.deleteButton.visibility = if (folderId != null) View.VISIBLE else View.GONE

        binding.nameInput.setText(initialName)
        setupIconsGrid()
        updateChatsCount()

        binding.managechatsButton.setOnClickListener {
            val intent = Intent(this, FolderChatPickerActivity::class.java)
                .putStringArrayListExtra(
                    FolderChatPickerActivity.EXTRA_INITIAL_SELECTED,
                    ArrayList(selectedChatIds)
                )
            chatPickerLauncher.launch(intent)
        }

        binding.saveButton.setOnClickListener { onSaveClicked() }
        binding.deleteButton.setOnClickListener { confirmDelete() }
    }

    private fun setupIconsGrid() {
        binding.iconsRecyclerView.layoutManager = GridLayoutManager(this, 5)
        binding.iconsRecyclerView.adapter = IconsAdapter()
    }

    private fun updateChatsCount() {
        val n = selectedChatIds.size
        binding.chatsCount.text = when {
            n == 0 -> "Чаты не выбраны"
            n == 1 -> "1 чат"
            n in 2..4 -> "$n чата"
            else -> "$n чатов"
        }
    }

    private fun onSaveClicked() {
        val name = binding.nameInput.text?.toString()?.trim().orEmpty()
        if (name.isEmpty()) {
            binding.nameInput.error = "Введите название"
            return
        }
        if (name.length > 64) {
            binding.nameInput.error = "Не более 64 символов"
            return
        }

        binding.saveButton.isEnabled = false
        lifecycleScope.launch {
            val id = folderId
            val result = if (id == null) {
                // Создание + при необходимости — добавление чатов через UpdateChatFolder
                val createResult = grpcManager.createChatFolder(name, selectedIcon)
                if (createResult.isSuccess && selectedChatIds.isNotEmpty()) {
                    val created = createResult.getOrNull()!!
                    grpcManager.updateChatFolder(
                        folderId = created.folderId,
                        chatList = selectedChatIds
                    )
                } else {
                    createResult
                }
            } else {
                grpcManager.updateChatFolder(
                    folderId = id,
                    name = name,
                    icon = selectedIcon,
                    chatList = selectedChatIds
                )
            }
            binding.saveButton.isEnabled = true
            if (result.isSuccess) {
                setResult(Activity.RESULT_OK)
                finish()
            } else {
                Toast.makeText(this@FolderEditActivity, "Ошибка сохранения папки", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun confirmDelete() {
        val id = folderId ?: return
        AlertDialog.Builder(this)
            .setTitle("Удалить папку?")
            .setMessage("Чаты не будут удалены, только папка.")
            .setPositiveButton("Удалить") { _, _ ->
                lifecycleScope.launch {
                    val result = grpcManager.deleteChatFolder(id)
                    if (result.isSuccess) {
                        setResult(Activity.RESULT_OK)
                        finish()
                    } else {
                        Toast.makeText(this@FolderEditActivity, "Ошибка удаления", Toast.LENGTH_SHORT).show()
                    }
                }
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private inner class IconsAdapter : RecyclerView.Adapter<IconsAdapter.VH>() {
        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
            val binding = ItemFolderIconBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            return VH(binding)
        }

        override fun getItemCount() = DEFAULT_ICONS.size

        override fun onBindViewHolder(holder: VH, position: Int) {
            val icon = DEFAULT_ICONS[position]
            holder.binding.iconText.text = icon
            val isSelected = (icon == selectedIcon)
            val tv = TypedValue()
            theme.resolveAttribute(
                if (isSelected) com.google.android.material.R.attr.colorPrimaryContainer
                else com.google.android.material.R.attr.colorSurfaceContainer,
                tv, true
            )
            holder.binding.iconText.setBackgroundResource(R.drawable.bg_circle_btn)
            holder.binding.iconText.background?.setTint(tv.data)
            holder.binding.root.setOnClickListener {
                val previous = selectedIcon
                selectedIcon = if (icon == selectedIcon) "" else icon
                notifyItemChanged(DEFAULT_ICONS.indexOf(previous).coerceAtLeast(0))
                notifyItemChanged(position)
            }
        }

        inner class VH(val binding: ItemFolderIconBinding) : RecyclerView.ViewHolder(binding.root)
    }
}
