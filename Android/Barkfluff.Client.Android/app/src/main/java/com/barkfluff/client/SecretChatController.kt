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
import com.barkfluff.client.crypto.E2EBootstrap
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.repository.SecretChatRepository
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

sealed interface SecretSendOutcome {
    data object Sent : SecretSendOutcome
    data class SendFailed(val error: Throwable) : SecretSendOutcome
    data class HistoryFailed(val error: Throwable) : SecretSendOutcome
}

/** A definite server send is never reported as a send failure because local history failed. */
internal suspend fun sendThenRefresh(send: suspend () -> Result<*>, refresh: suspend () -> Unit): SecretSendOutcome {
    send().exceptionOrNull()?.let { return SecretSendOutcome.SendFailed(it) }
    return try {
        refresh()
        SecretSendOutcome.Sent
    } catch (e: CancellationException) {
        throw e
    } catch (e: Exception) {
        SecretSendOutcome.HistoryFailed(e)
    }
}

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
    private val repo: SecretChatRepository,
    private val chatCacheRepository: ChatCacheRepository,
    private val realtimeService: RealtimeService,
    private val globalParam: GlobalParam,
    private val chat: SecretChatRepository.SecretChat,
) {

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

    private val historyMutex = Mutex()

    fun start() {
        binding.messageEditText.setHint(R.string.hint_secret_message)
        binding.sendButton.setOnClickListener { onSendClicked() }
        loadCachedHistory()

        observeRealtime()
    }

    private fun loadCachedHistory() {
        activity.lifecycleScope.launch {
            try {
                refreshHistory()
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.w(TAG, "Could not recover secret history", e)
            }
        }
    }

    private suspend fun refreshHistory() = historyMutex.withLock {
        val scope = checkNotNull(cacheScope) { "Secret history requires an account scope" }
        val attemptedAcks = repo.persistPendingHistory(chat.id) { message ->
            chatCacheRepository.saveSecretMessage(scope, chat.id, message.messageId,
                if (message.senderUserId == globalParam.userId) SELF_LABEL else "ID ${message.senderUserId}",
                message.plaintext, message.sentAtMillis)
        }
        val messages = chatCacheRepository.latestSecretMessages(scope, chat.id, limit = 50)
        val items = messages.map {
            MessageItem(
                messageId = longIdFor(it.messageId),
                senderId = if (it.senderLabel == SELF_LABEL) globalParam.userId else chat.peerUserId,
                text = it.plaintext, timestamp = it.sentAtMillis,
                attachments = emptyList(), type = MessageType.MESSAGE,
            )
        }
        adapter.submitList(items) {
            binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
        }
        attemptedAcks
    }

    private fun onSendClicked() {
        val text = binding.messageEditText.text?.toString()?.trim().orEmpty()
        if (text.isEmpty()) return
        binding.messageEditText.setText("")
        activity.lifecycleScope.launch {
            val outcome = sendThenRefresh(send = { repo.sendMessage(chat, text) }, refresh = { refreshHistory() })
            when (outcome) {
                is SecretSendOutcome.Sent -> Unit
                is SecretSendOutcome.SendFailed -> {
                    // Nothing reached the server: give the text back for an explicit resend.
                    if (binding.messageEditText.text.isNullOrEmpty()) {
                        binding.messageEditText.setText(text)
                        binding.messageEditText.setSelection(text.length)
                    }
                    Toast.makeText(activity,
                        activity.getString(R.string.private_chat_message_send_failed, outcome.error.message.orEmpty()),
                        Toast.LENGTH_LONG).show()
                }
                // The server accepted it; the pending handoff is recovered on the next refresh.
                is SecretSendOutcome.HistoryFailed -> {
                    Log.w(TAG, "Sent secret message is not in local history yet", outcome.error)
                    Toast.makeText(activity, R.string.secret_message_history_failed, Toast.LENGTH_LONG).show()
                }
            }
        }
    }

    private fun observeRealtime() {
        activity.lifecycleScope.launch {
            realtimeService.secretMessages
                .filter {
                    it.envelope.senderUserId == chat.peerUserId &&
                        it.envelope.senderDeviceId == chat.peerDeviceId
                }
                .collect { event ->
                    repo.decryptIncoming(event.envelope)
                        .onSuccess { decrypted ->
                            try {
                                val attemptedAcks = refreshHistory()
                                if (decrypted.messageId !in attemptedAcks) repo.ack(decrypted.messageId)
                                if (decrypted.isPrekeyMessage) {
                                    activity.lifecycleScope.launch(Dispatchers.IO) {
                                        E2EBootstrap.ensurePrekeyBundleRegistered(activity.applicationContext)
                                    }
                                }
                            } catch (e: CancellationException) {
                                throw e
                            } catch (e: Exception) {
                                Log.w(TAG, "Could not persist incoming secret message", e)
                            }
                        }
                        .onFailure {
                            Log.w(TAG, "Failed to decrypt incoming secret envelope", it)
                        }
                }
        }
    }
}
