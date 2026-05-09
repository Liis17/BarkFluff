package com.barkfluff.client

import android.os.Bundle
import android.util.Log
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.EncryptedMessageAdapter
import com.barkfluff.client.adapter.EncryptedMessageItem
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivitySecretChatBinding
import com.barkfluff.client.repository.SecretChatRepository
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.launch

/**
 * Минимальный экран секретного чата (E2E через Signal Double Ratchet).
 *
 * История не загружается с сервера (сервер её не хранит) — только сообщения,
 * полученные в realtime, и только пока активна сессия. Сохранение локально
 * не реализовано в первой итерации — это задача для отдельной фичи.
 */
class SecretChatActivity : AppCompatActivity() {

    companion object {
        const val EXTRA_SECRET_CHAT_ID = "secret_chat_id"
        const val EXTRA_INITIAL_MESSAGE = "initial_message"
        private const val TAG = "SecretChatActivity"
    }

    private lateinit var binding: ActivitySecretChatBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var repo: SecretChatRepository
    private lateinit var adapter: EncryptedMessageAdapter

    private lateinit var chat: SecretChatRepository.SecretChat

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivitySecretChatBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val secretChatId = intent.getStringExtra(EXTRA_SECRET_CHAT_ID) ?: run { finish(); return }
        globalParam = GlobalParam(this)
        val app = applicationContext as BarkFluffApplication
        repo = app.secretChatRepository
        chat = repo.getChat(secretChatId) ?: run {
            Toast.makeText(this, "Секретный чат не найден", Toast.LENGTH_LONG).show()
            finish()
            return
        }

        binding.toolbar.title = "Секретный чат · ${chat.peerUserId}"
        binding.toolbar.setNavigationOnClickListener { finish() }
        binding.toolbar.setNavigationIcon(android.R.drawable.ic_menu_close_clear_cancel)

        adapter = EncryptedMessageAdapter()
        binding.messagesRecyclerView.layoutManager = LinearLayoutManager(this).apply { stackFromEnd = true }
        binding.messagesRecyclerView.adapter = adapter

        // Если чат только что создан — показать первое сообщение в UI
        intent.getStringExtra(EXTRA_INITIAL_MESSAGE)?.takeIf { it.isNotBlank() }?.let { firstMsg ->
            val item = EncryptedMessageItem(
                id = "init:${System.currentTimeMillis()}",
                senderLabel = "Вы",
                plaintext = firstMsg,
                sentAtMillis = System.currentTimeMillis()
            )
            adapter.submitList(listOf(item))
        }

        binding.sendButton.setOnClickListener { onSendClicked() }
        observeRealtime(app)
    }

    private fun onSendClicked() {
        val text = binding.messageEditText.text?.toString()?.trim().orEmpty()
        if (text.isEmpty()) return
        binding.messageEditText.setText("")
        lifecycleScope.launch {
            repo.sendMessage(chat, text)
                .onSuccess {
                    val item = EncryptedMessageItem(
                        id = "out:${it.messageId}",
                        senderLabel = "Вы",
                        plaintext = text,
                        sentAtMillis = System.currentTimeMillis()
                    )
                    val list = adapter.currentList + item
                    adapter.submitList(list) { binding.messagesRecyclerView.scrollToPosition(list.lastIndex) }
                }
                .onFailure {
                    Toast.makeText(this@SecretChatActivity, "Не удалось отправить: ${it.message}", Toast.LENGTH_LONG).show()
                }
        }
    }

    private fun observeRealtime(app: BarkFluffApplication) {
        lifecycleScope.launch {
            app.realtimeService.secretMessages
                .filter {
                    it.envelope.senderUserId == chat.peerUserId &&
                        it.envelope.senderDeviceId == chat.peerDeviceId
                }
                .collect { event ->
                    repo.decryptIncoming(event.envelope)
                        .onSuccess { decrypted ->
                            val item = EncryptedMessageItem(
                                id = "in:${decrypted.messageId}",
                                senderLabel = "ID ${decrypted.senderUserId}",
                                plaintext = decrypted.plaintext,
                                sentAtMillis = decrypted.sentAtSeconds * 1000L
                            )
                            val list = adapter.currentList + item
                            adapter.submitList(list) { binding.messagesRecyclerView.scrollToPosition(list.lastIndex) }
                            // Подтверждаем доставку, чтобы сервер удалил из Redis-буфера
                            repo.ack(decrypted.messageId)
                        }
                        .onFailure {
                            Log.w(TAG, "Failed to decrypt incoming secret envelope", it)
                        }
                }
        }
    }
}
