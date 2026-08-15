package com.barkfluff.client

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.ItemTouchHelper
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.adapter.ChatFoldersAdapter
import com.barkfluff.client.databinding.ActivityChatFoldersSettingsBinding
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.launch

/**
 * Экран настроек папок чатов.
 * Показывает список папок текущего пользователя, FAB для создания, drag-drop для порядка.
 */
class ChatFoldersSettingsActivity : AppCompatActivity() {

    companion object {
        private const val TAG = "ChatFoldersSettings"
    }

    private lateinit var binding: ActivityChatFoldersSettingsBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var adapter: ChatFoldersAdapter

    private val editLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) loadFolders()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityChatFoldersSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager

        binding.toolbar.setNavigationOnClickListener {
            setResult(Activity.RESULT_OK)
            finish()
        }
        binding.createFolderFab.setOnClickListener {
            val intent = Intent(this, FolderEditActivity::class.java)
            editLauncher.launch(intent)
        }

        adapter = ChatFoldersAdapter { folder ->
            val intent = Intent(this, FolderEditActivity::class.java).apply {
                putExtra(FolderEditActivity.EXTRA_FOLDER_ID, folder.folderId)
                putExtra(FolderEditActivity.EXTRA_FOLDER_NAME, folder.folderName)
                putExtra(FolderEditActivity.EXTRA_FOLDER_ICON, folder.folderIcon)
                putStringArrayListExtra(FolderEditActivity.EXTRA_FOLDER_CHATS, ArrayList(folder.chatIds))
            }
            editLauncher.launch(intent)
        }
        binding.foldersRecyclerView.layoutManager = LinearLayoutManager(this)
        binding.foldersRecyclerView.adapter = adapter

        setupDragAndDrop()
        loadFolders()
    }

    override fun onResume() {
        super.onResume()
        loadFolders()
    }

    private fun setupDragAndDrop() {
        val callback = object : ItemTouchHelper.SimpleCallback(
            ItemTouchHelper.UP or ItemTouchHelper.DOWN, 0
        ) {
            override fun onMove(
                rv: RecyclerView,
                viewHolder: RecyclerView.ViewHolder,
                target: RecyclerView.ViewHolder
            ): Boolean {
                adapter.moveItem(viewHolder.bindingAdapterPosition, target.bindingAdapterPosition)
                return true
            }

            override fun onSwiped(viewHolder: RecyclerView.ViewHolder, direction: Int) {}

            override fun isLongPressDragEnabled(): Boolean = true

            override fun clearView(rv: RecyclerView, vh: RecyclerView.ViewHolder) {
                super.clearView(rv, vh)
                // По окончании drag — отправляем новый порядок на сервер
                val orders = adapter.currentList.mapIndexed { idx, folder -> folder.folderId to idx }
                lifecycleScope.launch {
                    val result = grpcManager.reorderChatFolders(orders)
                    if (result.isFailure) {
                        Toast.makeText(this@ChatFoldersSettingsActivity, R.string.chat_folders_reorder_error, Toast.LENGTH_SHORT).show()
                        loadFolders()
                    }
                }
            }
        }
        ItemTouchHelper(callback).attachToRecyclerView(binding.foldersRecyclerView)
    }

    private fun loadFolders() {
        lifecycleScope.launch {
            val result = grpcManager.getChatFolders()
            if (result.isSuccess) {
                val folders = result.getOrNull() ?: emptyList()
                adapter.submitList(folders)
                binding.emptyState.visibility = if (folders.isEmpty()) View.VISIBLE else View.GONE
            } else {
                Log.e(TAG, "Не удалось загрузить папки", result.exceptionOrNull())
                Toast.makeText(this@ChatFoldersSettingsActivity, R.string.chat_folders_load_error, Toast.LENGTH_SHORT).show()
            }
        }
    }

    override fun onBackPressed() {
        setResult(Activity.RESULT_OK)
        super.onBackPressed()
    }
}
