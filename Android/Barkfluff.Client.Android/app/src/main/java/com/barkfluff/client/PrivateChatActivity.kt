package com.barkfluff.client

import android.os.Bundle
import android.text.InputType
import android.util.Log
import android.view.View
import android.widget.Toast
import android.widget.LinearLayout
import androidx.appcompat.app.AppCompatActivity
import barkfluff.shared.Shared
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.EncryptedMessageAdapter
import com.barkfluff.client.adapter.EncryptedMessageItem
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.databinding.ActivityPrivateChatBinding
import com.barkfluff.client.repository.PrivateChatRepository
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
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
        const val EXTRA_INVITE_STATE = "invite_state"
        const val EXTRA_INVITER_USER_ID = "inviter_user_id"
        private const val TAG = "PrivateChatActivity"
    }

    private lateinit var binding: ActivityPrivateChatBinding
    private lateinit var globalParam: GlobalParam
    private lateinit var repo: PrivateChatRepository
    private lateinit var chatCacheRepository: ChatCacheRepository
    private var cacheScope: CacheScope? = null
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

        chatCacheRepository = app.chatCacheRepository
        cacheScope = CacheScope.from(globalParam)
        adapter = EncryptedMessageAdapter()
        binding.messagesRecyclerView.layoutManager = LinearLayoutManager(this).apply { stackFromEnd = true }
        binding.messagesRecyclerView.adapter = adapter

        binding.sendButton.setOnClickListener { onSendClicked() }

        resolveModeAndStart(app)

        observeRealtime(app)
    }

    /**
     * Определяет режим экрана по состоянию инвайта: обычный чат, запрос
     * «принять/отклонить» (приглашённый), ожидание подтверждения (инициатор)
     * или «запрос отклонён». Состояние берётся из extras, при входе с push —
     * из ListChats.
     */
    private fun resolveModeAndStart(app: BarkFluffApplication) {
        val stateNumber = intent.getIntExtra(EXTRA_INVITE_STATE, -1)
        val inviterUserId = intent.getLongExtra(EXTRA_INVITER_USER_ID, 0L)
        if (stateNumber >= 0) {
            applyMode(app, stateNumber, inviterUserId)
            return
        }
        lifecycleScope.launch {
            val chat = app.grpcManager.getChat(chatId).getOrNull()
            if (chat == null) {
                Toast.makeText(this@PrivateChatActivity, "Чат не найден", Toast.LENGTH_LONG).show()
                finish()
                return@launch
            }
            if (chat.title.isNotBlank()) binding.toolbar.title = chat.title
            applyMode(app, chat.privateInviteState.number, chat.privateInviterUserId)
        }
    }

    private fun applyMode(app: BarkFluffApplication, stateNumber: Int, inviterUserId: Long) {
        val isInvitee = inviterUserId != 0L && inviterUserId != globalParam.userId
        when (stateNumber) {
            Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_PENDING_VALUE ->
                if (isInvitee) showInviteRequest() else showWaitingMode(app)

            Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_REJECTED_VALUE ->
                showRejectedMode()

            else -> ensureUnlockedThenLoad()
        }
    }

    // ─── Приглашённый: запрос «принять/отклонить» ────────────────────────────

    private fun showInviteRequest() {
        setInputEnabled(false)
        binding.inviteRequestContainer.visibility = View.VISIBLE
        binding.invitePromptText.text = getString(R.string.private_chat_invite_prompt, binding.toolbar.title)
        binding.inviteAcceptButton.setOnClickListener { promptPassphraseAndAccept() }
        binding.inviteDeclineButton.setOnClickListener { declineInvite() }
    }

    private fun promptPassphraseAndAccept() {
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val margin = (24 * resources.displayMetrics.density).toInt()
            setPadding(margin, 0, margin, 0)
        }
        val passwordLayout = TextInputLayout(this, null, com.google.android.material.R.attr.textInputOutlinedStyle).apply {
            hint = getString(R.string.private_chat_password_hint)
        }
        val edit = TextInputEditText(passwordLayout.context).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        passwordLayout.addView(edit)
        val remember = MaterialCheckBox(this).apply {
            text = getString(R.string.private_chat_remember_password)
            isChecked = false
        }
        content.addView(passwordLayout)
        content.addView(remember)
        MaterialAlertDialogBuilder(this)
            .setTitle("Введите passphrase")
            .setMessage("Этот чат зашифрован общим паролем. Введите passphrase, чтобы принять запрос.")
            .setView(content)
            .setPositiveButton("OK") { _, _ ->
                val passphrase = edit.text?.toString()?.trim().orEmpty()
                if (passphrase.isEmpty()) return@setPositiveButton
                lifecycleScope.launch {
                    val app = applicationContext as BarkFluffApplication
                    val chat = app.grpcManager.getChat(chatId).getOrNull()
                    if (chat == null) {
                        Toast.makeText(this@PrivateChatActivity, "Чат не найден", Toast.LENGTH_LONG).show()
                        return@launch
                    }
                    repo.acceptPrivateChatInvite(
                        chatId,
                        passphrase,
                        chat.kdfSalt.toByteArray(),
                        chat.passphraseVerifier.toByteArray(),
                        remember.isChecked
                    ).onSuccess {
                        binding.inviteRequestContainer.visibility = View.GONE
                        setInputEnabled(true)
                        loadHistory()
                    }.onFailure {
                        Log.w(TAG, "Failed to accept private chat invite", it)
                        Toast.makeText(this@PrivateChatActivity, "Неверный passphrase", Toast.LENGTH_LONG).show()
                    }
                }
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private fun declineInvite() {
        lifecycleScope.launch {
            repo.rejectPrivateChat(chatId)
                .onSuccess { finish() }
                .onFailure {
                    Toast.makeText(this@PrivateChatActivity, "Не удалось отклонить: ${it.message}", Toast.LENGTH_LONG).show()
                }
        }
    }

    // ─── Инициатор: ожидание подтверждения / отклонено ───────────────────────

    private fun showWaitingMode(app: BarkFluffApplication) {
        setInputEnabled(false)
        binding.pendingBanner.text = getString(R.string.private_chat_invite_waiting)
        binding.pendingBanner.visibility = View.VISIBLE
        lifecycleScope.launch {
            app.realtimeService.privateChatInviteResolutions
                .filter { it.chatId == chatId }
                .collect { event ->
                    if (event.accepted) {
                        binding.pendingBanner.visibility = View.GONE
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
        binding.pendingBanner.text = getString(R.string.private_chat_invite_rejected)
        binding.pendingBanner.visibility = View.VISIBLE
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
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val margin = (24 * resources.displayMetrics.density).toInt()
            setPadding(margin, 0, margin, 0)
        }
        val passwordLayout = TextInputLayout(this, null, com.google.android.material.R.attr.textInputOutlinedStyle).apply {
            hint = getString(R.string.private_chat_password_hint)
        }
        val edit = TextInputEditText(passwordLayout.context).apply {
            inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
        }
        passwordLayout.addView(edit)
        val remember = MaterialCheckBox(this).apply {
            text = getString(R.string.private_chat_remember_password)
            isChecked = false
        }
        content.addView(passwordLayout)
        content.addView(remember)
        MaterialAlertDialogBuilder(this)
            .setTitle("Введите passphrase")
            .setMessage("Этот чат зашифрован общим паролем. Введите passphrase для расшифровки истории.")
            .setView(content)
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
                    val ok = repo.unlockExistingChat(chat, passphrase, remember.isChecked)
                    if (ok) {
                        loadCachedHistory()
                        loadHistory()
                    } else {
                        Toast.makeText(this@PrivateChatActivity, "Неверный passphrase", Toast.LENGTH_LONG).show()
                        finish()
                    }
                }
            }
            .setNegativeButton("Отмена") { _, _ -> finish() }
            .show()
    }

    private fun loadCachedHistory() {
        val scope = cacheScope ?: return
        lifecycleScope.launch {
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
        lifecycleScope.launch {
            val result = repo.listMessages(chatId, fromMessageId = 0, offsetBefore = 50, offsetAfter = 0)
            result.onSuccess { messages ->
                cacheScope?.let { scope ->
                    lifecycleScope.launch {
                        runCatching { chatCacheRepository.savePrivateMessages(scope, chatId, messages.map { it.raw }) }
                    }
                }
                adapter.submitList(messages.map { it.toItem() })
                binding.messagesRecyclerView.scrollToPosition(adapter.itemCount.coerceAtLeast(1) - 1)
                messages.maxOfOrNull { it.raw.id }?.let { repo.markMessagesRead(chatId, it) }
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
                    cacheScope?.let { scope ->
                        lifecycleScope.launch {
                            runCatching { chatCacheRepository.savePrivateMessages(scope, chatId, listOf(sent.raw)) }
                        }
                    }
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
                    cacheScope?.let { scope ->
                        chatCacheRepository.savePrivateMessages(scope, chatId, listOf(event.message))
                    }
                    if (event.message.senderId == globalParam.userId) return@collect
                    val decrypted = repo.decryptIncoming(chatId, event.message) ?: return@collect
                    val newList = adapter.currentList + decrypted.toItem()
                    adapter.submitList(newList) {
                        binding.messagesRecyclerView.scrollToPosition(newList.lastIndex)
                    }
                    repo.markMessagesRead(chatId, event.message.id)
                }
        }
        lifecycleScope.launch {
            app.realtimeService.privateMessageDeletes
                .filter { it.chatId == chatId }
                .collect { event ->
                    cacheScope?.let { scope ->
                        chatCacheRepository.deletePrivateMessage(scope, chatId, event.messageId)
                    }
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
