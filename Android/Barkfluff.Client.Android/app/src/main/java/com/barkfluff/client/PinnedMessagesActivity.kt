package com.barkfluff.client

import android.app.Activity
import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.MessageAdapter
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.MessageType
import com.barkfluff.client.adapter.ReadStatus
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityPinnedMessagesBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import kotlinx.coroutines.launch

/**
 * Полноэкранный список всех закреплённых сообщений в чате.
 * Возвращает в ChatActivity ID сообщения для скролла при тапе на сообщение.
 */
class PinnedMessagesActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_CHAT_ID = "chat_id"
        const val RESULT_SCROLL_TO_MESSAGE_ID = "scroll_to_message_id"
        private const val TAG = "PinnedMessagesActivity"
    }

    private lateinit var binding: ActivityPinnedMessagesBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private lateinit var realtimeService: RealtimeService
    private lateinit var adapter: MessageAdapter
    private var chatId: String = ""
    private var currentUserId: Long = 0L

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityPinnedMessagesBinding.inflate(layoutInflater)
        setContentView(binding.root)

        chatId = intent.getStringExtra(EXTRA_CHAT_ID).orEmpty()
        if (chatId.isEmpty()) {
            finish(); return
        }

        val app = application as BarkFluffApplication
        globalParam = GlobalParam(this)
        grpcManager = app.grpcManager
        realtimeService = app.realtimeService
        currentUserId = globalParam.userId

        setupToolbar()
        setupRecyclerView()
        subscribeToRealtimeEvents()
        loadPinned()
    }

    private fun setupToolbar() {
        binding.toolbar.setNavigationOnClickListener { finish() }
        binding.toolbar.setOnMenuItemClickListener { item ->
            when (item.itemId) {
                R.id.action_unpin_all -> {
                    confirmUnpinAll()
                    true
                }
                else -> false
            }
        }
    }

    private fun setupRecyclerView() {
        adapter = MessageAdapter(
            currentUserId = currentUserId,
            isGroupChat = true,
            getFileUrl = { fileId ->
                val r = grpcManager.getFileDownloadUrl(fileId)
                if (r.isSuccess) r.getOrNull() else null
            },
            onMessageActionRequested = { _, item -> showUnpinMenu(item) },
        )
        binding.pinnedRecyclerView.layoutManager = LinearLayoutManager(this)
        binding.pinnedRecyclerView.adapter = adapter
    }

    private fun subscribeToRealtimeEvents() {
        lifecycleScope.launch {
            realtimeService.messagePinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) loadPinned()
            }
        }
        lifecycleScope.launch {
            realtimeService.messageUnpinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) loadPinned()
            }
        }
        lifecycleScope.launch {
            realtimeService.allMessagesUnpinned.collect { event ->
                if (event.chatId.equals(chatId, ignoreCase = true)) {
                    setResult(Activity.RESULT_OK)
                    finish()
                }
            }
        }
    }

    private fun loadPinned() {
        lifecycleScope.launch {
            binding.loadingIndicator.visibility = View.VISIBLE
            binding.emptyState.visibility = View.GONE
            val result = grpcManager.listPinnedMessages(chatId)
            binding.loadingIndicator.visibility = View.GONE
            if (result.isFailure) {
                Toast.makeText(this@PinnedMessagesActivity, R.string.pinned_load_failed, Toast.LENGTH_SHORT).show()
                return@launch
            }
            val (list, _) = result.getOrNull() ?: return@launch
            if (list.isEmpty()) {
                binding.emptyState.visibility = View.VISIBLE
                adapter.submitList(emptyList())
                return@launch
            }
            val items = list.map { p ->
                val msg = p.message
                val isSystem = msg.type == barkfluff.shared.Shared.MessageContentType.SYSTEM
                MessageItem(
                    messageId = msg.id,
                    senderId = msg.senderId,
                    text = msg.content?.text ?: "",
                    timestamp = msg.sentAt.seconds * 1000,
                    attachments = msg.content?.attachmentsList ?: emptyList(),
                    readStatus = if (!isSystem && msg.senderId == currentUserId) ReadStatus.READ else ReadStatus.NONE,
                    type = if (isSystem) MessageType.SYSTEM else MessageType.MESSAGE,
                    isEdited = msg.isEdited
                )
            }
            adapter.submitList(items)
        }
    }

    private fun showUnpinMenu(item: MessageItem) {
        AlertDialog.Builder(this)
            .setItems(arrayOf(getString(R.string.pinned_go_to_message), getString(R.string.message_unpin))) { _, which ->
                when (which) {
                    0 -> {
                        val data = Intent().putExtra(RESULT_SCROLL_TO_MESSAGE_ID, item.messageId)
                        setResult(Activity.RESULT_OK, data)
                        finish()
                    }
                    1 -> unpin(item.messageId)
                }
            }
            .show()
    }

    private fun unpin(messageId: Long) {
        lifecycleScope.launch {
            val result = grpcManager.unpinMessage(chatId, messageId)
            if (result.isFailure) {
                Toast.makeText(this@PinnedMessagesActivity, R.string.message_unpin_failed, Toast.LENGTH_SHORT).show()
            } else {
                loadPinned()
            }
        }
    }

    private fun confirmUnpinAll() {
        AlertDialog.Builder(this)
            .setTitle(R.string.pinned_unpin_all_title)
            .setMessage(R.string.pinned_unpin_all_message)
            .setPositiveButton(R.string.message_unpin) { _, _ ->
                lifecycleScope.launch {
                    val result = grpcManager.unpinAllMessages(chatId)
                    if (result.isSuccess) {
                        Toast.makeText(
                            this@PinnedMessagesActivity,
                            getString(R.string.pinned_unpinned_count, result.getOrNull() ?: 0),
                            Toast.LENGTH_SHORT
                        ).show()
                        setResult(Activity.RESULT_OK)
                        finish()
                    } else {
                        Toast.makeText(this@PinnedMessagesActivity, R.string.pinned_unpin_all_failed, Toast.LENGTH_SHORT).show()
                    }
                }
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }
}
