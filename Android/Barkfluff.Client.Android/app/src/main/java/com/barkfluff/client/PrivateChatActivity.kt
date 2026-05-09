package com.barkfluff.client

import android.os.Bundle
import android.text.InputType
import android.util.Log
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.EncryptedMessageAdapter
import com.barkfluff.client.adapter.EncryptedMessageItem
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityPrivateChatBinding
import com.barkfluff.client.repository.PrivateChatRepository
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.launch

/**
 * Минимальный экран приватного чата (E2E через passphrase).
 *
 * Отвечает за: загрузку истории шифротекста (сразу расшифрованного), отправку текстовых
 * сообщений, реалтайм-обновления через RealtimeService.privateMessages.
 *
 * Если у клиента нет ключа в локальном кэше, показывает диалог запроса passphrase
 * и пробует разблокировать существующий чат через PrivateChatRepository.unlockExistingChat.
 */
class PrivateChatActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_CHAT_ID = "chat_id"
        const val EXTRA_TITLE = "chat_title"
        private const val TAG = "PrivateChatActivity"
    }

    private lateinit var binding: ActivityPrivateChatBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var repo: PrivateChatRepository
    private lateinit var adapter: EncryptedMessageAdapter

    private lateinit var chatId: String

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityPrivateChatBinding.inflate(layoutInflater)
        setContentView(binding.root)

        chatId = intent.getStringExtra(EXTRA_CHAT_ID) ?: run {
            finish(); return
        }
        val title = intent.getStringExtra(EXTRA_TITLE) ?: "Приватный чат"
        binding.toolbar.title = title
        binding.toolbar.setNavigationOnClickListener { finish() }
        binding.toolbar.setNavigationIcon(android.R.drawable.ic_menu_close_clear_cancel)

        globalParam = GlobalParam(this)
        val app = applicationContext as BarkFluffApplication
        repo = app.privateChatRepository

        adapter = EncryptedMessageAdapter()
        binding.messagesRecyclerView.layoutManager = LinearLayoutManager(this).apply { stackFromEnd = true }
        binding.messagesRecyclerView.adapter = adapter

        binding.sendButton.setOnClickListener { onSendClicked() }

        ensureUnlockedThenLoad()

        observeRealtime(app)
    }

    private fun ensureUnlockedThenLoad() {
        if (repo.hasKey(chatId)) {
            loadHistory()
            return
        }
        promptPassphraseAndUnlock()
    }

    private fun promptPassphraseAndUnlock() {
        val edit = TextInputEditText(this).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
            hint = "Passphrase"
        }
        MaterialAlertDialogBuilder(this)
            .setTitle("Введите passphrase")
            .setMessage("Этот чат зашифрован общим паролем. Введите passphrase для расшифровки истории.")
            .setView(edit)
            .setCancelable(false)
            .setPositiveButton("OK") { _, _ ->
                val passphrase = edit.text?.toString()?.trim().orEmpty()
                if (passphrase.isEmpty()) {
                    finish()
                    return@setPositiveButton
                }
                lifecycleScope.launch {
                    val app = applicationContext as BarkFluffApplication
                    val chat = app.grpcManager.getChat(chatId).getOrNull()
                    if (chat == null) {
                        Toast.makeText(this@PrivateChatActivity, "Чат не найден", Toast.LENGTH_LONG).show()
                        finish()
                        return@launch
                    }
                    val ok = repo.unlockExistingChat(chat, passphrase)
                    if (ok) loadHistory() else {
                        Toast.makeText(this@PrivateChatActivity, "Неверный passphrase", Toast.LENGTH_LONG).show()
                        finish()
                    }
                }
            }
            .setNegativeButton("Отмена") { _, _ -> finish() }
            .show()
    }

    private fun loadHistory() {
        lifecycleScope.launch {
            val result = repo.listMessages(chatId, fromMessageId = 0, offsetBefore = 50, offsetAfter = 0)
            result.onSuccess { messages ->
                adapter.submitList(messages.map { it.toItem() })
                binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
            }.onFailure {
                Log.w(TAG, "Failed to load private history", it)
                Toast.makeText(this@PrivateChatActivity, "Не удалось загрузить историю: ${it.message}", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun onSendClicked() {
        val text = binding.messageEditText.text?.toString()?.trim().orEmpty()
        if (text.isEmpty()) return
        binding.messageEditText.setText("")
        lifecycleScope.launch {
            repo.sendText(chatId, text)
                .onSuccess { sent ->
                    val newList = adapter.currentList + sent.toItem()
                    adapter.submitList(newList) {
                        binding.messagesRecyclerView.scrollToPosition(newList.lastIndex)
                    }
                }
                .onFailure {
                    Toast.makeText(this@PrivateChatActivity, "Не удалось отправить: ${it.message}", Toast.LENGTH_LONG).show()
                }
        }
    }

    private fun observeRealtime(app: BarkFluffApplication) {
        lifecycleScope.launch {
            app.realtimeService.privateMessages
                .filter { it.chatId == chatId }
                .collect { event ->
                    if (event.message.senderId == globalParam.userId) return@collect
                    val decrypted = repo.decryptIncoming(chatId, event.message) ?: return@collect
                    val newList = adapter.currentList + decrypted.toItem()
                    adapter.submitList(newList) {
                        binding.messagesRecyclerView.scrollToPosition(newList.lastIndex)
                    }
                }
        }
        lifecycleScope.launch {
            app.realtimeService.privateMessageDeletes
                .filter { it.chatId == chatId }
                .collect { event ->
                    val updated = adapter.currentList.filterNot { it.id == "p:${event.messageId}" }
                    adapter.submitList(updated)
                }
        }
    }

    private fun PrivateChatRepository.DecryptedPrivateMessage.toItem(): EncryptedMessageItem {
        val isSelf = raw.senderId == globalParam.userId
        return EncryptedMessageItem(
            id = "p:${raw.id}",
            senderLabel = if (isSelf) "Вы" else "ID ${raw.senderId}",
            plaintext = plaintext,
            sentAtMillis = raw.sentAt.seconds * 1000L
        )
    }
}
