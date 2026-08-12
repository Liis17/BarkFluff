package com.barkfluff.client

import android.text.InputType
import android.util.Log
import android.view.View
import android.widget.LinearLayout
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import barkfluff.shared.Shared
import com.barkfluff.client.adapter.MessageAdapter
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.adapter.MessageType
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityChatBinding
import com.barkfluff.client.repository.PrivateChatRepository
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import kotlinx.coroutines.flow.filter
import kotlinx.coroutines.launch

/**
 * Контроллер приватного чата (E2E через passphrase). Рендерит в общий shell ChatActivity
 * (ActivityChatBinding + MessageAdapter). Порт логики бывшего PrivateChatActivity:
 * загрузка/расшифровка истории, отправка текста, realtime, машина состояний инвайта
 * (принять/отклонить/ожидание/отклонён) и passphrase-диалоги.
 */
class PrivateChatController(
    private val activity: AppCompatActivity,
    private val binding: ActivityChatBinding,
    private val adapter: MessageAdapter,
    private val app: BarkFluffApplication,
    private val globalParam: GlobalParam,
    private val chatId: String,
) {

    private val repo: PrivateChatRepository = app.privateChatRepository
    private val chatCacheRepository: ChatCacheRepository = app.chatCacheRepository
    private val cacheScope: CacheScope? = CacheScope.from(globalParam)

    companion object {
        private const val TAG = "PrivateChatController"
    }

    fun start(inviteState: Int, inviterUserId: Long) {
        binding.messageEditText.setHint(R.string.hint_encrypted_message)
        binding.sendButton.setOnClickListener { onSendClicked() }
        binding.e2eInviteAcceptButton.setOnClickListener { promptPassphraseAndAccept() }
        binding.e2eInviteDeclineButton.setOnClickListener { declineInvite() }
        resolveModeAndStart(inviteState, inviterUserId)
        observeRealtime()
    }

    /**
     * Определяет режим экрана по состоянию инвайта. Состояние берётся из extras, при входе
     * с push (state < 0) — из ListChats через getChat.
     */
    private fun resolveModeAndStart(stateNumber: Int, inviterUserId: Long) {
        if (stateNumber >= 0) {
            applyMode(stateNumber, inviterUserId)
            return
        }
        activity.lifecycleScope.launch {
            val chat = app.grpcManager.getChat(chatId).getOrNull()
            if (chat == null) {
                Toast.makeText(activity, R.string.chat_not_found, Toast.LENGTH_LONG).show()
                activity.finish()
                return@launch
            }
            if (chat.title.isNotBlank()) {
                binding.chatNameTextView.text = chat.title
                binding.chatAvatarPlaceholder.text = chat.title.trim().firstOrNull()?.uppercase() ?: "?"
            }
            applyMode(chat.privateInviteState.number, chat.privateInviterUserId)
        }
    }

    private fun applyMode(stateNumber: Int, inviterUserId: Long) {
        val isInvitee = inviterUserId != 0L && inviterUserId != globalParam.userId
        when (stateNumber) {
            Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_PENDING_VALUE ->
                if (isInvitee) showInviteRequest() else showWaitingMode()

            Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_REJECTED_VALUE ->
                showRejectedMode()

            else -> ensureUnlockedThenLoad()
        }
    }

    // ─── Приглашённый: запрос «принять/отклонить» ────────────────────────────

    private fun showInviteRequest() {
        setInputEnabled(false)
        binding.e2eInviteContainer.visibility = View.VISIBLE
        binding.e2eInvitePrompt.text =
            activity.getString(R.string.private_chat_invite_prompt, binding.chatNameTextView.text)
    }

    private fun promptPassphraseAndAccept() {
        val content = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            val margin = (24 * activity.resources.displayMetrics.density).toInt()
            setPadding(margin, 0, margin, 0)
        }
        val passwordLayout = TextInputLayout(activity, null, com.google.android.material.R.attr.textInputOutlinedStyle).apply {
            hint = activity.getString(R.string.private_chat_password_hint)
        }
        val edit = TextInputEditText(passwordLayout.context).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        passwordLayout.addView(edit)
        val remember = MaterialCheckBox(activity).apply {
            text = activity.getString(R.string.private_chat_remember_password)
            isChecked = false
        }
        content.addView(passwordLayout)
        content.addView(remember)
        MaterialAlertDialogBuilder(activity)
            .setTitle(R.string.passphrase_dialog_title)
            .setMessage(R.string.private_chat_accept_message)
            .setView(content)
            .setPositiveButton(R.string.btn_confirm) { _, _ ->
                val passphrase = edit.text?.toString()?.trim().orEmpty()
                if (passphrase.isEmpty()) return@setPositiveButton
                activity.lifecycleScope.launch {
                    val chat = app.grpcManager.getChat(chatId).getOrNull()
                    if (chat == null) {
                        Toast.makeText(activity, R.string.chat_not_found, Toast.LENGTH_LONG).show()
                        return@launch
                    }
                    repo.acceptPrivateChatInvite(
                        chatId,
                        passphrase,
                        chat.kdfSalt.toByteArray(),
                        chat.passphraseVerifier.toByteArray(),
                        remember.isChecked
                    ).onSuccess {
                        binding.e2eInviteContainer.visibility = View.GONE
                        setInputEnabled(true)
                        loadHistory()
                    }.onFailure {
                        Log.w(TAG, "Failed to accept private chat invite", it)
                        Toast.makeText(activity, R.string.invalid_passphrase, Toast.LENGTH_LONG).show()
                    }
                }
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }

    private fun declineInvite() {
        activity.lifecycleScope.launch {
            repo.rejectPrivateChat(chatId)
                .onSuccess { activity.finish() }
                .onFailure {
                    Toast.makeText(
                        activity,
                        activity.getString(R.string.private_chat_decline_failed, it.message.orEmpty()),
                        Toast.LENGTH_LONG
                    ).show()
                }
        }
    }

    // ─── Инициатор: ожидание подтверждения / отклонено ───────────────────────

    private fun showWaitingMode() {
        setInputEnabled(false)
        binding.e2eBanner.text = activity.getString(R.string.private_chat_invite_waiting)
        binding.e2eBanner.visibility = View.VISIBLE
        activity.lifecycleScope.launch {
            app.realtimeService.privateChatInviteResolutions
                .filter { it.chatId == chatId }
                .collect { event ->
                    if (event.accepted) {
                        binding.e2eBanner.visibility = View.GONE
                        setInputEnabled(true)
                        ensureUnlockedThenLoad()
                    } else {
                        showRejectedMode()
                    }
                }
        }
    }

    private fun showRejectedMode() {
        setInputEnabled(false)
        binding.e2eBanner.text = activity.getString(R.string.private_chat_invite_rejected)
        binding.e2eBanner.visibility = View.VISIBLE
    }

    private fun setInputEnabled(enabled: Boolean) {
        binding.messageEditText.isEnabled = enabled
        binding.sendButton.isEnabled = enabled
    }

    private fun ensureUnlockedThenLoad() {
        if (repo.hasKey(chatId)) {
            loadCachedHistory()
            loadHistory()
            return
        }
        promptPassphraseAndUnlock()
    }

    private fun promptPassphraseAndUnlock() {
        val content = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            val margin = (24 * activity.resources.displayMetrics.density).toInt()
            setPadding(margin, 0, margin, 0)
        }
        val passwordLayout = TextInputLayout(activity, null, com.google.android.material.R.attr.textInputOutlinedStyle).apply {
            hint = activity.getString(R.string.private_chat_password_hint)
        }
        val edit = TextInputEditText(passwordLayout.context).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        passwordLayout.addView(edit)
        val remember = MaterialCheckBox(activity).apply {
            text = activity.getString(R.string.private_chat_remember_password)
            isChecked = false
        }
        content.addView(passwordLayout)
        content.addView(remember)
        MaterialAlertDialogBuilder(activity)
            .setTitle(R.string.passphrase_dialog_title)
            .setMessage(R.string.private_chat_unlock_message)
            .setView(content)
            .setCancelable(false)
            .setPositiveButton(R.string.btn_confirm) { _, _ ->
                val passphrase = edit.text?.toString()?.trim().orEmpty()
                if (passphrase.isEmpty()) {
                    activity.finish()
                    return@setPositiveButton
                }
                activity.lifecycleScope.launch {
                    val chat = app.grpcManager.getChat(chatId).getOrNull()
                    if (chat == null) {
                        Toast.makeText(activity, R.string.chat_not_found, Toast.LENGTH_LONG).show()
                        activity.finish()
                        return@launch
                    }
                    val ok = repo.unlockExistingChat(chat, passphrase, remember.isChecked)
                    if (ok) {
                        loadCachedHistory()
                        loadHistory()
                    } else {
                        Toast.makeText(activity, R.string.invalid_passphrase, Toast.LENGTH_LONG).show()
                        activity.finish()
                    }
                }
            }
            .setNegativeButton(R.string.btn_cancel) { _, _ -> activity.finish() }
            .show()
    }

    private fun loadCachedHistory() {
        val scope = cacheScope ?: return
        activity.lifecycleScope.launch {
            val messages = runCatching {
                chatCacheRepository.latestPrivateMessages(scope, chatId, limit = 50)
            }.getOrNull().orEmpty()
                .mapNotNull { repo.decryptIncoming(chatId, it) }
            if (messages.isEmpty()) return@launch

            adapter.submitList(messages.map { it.toItem() }) {
                binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
            }
        }
    }

    private fun loadHistory() {
        activity.lifecycleScope.launch {
            val result = repo.listMessages(chatId, fromMessageId = 0, offsetBefore = 50, offsetAfter = 0)
            result.onSuccess { messages ->
                cacheScope?.let { scope ->
                    activity.lifecycleScope.launch {
                        runCatching { chatCacheRepository.savePrivateMessages(scope, chatId, messages.map { it.raw }) }
                    }
                }
                adapter.submitList(messages.map { it.toItem() }) {
                    binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
                }
                messages.maxOfOrNull { it.raw.id }?.let { repo.markMessagesRead(chatId, it) }
            }.onFailure {
                Log.w(TAG, "Failed to load private history", it)
                Toast.makeText(
                    activity,
                    activity.getString(R.string.private_chat_history_load_failed, it.message.orEmpty()),
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    private fun onSendClicked() {
        val text = binding.messageEditText.text?.toString()?.trim().orEmpty()
        if (text.isEmpty()) return
        binding.messageEditText.setText("")
        activity.lifecycleScope.launch {
            repo.sendText(chatId, text)
                .onSuccess { sent ->
                    cacheScope?.let { scope ->
                        activity.lifecycleScope.launch {
                            runCatching { chatCacheRepository.savePrivateMessages(scope, chatId, listOf(sent.raw)) }
                        }
                    }
                    val newList = adapter.currentList + sent.toItem()
                    adapter.submitList(newList) {
                        binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
                    }
                }
                .onFailure {
                    Toast.makeText(
                        activity,
                        activity.getString(R.string.private_chat_message_send_failed, it.message.orEmpty()),
                        Toast.LENGTH_LONG
                    ).show()
                }
        }
    }

    private fun observeRealtime() {
        activity.lifecycleScope.launch {
            app.realtimeService.privateMessages
                .filter { it.chatId == chatId }
                .collect { event ->
                    cacheScope?.let { scope ->
                        chatCacheRepository.savePrivateMessages(scope, chatId, listOf(event.message))
                    }
                    if (event.message.senderId == globalParam.userId) return@collect
                    val decrypted = repo.decryptIncoming(chatId, event.message) ?: return@collect
                    val newList = adapter.currentList + decrypted.toItem()
                    adapter.submitList(newList) {
                        binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
                    }
                    repo.markMessagesRead(chatId, event.message.id)
                }
        }
        activity.lifecycleScope.launch {
            app.realtimeService.privateMessageDeletes
                .filter { it.chatId == chatId }
                .collect { event ->
                    cacheScope?.let { scope ->
                        chatCacheRepository.deletePrivateMessage(scope, chatId, event.messageId)
                    }
                    val updated = adapter.currentList.filterNot {
                        it.type == MessageType.MESSAGE && it.messageId == event.messageId
                    }
                    adapter.submitList(updated)
                }
        }
    }

    private fun PrivateChatRepository.DecryptedPrivateMessage.toItem(): MessageItem {
        return MessageItem(
            messageId = raw.id,
            senderId = raw.senderId,
            text = plaintext ?: activity.getString(R.string.private_chat_decryption_failed),
            timestamp = raw.sentAt.seconds * 1000L,
            attachments = emptyList(),
            type = MessageType.MESSAGE
        )
    }
}
