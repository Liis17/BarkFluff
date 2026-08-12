package com.barkfluff.client.crypto

import android.app.Activity
import android.text.InputType
import android.util.Log
import android.widget.Toast
import android.widget.LinearLayout
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.lifecycleScope
import barkfluff.updates.UpdatesApiOuterClass
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.ChatActivity
import com.barkfluff.client.R
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.checkbox.MaterialCheckBox
import com.google.android.material.textfield.TextInputEditText
import com.google.android.material.textfield.TextInputLayout
import kotlinx.coroutines.launch

/**
 * Слушатель входящих E2E-инвайтов и резолюций. Регистрируется из MainActivity (lifecycle-aware)
 * и показывает MaterialAlertDialog при получении событий из RealtimeService.
 *
 *  - PrivateChatInviteEvent → запрос passphrase, AcceptPrivateChat / RejectPrivateChat
 *  - PrivateChatInviteResolutionEvent → Toast («собеседник принял/отклонил»)
 *  - SecretChatInviteEvent → диалог Принять/Отклонить, при принятии локально создать SecretChat
 *  - SecretChatInviteResolutionEvent → пометить локальный SecretChat как accepted (или удалить)
 */
object EncryptedInviteHandler {

    private const val TAG = "EncryptedInvite"

    fun attach(activity: Activity, owner: LifecycleOwner) {
        val app = activity.applicationContext as BarkFluffApplication

        owner.lifecycleScope.launch {
            app.realtimeService.privateChatInvites.collect { invite ->
                showPrivateInviteDialog(activity, invite)
            }
        }
        owner.lifecycleScope.launch {
            app.realtimeService.privateChatInviteResolutions.collect { res ->
                val text = activity.getString(
                    if (res.accepted) R.string.private_invite_accepted_by_peer else R.string.private_invite_declined_by_peer
                )
                Toast.makeText(activity, text, Toast.LENGTH_SHORT).show()
            }
        }
        owner.lifecycleScope.launch {
            app.realtimeService.secretChatInvites.collect { invite ->
                showSecretInviteDialog(activity, invite)
            }
        }
        owner.lifecycleScope.launch {
            app.realtimeService.secretChatResolutions.collect { res ->
                if (res.accepted) {
                    app.secretChatRepository.markInitiatorChatAccepted(res.inviteId)
                    Toast.makeText(activity, R.string.secret_chat_confirmed, Toast.LENGTH_SHORT).show()
                } else {
                    val chat = app.secretChatRepository.findByInviteId(res.inviteId)
                    chat?.let { app.secretChatRepository.forgetChat(it.id) }
                    Toast.makeText(activity, R.string.secret_chat_declined_by_peer, Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    private fun showPrivateInviteDialog(activity: Activity, invite: UpdatesApiOuterClass.PrivateChatInviteEvent) {
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
            .setTitle(R.string.private_invite_title)
            .setMessage(activity.getString(R.string.private_invite_message, invite.inviterUserId))
            .setView(content)
            .setCancelable(false)
            .setPositiveButton(R.string.private_chat_invite_accept) { _, _ ->
                val passphrase = edit.text?.toString()?.trim().orEmpty()
                acceptPrivate(activity, invite, passphrase, remember.isChecked)
            }
            .setNegativeButton(R.string.private_chat_invite_decline) { _, _ ->
                rejectPrivate(activity, invite.chatId)
            }
            .show()
    }

    private fun acceptPrivate(
        activity: Activity,
        invite: UpdatesApiOuterClass.PrivateChatInviteEvent,
        passphrase: String,
        rememberKey: Boolean
    ) {
        if (passphrase.isEmpty()) return
        val app = activity.applicationContext as BarkFluffApplication
        (activity as? LifecycleOwner)?.lifecycleScope?.launch {
            val result = app.privateChatRepository.acceptPrivateChatInvite(
                chatId = invite.chatId,
                passphrase = passphrase,
                kdfSalt = invite.kdfSalt.toByteArray(),
                passphraseVerifier = invite.passphraseVerifier.toByteArray(),
                rememberKey = rememberKey
            )
            result.onSuccess { chat ->
                Toast.makeText(activity, R.string.private_chat_accepted, Toast.LENGTH_SHORT).show()
                activity.startActivity(ChatActivity.privateChatIntent(
                    activity,
                    chatId = chat.id,
                    title = chat.title.ifBlank { activity.getString(R.string.private_chat_title) }
                ))
            }.onFailure {
                Toast.makeText(
                    activity,
                    activity.getString(R.string.private_chat_accept_failed, it.message.orEmpty()),
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    private fun rejectPrivate(activity: Activity, chatId: String) {
        val app = activity.applicationContext as BarkFluffApplication
        (activity as? LifecycleOwner)?.lifecycleScope?.launch {
            app.privateChatRepository.rejectPrivateChat(chatId)
        }
    }

    private fun showSecretInviteDialog(activity: Activity, invite: UpdatesApiOuterClass.SecretChatInviteEvent) {
        MaterialAlertDialogBuilder(activity)
            .setTitle(R.string.secret_invite_title)
            .setMessage(
                activity.getString(
                    R.string.secret_invite_message,
                    invite.senderUserId,
                    invite.senderDeviceId.take(8)
                )
            )
            .setCancelable(false)
            .setPositiveButton(R.string.private_chat_invite_accept) { _, _ -> acceptSecret(activity, invite) }
            .setNegativeButton(R.string.private_chat_invite_decline) { _, _ -> rejectSecret(activity, invite.inviteId) }
            .show()
    }

    private fun acceptSecret(activity: Activity, invite: UpdatesApiOuterClass.SecretChatInviteEvent) {
        val app = activity.applicationContext as BarkFluffApplication
        (activity as? LifecycleOwner)?.lifecycleScope?.launch {
            val result = app.secretChatRepository.acceptIncomingInvite(
                inviteId = invite.inviteId,
                senderUserId = invite.senderUserId,
                senderDeviceId = invite.senderDeviceId,
                initialEnvelope = invite.initialEnvelope.toByteArray()
            )
            result.onSuccess { (chat, plaintext) ->
                Toast.makeText(activity, R.string.secret_chat_started, Toast.LENGTH_SHORT).show()
                activity.startActivity(ChatActivity.secretChatIntent(
                    activity,
                    secretChatId = chat.id,
                    initialMessage = plaintext
                ))
            }.onFailure {
                Log.w(TAG, "acceptSecret failed", it)
                Toast.makeText(
                    activity,
                    activity.getString(R.string.secret_chat_accept_failed, it.message.orEmpty()),
                    Toast.LENGTH_LONG
                ).show()
            }
        }
    }

    private fun rejectSecret(activity: Activity, inviteId: String) {
        val app = activity.applicationContext as BarkFluffApplication
        (activity as? LifecycleOwner)?.lifecycleScope?.launch {
            app.secretChatRepository.rejectIncomingInvite(inviteId)
        }
    }
}
