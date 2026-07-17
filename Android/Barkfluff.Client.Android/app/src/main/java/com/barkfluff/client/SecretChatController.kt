package com.barkfluff.client

import android.util.Log
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.adapter.MessageAdapter
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.MessageType
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityChatBinding
import com.barkfluff.client.repository.SecretChatRepository
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.launch

/**
 * Контроллер секретного чата (E2E через Signal Double Ratchet). Рендерит в общий shell
 * ChatActivity (ActivityChatBinding + MessageAdapter). Порт логики бывшего SecretChatActivity.
 *
 * История не грузится с сервера (сервер её не хранит) — только локальный кэш + realtime
 * в рамках активной сессии.
 */
class SecretChatController(
    private val activity: AppCompatActivity,
    private val binding: ActivityChatBinding,
    private val adapter: MessageAdapter,
    private val app: BarkFluffApplication,
    private val globalParam: GlobalParam,
    private val chat: SecretChatRepository.SecretChat,
) {

    private val repo: SecretChatRepository = app.secretChatRepository
    private val chatCacheRepository: ChatCacheRepository = app.chatCacheRepository
    private val cacheScope: CacheScope? = CacheScope.from(globalParam)

    // Секретные сообщения идентифицируются строкой (envelope.messageId), а MessageItem требует
    // Long — держим стабильный маппинг в рамках сессии, чтобы DiffUtil корректно различал элементы.
    private val idMap = HashMap<String, Long>()
    private var idCounter = 0L

    private fun longIdFor(stringId: String): Long = idMap.getOrPut(stringId) { ++idCounter }

    companion object {
        private const val TAG = "SecretChatController"
        private const val SELF_LABEL = "Вы"
    }

    fun start(initialMessage: String?) {
        binding.messageEditText.setHint(R.string.hint_secret_message)
        binding.sendButton.setOnClickListener { onSendClicked() }
        loadCachedHistory()

        // Если чат только что создан — показать первое сообщение в UI
        initialMessage?.takeIf { it.isNotBlank() }?.let { firstMsg ->
            val sid = "init:${System.currentTimeMillis()}"
            val item = MessageItem(
                messageId = longIdFor(sid),
                senderId = globalParam.userId,
                text = firstMsg,
                timestamp = System.currentTimeMillis(),
                attachments = emptyList(),
                type = MessageType.MESSAGE
            )
            adapter.submitList(listOf(item))
        }

        observeRealtime()
    }

    private fun loadCachedHistory() {
        val scope = cacheScope ?: return
        activity.lifecycleScope.launch {
            val messages = runCatching {
                chatCacheRepository.latestSecretMessages(scope, chat.id, limit = 50)
            }.getOrNull().orEmpty()
            if (messages.isEmpty()) return@launch

            val items = messages.map {
                val isSelf = it.senderLabel == SELF_LABEL
                MessageItem(
                    messageId = longIdFor(it.messageId),
                    senderId = if (isSelf) globalParam.userId else chat.peerUserId,
                    text = it.plaintext,
                    timestamp = it.sentAtMillis,
                    attachments = emptyList(),
                    type = MessageType.MESSAGE
                )
            }
            adapter.submitList(items) {
                binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
            }
        }
    }

    private fun onSendClicked() {
        val text = binding.messageEditText.text?.toString()?.trim().orEmpty()
        if (text.isEmpty()) return
        binding.messageEditText.setText("")
        activity.lifecycleScope.launch {
            repo.sendMessage(chat, text)
                .onSuccess { sent ->
                    val sid = "out:${sent.messageId}"
                    val sentAt = System.currentTimeMillis()
                    val item = MessageItem(
                        messageId = longIdFor(sid),
                        senderId = globalParam.userId,
                        text = text,
                        timestamp = sentAt,
                        attachments = emptyList(),
                        type = MessageType.MESSAGE
                    )
                    cacheScope?.let { scope ->
                        activity.lifecycleScope.launch {
                            chatCacheRepository.saveSecretMessage(scope, chat.id, sid, SELF_LABEL, text, sentAt)
                        }
                    }
                    val list = adapter.currentList + item
                    adapter.submitList(list) {
                        binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
                    }
                }
                .onFailure {
                    Toast.makeText(activity, "Не удалось отправить: ${it.message}", Toast.LENGTH_LONG).show()
                }
        }
    }

    private fun observeRealtime() {
        activity.lifecycleScope.launch {
            app.realtimeService.secretMessages
                .filter {
                    it.envelope.senderUserId == chat.peerUserId &&
                        it.envelope.senderDeviceId == chat.peerDeviceId
                }
                .collect { event ->
                    repo.decryptIncoming(event.envelope)
                        .onSuccess { decrypted ->
                            val sid = "in:${decrypted.messageId}"
                            val sentAt = decrypted.sentAtSeconds * 1000L
                            val label = "ID ${decrypted.senderUserId}"
                            val item = MessageItem(
                                messageId = longIdFor(sid),
                                senderId = decrypted.senderUserId,
                                text = decrypted.plaintext,
                                timestamp = sentAt,
                                attachments = emptyList(),
                                type = MessageType.MESSAGE
                            )
                            cacheScope?.let { scope ->
                                activity.lifecycleScope.launch {
                                    chatCacheRepository.saveSecretMessage(
                                        scope, chat.id, sid, label, decrypted.plaintext, sentAt
                                    )
                                }
                            }
                            val list = adapter.currentList + item
                            adapter.submitList(list) {
                                binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
                            }
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
