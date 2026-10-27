package com.barkfluff.client.notifications

import android.app.NotificationChannel
import android.app.NotificationChannelGroup
import android.app.NotificationManager
import android.app.Notification
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.res.Configuration
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.media.AudioAttributes
import android.media.Ringtone
import android.media.RingtoneManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.telecom.DisconnectCause
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.app.Person
import androidx.core.app.RemoteInput
import androidx.core.content.pm.ShortcutInfoCompat
import androidx.core.content.pm.ShortcutManagerCompat
import androidx.core.graphics.drawable.IconCompat
import com.barkfluff.client.MainActivity
import com.barkfluff.client.R
import com.barkfluff.client.calls.CallActionReceiver
import com.barkfluff.client.calls.CallActivity
import com.barkfluff.client.calls.CallExtras
import com.barkfluff.client.calls.CallTelecomRegistry
import com.barkfluff.client.calls.IncomingCallActivity
import com.barkfluff.client.calls.IncomingCallPrefetch
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.utils.MarkdownRenderer
import java.util.Locale

object NotificationHelper {

    private const val TAG = "NotificationHelper"

    private const val DEDUP_MAX_SIZE = 100
    private const val DEDUP_PREFS = "message_notification_events"

    // Пул активных уведомлений — chatId'ы у которых сейчас висит уведомление в шторке
    val activeNotificationChats: MutableSet<String> =
        java.util.Collections.newSetFromMap(java.util.concurrent.ConcurrentHashMap())

    private var incomingCallRingtone: Ringtone? = null
    private var ringingCallId: String? = null
    private val recentMessagingHistory = LinkedHashMap<String, List<NotificationCompat.MessagingStyle.Message>>()
    private val recentMessagingHistoryGenerations = LinkedHashMap<String, Long>()
    private val recentRegularNotificationTemplates = LinkedHashMap<String, Notification>()

    // Группа
    private const val GROUP_ID = "barkfluff"
    private const val GROUP_NAME = "BarkFluff"

    // Канал: Личные сообщения
    const val CHANNEL_CHAT_MESSAGES = "chat_messages"

    // Канал: Входящие звонки (отдельный ID, чтобы старые silent-настройки calls не гасили heads-up)
    const val CHANNEL_INCOMING_CALLS = "incoming_calls_v2"

    // Канал: Звонки
    const val CHANNEL_CALLS = "calls"

    // Канал: Системные
    const val CHANNEL_SYSTEM = "system"

    // Канал: Прочее
    const val CHANNEL_OTHER = "other"

    const val EXTRA_CHAT_ID = "extra_chat_id"
    const val EXTRA_MESSAGE_ID = "extra_message_id"
    const val EXTRA_IS_PRIVATE_CHAT = "extra_is_private_chat"
    const val EXTRA_SCOPE_ID = "extra_notification_scope"
    const val EXTRA_KIND = "extra_notification_kind"
    const val EXTRA_EVENT_ID = "extra_notification_event"
    const val EXTRA_THREAD_ID = "extra_notification_thread"
    const val EXTRA_OPEN_CHAT_LIST = "extra_notification_open_chat_list"
    const val EXTRA_SERVER_MESSAGE_ID = "extra_notification_server_message_id"
    const val EXTRA_MESSAGING_HISTORY = "extra_notification_messaging_history"
    const val EXTRA_RECEIVED_CUTOFF = "extra_notification_received_cutoff"
    const val EXTRA_GROUP_CHAT = "extra_notification_group_chat"
    const val EXTRA_CHAT_TITLE = "extra_notification_chat_title"
    const val EXTRA_SENDER_ID = "extra_notification_sender_id"
    const val EXTRA_OPERATION_ID = "extra_notification_operation_id"
    const val EXTRA_REPLY_STATUS = "extra_notification_reply_status"
    const val EXTRA_REPLY_BODY = "extra_notification_reply_body"
    const val EXTRA_SENDER_NAME_FALLBACK = "extra_notification_sender_name_fallback"
    const val EXTRA_CHAT_TITLE_FALLBACK = "extra_notification_chat_title_fallback"
    const val EXTRA_CONTENT_COMPLETE = "extra_notification_content_complete"
    internal const val EXTRA_CARD_GENERATION = "extra_notification_card_generation"
    private const val EXTRA_MESSAGE_GENERATION = "extra_notification_message_generation"
    const val KIND_REGULAR = "regular"
    const val KIND_PRIVATE = "private"
    const val KIND_SECRET = "secret"
    const val ACTION_MARK_AS_READ = "com.barkfluff.client.ACTION_MARK_AS_READ"
    const val ACTION_REPLY = "com.barkfluff.client.ACTION_NOTIFICATION_REPLY"
    const val ACTION_HIDE = "com.barkfluff.client.ACTION_NOTIFICATION_HIDE"
    const val ACTION_DISMISSED = "com.barkfluff.client.ACTION_NOTIFICATION_DISMISSED"
    const val REPLY_INPUT_KEY = "notification_reply_text"

    // A receiver can start the process before an AppCompat Activity applies the saved locale.
    internal fun localizedContext(context: Context): Context {
        val language = GlobalParam(context).appLanguage
        if (language !in setOf(GlobalParam.LANGUAGE_RU, GlobalParam.LANGUAGE_EN,
                GlobalParam.LANGUAGE_DE, GlobalParam.LANGUAGE_ES, GlobalParam.LANGUAGE_ZH)) return context
        val configuration = Configuration(context.resources.configuration)
        configuration.setLocale(Locale.forLanguageTag(language))
        return context.createConfigurationContext(configuration)
    }

    fun createChannels(context: Context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val manager = context.getSystemService(NotificationManager::class.java)

            // Удаляем старый канал, если есть
            manager.deleteNotificationChannel("messages")

            // Группа
            manager.createNotificationChannelGroup(
                NotificationChannelGroup(GROUP_ID, GROUP_NAME)
            )

            val chatChannel = NotificationChannel(
                CHANNEL_CHAT_MESSAGES,
                context.getString(R.string.notification_channel_messages_name),
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = context.getString(R.string.notification_channel_messages_description)
                group = GROUP_ID
                enableVibration(true)
                setShowBadge(true)
            }

            val incomingCallsChannel = NotificationChannel(
                CHANNEL_INCOMING_CALLS,
                context.getString(R.string.notification_channel_incoming_calls_name),
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = context.getString(R.string.notification_channel_incoming_calls_description)
                group = GROUP_ID
                enableVibration(true)
                vibrationPattern = longArrayOf(0, 600, 600, 600)
                setShowBadge(false)
                lockscreenVisibility = android.app.Notification.VISIBILITY_PUBLIC
            }

            val callsChannel = NotificationChannel(
                CHANNEL_CALLS,
                context.getString(R.string.notification_channel_calls_name),
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = context.getString(R.string.notification_channel_calls_description)
                group = GROUP_ID
                enableVibration(true)
                setShowBadge(false)
                lockscreenVisibility = android.app.Notification.VISIBILITY_PUBLIC
            }

            val systemChannel = NotificationChannel(
                CHANNEL_SYSTEM,
                context.getString(R.string.notification_channel_system_name),
                NotificationManager.IMPORTANCE_DEFAULT
            ).apply {
                description = context.getString(R.string.notification_channel_system_description)
                group = GROUP_ID
                setShowBadge(false)
            }

            val otherChannel = NotificationChannel(
                CHANNEL_OTHER,
                context.getString(R.string.notification_channel_other_name),
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = context.getString(R.string.notification_channel_other_description)
                group = GROUP_ID
                setShowBadge(false)
            }

            manager.createNotificationChannels(listOf(chatChannel, incomingCallsChannel, callsChannel, systemChannel, otherChannel))
            Log.d(TAG, "Notification channels created")
        }
    }

    fun showMessageNotification(
        context: Context,
        senderName: String,
        messageText: String,
        avatarBitmap: Bitmap?,
        chatId: String,
        messageId: Long = 0,
        imageBitmap: Bitmap? = null,
        expectedScopeId: String? = CacheScope.from(GlobalParam(context))?.id
    ) {
        val scopeId = expectedScopeId ?: return
        showRegularMessageNotification(
            context,
            RegularNotificationInput(
                scopeId = scopeId,
                chatId = chatId,
                messageId = messageId,
                senderId = 0,
                senderName = senderName,
                chatTitle = senderName,
                isGroupChat = false,
                timestampMillis = System.currentTimeMillis(),
                text = messageText,
                attachmentKind = if (imageBitmap != null) "image" else "",
                contentIsComplete = true,
            ),
            senderAvatarBitmap = avatarBitmap,
            chatAvatarBitmap = avatarBitmap,
        )
    }

    @Synchronized
    fun showRegularMessageNotification(
        context: Context,
        input: RegularNotificationInput,
        senderAvatarBitmap: Bitmap? = null,
        chatAvatarBitmap: Bitmap? = null,
    ) {
        if (input.chatId.isBlank() || input.scopeId.isBlank() ||
            CacheScope.from(GlobalParam(context))?.id != input.scopeId) return
        val strings = localizedContext(context)
        val manager = context.getSystemService(NotificationManager::class.java)
        val tag = regularTag(input.scopeId, input.chatId)
        val identity = scopedChatKey(input.scopeId, input.chatId)
        val fallbackSenderKey = "${input.scopeId}:${input.chatId}:sender"
        val shortcutId = "conversation:$identity"
        val active = manager.activeNotifications.firstOrNull { it.tag == tag && it.id == REGULAR_NOTIFICATION_ID }
            ?: manager.activeNotifications.firstOrNull { isLegacyRegularNotification(it, input.scopeId, input.chatId) }
        val previous = active?.notification
        val previousMessages = recentMessagingHistory[identity]
            ?: previous?.let(::messagingHistoryFromNotification).orEmpty()
        val previousGeneration = recentMessagingHistoryGenerations[identity]
            ?: previous?.extras?.getLong(EXTRA_CARD_GENERATION)?.takeIf { it > 0 }
        val recent = readShownKeys(context)
        val messageId = input.messageId.coerceAtLeast(0)
        val messageKey = notificationMessageKey(input.scopeId, input.chatId, messageId)
        val alreadyInHistory = messageId > 0 && previousMessages.any { it.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) == messageId }
        if (active == null && messageId > 0 && messageKey in recent) return

        val senderName = input.senderName.ifBlank { strings.getString(R.string.notification_unknown_sender) }
        val senderBitmap = senderAvatarBitmap ?: createPlaceholderBitmap(senderName, input.senderId)
        val senderIcon = IconCompat.createWithBitmap(scaledBitmap(senderBitmap, 48))
        val sender = Person.Builder().setName(senderName)
            .setKey(input.senderId.takeIf { it > 0 }?.toString() ?: fallbackSenderKey)
            .setIcon(senderIcon).build()
        val displayText = formatNotificationText(strings, input)
        val incoming = NotificationCompat.MessagingStyle.Message(
            displayText,
            input.timestampMillis.takeIf { it > 0 } ?: System.currentTimeMillis(),
            sender,
        ).apply {
            extras.putLong(EXTRA_SERVER_MESSAGE_ID, messageId)
            extras.putLong(EXTRA_SENDER_ID, input.senderId)
            extras.putBoolean(EXTRA_SENDER_NAME_FALLBACK, input.senderNameIsFallback)
            extras.putBoolean(EXTRA_CONTENT_COMPLETE, input.contentIsComplete)
        }

        val cutoff = maxOf(
            previous?.extras?.getLong(EXTRA_RECEIVED_CUTOFF, 0) ?: 0,
            previousMessages.maxOfOrNull { it.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) } ?: 0,
            messageId,
        )
        val messages = previousMessages.toMutableList()
        val existingIndex = if (messageId > 0) messages.indexOfFirst {
            it.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) == messageId
        } else -1
        val appendIncoming = existingIndex < 0 &&
            (messageId <= 0 || (!alreadyInHistory && messageKey !in recent))
        val cardGeneration = if (appendIncoming) maxOf(previousGeneration ?: 0L, System.currentTimeMillis()) + 1L
            else previousGeneration ?: 1L
        if (appendIncoming) incoming.extras.putLong(EXTRA_MESSAGE_GENERATION, cardGeneration)
        if (existingIndex >= 0) {
            val old = messages[existingIndex]
            val oldPerson = old.person
            val oldName = oldPerson?.name?.toString().orEmpty()
            val oldNameIsFallback = old.extras.getBoolean(EXTRA_SENDER_NAME_FALLBACK) ||
                oldName.startsWith("Unknown #")
            val useIncomingName = oldName.isBlank() ||
                (oldNameIsFallback && !input.senderNameIsFallback)
            val mergedName = if (useIncomingName) senderName else oldName
            val oldContentIsComplete = old.extras.getBoolean(EXTRA_CONTENT_COMPLETE, true)
            val oldText = old.text?.toString().orEmpty()
            val replaceContent = (!oldContentIsComplete && input.contentIsComplete) ||
                (oldContentIsComplete == input.contentIsComplete && displayText.length > oldText.length)
            val oldIcon = oldPerson?.icon
            val enrichedPerson = Person.Builder()
                .setName(mergedName)
                .setKey(oldPerson?.key ?: sender.key)
                .apply {
                    val icon = senderAvatarBitmap?.let { IconCompat.createWithBitmap(scaledBitmap(it, 48)) } ?: oldIcon
                    icon?.let(::setIcon)
                }
                .build()
            val merged = NotificationCompat.MessagingStyle.Message(
                if (replaceContent) displayText else oldText,
                old.timestamp,
                enrichedPerson,
            ).also {
                it.extras.putAll(old.extras)
                it.extras.putLong(EXTRA_SERVER_MESSAGE_ID, messageId)
                it.extras.putLong(EXTRA_SENDER_ID, input.senderId)
                it.extras.putBoolean(EXTRA_SENDER_NAME_FALLBACK, !useIncomingName && oldNameIsFallback)
                it.extras.putBoolean(EXTRA_CONTENT_COMPLETE, if (replaceContent) input.contentIsComplete else oldContentIsComplete)
            }
            messages[existingIndex] = merged
        } else if (appendIncoming) {
            messages += incoming
        }
        val normalizedMessages = messages
            .sortedWith(compareBy<NotificationCompat.MessagingStyle.Message> { it.timestamp }
                .thenBy { it.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) })
            .takeLast(MAX_MESSAGING_HISTORY)
        val latestIncomingIndex = normalizedMessages.indices.lastOrNull { index ->
            normalizedMessages[index].extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) > 0
        }
        val savedMessages = normalizedMessages.mapIndexed { index, message ->
            copyMessage(message, preserveSenderIcon = index == latestIncomingIndex)
        }

        val previousTitleIsFallback = previous?.extras?.getBoolean(EXTRA_CHAT_TITLE_FALLBACK, false) ?: true
        val preservePreviousChatMetadata = previous != null && !previousTitleIsFallback && input.chatTitleIsFallback
        val isGroupChat = if (preservePreviousChatMetadata) {
            previous?.extras?.getBoolean(EXTRA_GROUP_CHAT, input.isGroupChat) ?: input.isGroupChat
        } else input.isGroupChat
        val chatTitle = if (preservePreviousChatMetadata) {
            previous?.extras?.getString(EXTRA_CHAT_TITLE).orEmpty()
        } else input.chatTitle.ifBlank {
            if (input.isGroupChat) strings.getString(R.string.notification_group_chat) else senderName
        }
        val chatBitmap = (chatAvatarBitmap ?: senderAvatarBitmap.takeUnless { isGroupChat })
            ?: previous?.largeIcon?.let { Bitmap.createScaledBitmap(it, 64, 64, true) }
            ?: createPlaceholderBitmap(chatTitle, input.chatId.hashCode().toLong())
        val chatIcon = IconCompat.createWithBitmap(scaledBitmap(chatBitmap, 64))
        val eventTarget = MessageNotificationTarget(input.scopeId, KIND_REGULAR, input.chatId, cutoff.toString())
        val contentIntent = Intent(context, MainActivity::class.java).apply {
            action = Intent.ACTION_VIEW
            putExtras(metadata(eventTarget, chatTitle, isGroupChat, cutoff))
            data = actionUri(eventTarget, Intent.ACTION_VIEW)
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val contentPendingIntent = PendingIntent.getActivity(
            context, identity.hashCode(), contentIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val shortcutPerson = Person.Builder().setName(chatTitle).setKey(identity).setIcon(chatIcon).build()
        val shortcut = ShortcutInfoCompat.Builder(context, shortcutId)
            .setShortLabel(chatTitle.take(40)).setLongLabel(chatTitle).setIcon(chatIcon)
            .setIntent(contentIntent).setLongLived(true).setPerson(shortcutPerson).build()
        runCatching { ShortcutManagerCompat.pushDynamicShortcut(context, shortcut) }
            .onFailure { Log.w(TAG, "Unable to update conversation shortcut", it) }

        val me = Person.Builder().setName(strings.getString(R.string.notification_me)).build()
        val style = NotificationCompat.MessagingStyle(me)
            .setGroupConversation(isGroupChat)
        if (isGroupChat) style.setConversationTitle(chatTitle)
        savedMessages.forEach(style::addMessage)
        val builder = NotificationCompat.Builder(strings, CHANNEL_CHAT_MESSAGES)
            .setSmallIcon(R.drawable.ic_notification)
            .setLargeIcon(scaledBitmap(chatBitmap, 64))
            .setColor(context.resources.getColor(R.color.primary, null))
            .setAutoCancel(true)
            .setOnlyAlertOnce(previous != null)
            .setShortcutId("conversation:$identity")
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_MESSAGE)
            .setContentTitle(chatTitle)
            .setContentText(savedMessages.lastOrNull()?.text ?: displayText)
            .setContentIntent(contentPendingIntent)
            .setDeleteIntent(dismissPendingIntent(context, eventTarget, cardGeneration))
            .setStyle(style)
            .addExtras(metadata(eventTarget, chatTitle, isGroupChat, cutoff).apply {
                putBoolean(EXTRA_CHAT_TITLE_FALLBACK, if (preservePreviousChatMetadata) false else input.chatTitleIsFallback)
                putLong(EXTRA_CARD_GENERATION, cardGeneration)
                putParcelableArrayList(EXTRA_MESSAGING_HISTORY, serializedMessagingHistory(savedMessages))
            })

        if (cutoff > 0) {
            val replyPendingIntent = PendingIntent.getBroadcast(
                context, identity.hashCode(), actionIntent(context, eventTarget, ACTION_REPLY),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE,
            )
            val remoteInput = RemoteInput.Builder(REPLY_INPUT_KEY)
                .setLabel(strings.getString(R.string.notification_reply_hint)).build()
            builder.addAction(NotificationCompat.Action.Builder(
                0, strings.getString(R.string.notification_reply), replyPendingIntent,
            ).addRemoteInput(remoteInput)
                .setSemanticAction(NotificationCompat.Action.SEMANTIC_ACTION_REPLY).build())
            val readPendingIntent = PendingIntent.getBroadcast(
                context, identity.hashCode(), actionIntent(context, eventTarget, ACTION_MARK_AS_READ),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
            builder.addAction(0, strings.getString(R.string.notification_mark_read), readPendingIntent)
        }

        try {
            if (active != null && isLegacyRegularNotification(active, input.scopeId, input.chatId)) {
                manager.cancel(active.tag, active.id)
            }
            val notification = builder.build()
            rememberMessagingHistory(identity, savedMessages)
            recentMessagingHistoryGenerations[identity] = cardGeneration
            rememberRegularNotificationTemplate(identity, notification)
            NotificationManagerCompat.from(context).notify(tag, REGULAR_NOTIFICATION_ID, notification)
            activeNotificationChats.add(identity)
            if (messageId > 0 && !alreadyInHistory) {
                recent.add(messageKey)
                while (recent.size > DEDUP_MAX_SIZE) recent.remove(recent.first())
                context.getSharedPreferences(DEDUP_PREFS, Context.MODE_PRIVATE).edit()
                    .putStringSet("shown", recent).apply()
            }
        } catch (e: SecurityException) {
            Log.w(TAG, "No notification permission", e)
        } catch (e: Exception) {
            Log.e(TAG, "Failed to show notification", e)
        }
    }


    fun showEncryptedMessageNotification(
        context: Context,
        kind: String,
        threadId: String,
        eventId: String,
        chatId: String = threadId,
        expectedScopeId: String? = CacheScope.from(GlobalParam(context))?.id
    ) {
        if (kind != KIND_PRIVATE && kind != KIND_SECRET) return
        val scopeId = expectedScopeId ?: return
        if (threadId.isBlank() || eventId.isBlank()) return
        val target = MessageNotificationTarget(scopeId, kind, chatId, eventId, threadId)
        val strings = localizedContext(context)
        val contentIntent = Intent(context, MainActivity::class.java).apply {
            action = Intent.ACTION_VIEW
            putExtra(EXTRA_OPEN_CHAT_LIST, true)
            putExtras(metadata(target))
            data = actionUri(target, Intent.ACTION_VIEW)
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val contentPendingIntent = PendingIntent.getActivity(
            context, 0, contentIntent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val hidePendingIntent = PendingIntent.getBroadcast(
            context, 0, actionIntent(context, target, ACTION_HIDE),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val builder = NotificationCompat.Builder(strings, CHANNEL_CHAT_MESSAGES)
            .setSmallIcon(R.drawable.ic_notification)
            .setColor(context.resources.getColor(R.color.primary, null))
            .setContentTitle(strings.getString(R.string.app_name))
            .setContentText(strings.getString(R.string.notification_encrypted_message))
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_MESSAGE)
            .setContentIntent(contentPendingIntent)
            .setAllowSystemGeneratedContextualActions(false)
            .addExtras(metadata(target))
            .addAction(NotificationCompat.Action.Builder(
                0, strings.getString(R.string.notification_hide), hidePendingIntent
            ).setAllowGeneratedReplies(false).build())
        try {
            postMessage(context, target, encryptedTag(target), 0, builder.build())
        } catch (e: SecurityException) {
            Log.w(TAG, "No notification permission")
        }
    }

    internal fun actionIntent(context: Context, target: MessageNotificationTarget, action: String) =
        Intent(context, if (action == ACTION_MARK_AS_READ) MarkAsReadReceiver::class.java
            else NotificationActionReceiver::class.java).apply {
            this.action = action
            data = actionUri(target, action)
            putExtras(metadata(target))
        }

    private fun actionUri(target: MessageNotificationTarget, action: String, generation: Long? = null) = Uri.Builder()
        .scheme("barkfluff").authority("message-notification")
        .appendPath(target.scopeId).appendPath(target.kind).appendPath(target.threadId)
        .appendPath(target.eventId).appendPath(action).apply {
            generation?.let { appendPath(it.toString()) }
        }.build()

    private fun dismissPendingIntent(context: Context, target: MessageNotificationTarget, generation: Long): PendingIntent {
        val intent = actionIntent(context, target, ACTION_DISMISSED).apply {
            putExtra(EXTRA_CARD_GENERATION, generation)
            data = actionUri(target, ACTION_DISMISSED, generation)
        }
        return PendingIntent.getBroadcast(
            context,
            scopedChatKey(target.scopeId, target.chatId).hashCode(),
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    private fun metadata(
        target: MessageNotificationTarget,
        chatTitle: String = "",
        isGroupChat: Boolean = false,
        receivedCutoff: Long = target.eventId.toLongOrNull() ?: 0,
    ) = Bundle().apply {
        putString(EXTRA_SCOPE_ID, target.scopeId)
        putString(EXTRA_KIND, target.kind)
        putString(EXTRA_CHAT_ID, target.chatId)
        putString(EXTRA_THREAD_ID, target.threadId)
        putString(EXTRA_EVENT_ID, target.eventId)
        putLong(EXTRA_MESSAGE_ID, receivedCutoff)
        putLong(EXTRA_RECEIVED_CUTOFF, receivedCutoff)
        if (chatTitle.isNotBlank()) putString(EXTRA_CHAT_TITLE, chatTitle)
        putBoolean(EXTRA_GROUP_CHAT, isGroupChat)
    }

    internal fun currentTarget(context: Context, intent: Intent): MessageNotificationTarget? {
        val scopeId = intent.getStringExtra(EXTRA_SCOPE_ID)?.takeIf(String::isNotBlank) ?: return null
        if (CacheScope.from(GlobalParam(context))?.id != scopeId) return null
        val kind = intent.getStringExtra(EXTRA_KIND) ?: return null
        if (kind !in setOf(KIND_REGULAR, KIND_PRIVATE, KIND_SECRET)) return null
        val chatId = intent.getStringExtra(EXTRA_CHAT_ID)?.takeIf(String::isNotBlank) ?: return null
        val eventId = intent.getStringExtra(EXTRA_EVENT_ID)?.takeIf(String::isNotBlank) ?: return null
        val threadId = intent.getStringExtra(EXTRA_THREAD_ID)?.takeIf(String::isNotBlank) ?: return null
        val target = MessageNotificationTarget(scopeId, kind, chatId, eventId, threadId)
        val action = intent.action ?: return null
        val generation = if (action == ACTION_DISMISSED) {
            intent.getLongExtra(EXTRA_CARD_GENERATION, 0).takeIf { it > 0 } ?: return null
        } else null
        if (intent.data != actionUri(target, action, generation)) return null
        return target
    }

    private fun encryptedTag(target: MessageNotificationTarget) =
        "encrypted:${target.scopeId}:${target.kind}:${target.threadId}"

    @Synchronized
    private fun postMessage(
        context: Context, target: MessageNotificationTarget, tag: String?, id: Int, notification: Notification
    ) {
        if (CacheScope.from(GlobalParam(context))?.id != target.scopeId) return
        val prefs = context.getSharedPreferences(DEDUP_PREFS, Context.MODE_PRIVATE)
        val key = Uri.Builder().appendPath(target.scopeId).appendPath(target.kind)
            .appendPath(target.chatId).appendPath(target.eventId).build().toString()
        val recent = LinkedHashSet(prefs.getStringSet("shown", emptySet()).orEmpty())
        val deduplicate = target.eventId != "0"
        val manager = context.getSystemService(NotificationManager::class.java)
        val alreadyActive = manager.activeNotifications.any { it.tag == tag && it.id == id }
        if (deduplicate && key in recent && !alreadyActive) return
        if (deduplicate && alreadyActive) return
        NotificationManagerCompat.from(context).notify(tag, id, notification)
        if (deduplicate) {
            recent.add(key)
            while (recent.size > DEDUP_MAX_SIZE) recent.remove(recent.first())
            prefs.edit().putStringSet("shown", recent).apply()
        }
    }

    @Synchronized
    internal fun addNotificationReply(
        context: Context,
        target: MessageNotificationTarget,
        operationId: String,
        body: String,
        status: String,
    ) {
        if (CacheScope.from(GlobalParam(context))?.id != target.scopeId) return
        val active = findRegularNotification(context, target.scopeId, target.chatId) ?: return
        val identity = scopedChatKey(target.scopeId, target.chatId)
        val messages = (recentMessagingHistory[identity] ?: messagingHistoryFromNotification(active.notification))
            .map(::copyMessage).toMutableList()
        val existing = messages.indexOfFirst { it.extras.getString(EXTRA_OPERATION_ID) == operationId }
        val source = if (existing >= 0) messages.removeAt(existing) else null
        val replyBody = source?.extras?.getString(EXTRA_REPLY_BODY) ?: body
        val timestamp = source?.timestamp ?: System.currentTimeMillis()
        val generation = recentMessagingHistoryGenerations[identity]
            ?: active.notification.extras.getLong(EXTRA_CARD_GENERATION, 1L).coerceAtLeast(1L)
        val reply = NotificationCompat.MessagingStyle.Message(
            "$replyBody · ${replyStatusText(context, status)}", timestamp, null as Person?,
        ).apply {
            extras.putString(EXTRA_OPERATION_ID, operationId)
            extras.putString(EXTRA_REPLY_BODY, replyBody)
            extras.putString(EXTRA_REPLY_STATUS, status)
            extras.putLong(EXTRA_MESSAGE_GENERATION, generation)
        }
        messages += reply
        messages.sortWith(compareBy<NotificationCompat.MessagingStyle.Message> { it.timestamp }
            .thenBy { it.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) })
        recentMessagingHistoryGenerations[identity] = generation
        repostRegularWithMessages(context, active, messages.takeLast(MAX_MESSAGING_HISTORY))
    }

    @Synchronized
    internal fun removeNotificationReply(
        context: Context,
        scopeId: String,
        chatId: String,
        operationId: String,
    ) {
        if (CacheScope.from(GlobalParam(context))?.id != scopeId) return
        val identity = scopedChatKey(scopeId, chatId)
        val active = findRegularNotification(context, scopeId, chatId)
        val messages = recentMessagingHistory[identity]
            ?: active?.let { messagingHistoryFromNotification(it.notification) }
            ?: return
        val remaining = messages.filterNot { it.extras.getString(EXTRA_OPERATION_ID) == operationId }
        if (remaining.size == messages.size) return
        if (remaining.isEmpty()) {
            recentMessagingHistory.remove(identity)
            recentMessagingHistoryGenerations.remove(identity)
            recentRegularNotificationTemplates.remove(identity)
            activeNotificationChats.remove(identity)
            active?.let { context.getSystemService(NotificationManager::class.java).cancel(it.tag, it.id) }
        } else {
            rememberMessagingHistory(identity, remaining)
            active?.let { repostRegularWithMessages(context, it, remaining) }
        }
    }

    @Synchronized
    internal fun updateNotificationReplyStatus(
        context: Context,
        scopeId: String,
        chatId: String,
        operationId: String,
        body: String,
        status: String,
    ) {
        val target = MessageNotificationTarget(scopeId, KIND_REGULAR, chatId, "0")
        addNotificationReply(context, target, operationId, body, status)
    }

    @Synchronized
    internal fun dismissReadMessages(context: Context, target: MessageNotificationTarget) {
        if (CacheScope.from(GlobalParam(context))?.id != target.scopeId || target.kind != KIND_REGULAR) return
        val active = findRegularNotification(context, target.scopeId, target.chatId) ?: return
        val identity = scopedChatKey(target.scopeId, target.chatId)
        val messages = recentMessagingHistory[identity] ?: messagingHistoryFromNotification(active.notification)
        val cutoff = target.eventId.toLongOrNull()?.takeIf { it > 0 } ?: return
        val remaining = messages.filter { message ->
            val id = message.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0)
            id <= 0 || id > cutoff
        }
        val manager = context.getSystemService(NotificationManager::class.java)
        if (remaining.isEmpty()) {
            manager.cancel(active.tag, active.id)
            recentMessagingHistory.remove(identity)
            recentMessagingHistoryGenerations.remove(identity)
            recentRegularNotificationTemplates.remove(identity)
            activeNotificationChats.remove(identity)
        } else {
            repostRegularWithMessages(context, active, remaining)
        }
    }

    @Synchronized
    internal fun dismissRegularNotificationHistory(
        context: Context,
        target: MessageNotificationTarget,
        dismissedGeneration: Long,
    ) {
        if (CacheScope.from(GlobalParam(context))?.id != target.scopeId || target.kind != KIND_REGULAR) return
        val cutoff = target.eventId.toLongOrNull()?.takeIf { it > 0 } ?: return
        val identity = scopedChatKey(target.scopeId, target.chatId)
        val active = findRegularNotification(context, target.scopeId, target.chatId)
        val currentGeneration = recentMessagingHistoryGenerations[identity]
            ?: active?.notification?.extras?.getLong(EXTRA_CARD_GENERATION)?.takeIf { it > 0 }
            ?: return
        val activeGeneration = active?.notification?.extras?.getLong(EXTRA_CARD_GENERATION)?.takeIf { it > 0 }
        if (activeGeneration != null && activeGeneration > currentGeneration) return
        val messages = recentMessagingHistory[identity]
            ?: active?.let { messagingHistoryFromNotification(it.notification) }
            ?: return
        val remaining = messages.filter { message ->
            val messageGeneration = message.extras.getLong(EXTRA_MESSAGE_GENERATION, 0)
            val messageId = message.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0)
            messageGeneration > dismissedGeneration || messageId > cutoff
        }
        if (remaining.size == messages.size) return
        if (remaining.isEmpty()) {
            recentMessagingHistory.remove(identity)
            recentMessagingHistoryGenerations.remove(identity)
            recentRegularNotificationTemplates.remove(identity)
            activeNotificationChats.remove(identity)
            val manager = context.getSystemService(NotificationManager::class.java)
            active?.let { manager.cancel(it.tag, it.id) }
            manager.cancel(regularTag(target.scopeId, target.chatId), REGULAR_NOTIFICATION_ID)
            return
        }
        rememberMessagingHistory(identity, remaining)
        val template = recentRegularNotificationTemplates[identity]
        if (template != null) {
            repostRegularWithMessages(context, template, regularTag(target.scopeId, target.chatId), REGULAR_NOTIFICATION_ID, remaining)
        } else if (active != null && active.notification.extras.getLong(EXTRA_CARD_GENERATION, 0) <= currentGeneration) {
            repostRegularWithMessages(context, active, remaining)
        }
    }

    private fun repostRegularWithMessages(
        context: Context,
        active: android.service.notification.StatusBarNotification,
        messages: List<NotificationCompat.MessagingStyle.Message>,
    ) = repostRegularWithMessages(context, active.notification, active.tag, active.id, messages,
        isLegacyRegularNotification(active, active.notification.extras.getString(EXTRA_SCOPE_ID).orEmpty(),
            active.notification.extras.getString(EXTRA_CHAT_ID).orEmpty()))

    private fun repostRegularWithMessages(
        context: Context,
        template: Notification,
        tag: String,
        id: Int,
        messages: List<NotificationCompat.MessagingStyle.Message>,
        legacy: Boolean = false,
    ) {
        val extras = template.extras
        val scopeId = extras.getString(EXTRA_SCOPE_ID) ?: return
        val chatId = extras.getString(EXTRA_CHAT_ID) ?: return
        if (CacheScope.from(GlobalParam(context))?.id != scopeId) return
        val chatTitle = extras.getString(EXTRA_CHAT_TITLE).orEmpty()
        val sortedMessages = messages.sortedWith(compareBy<NotificationCompat.MessagingStyle.Message> { it.timestamp }
            .thenBy { it.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) })
        val latestIncomingIndex = sortedMessages.indices.lastOrNull { index ->
            sortedMessages[index].extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) > 0
        }
        val style = NotificationCompat.MessagingStyle(
            Person.Builder().setName(localizedContext(context).getString(R.string.notification_me)).build(),
        ).setGroupConversation(extras.getBoolean(EXTRA_GROUP_CHAT))
        if (extras.getBoolean(EXTRA_GROUP_CHAT)) style.setConversationTitle(chatTitle)
        sortedMessages.forEachIndexed { index, message ->
            style.addMessage(copyMessage(message, preserveSenderIcon = index == latestIncomingIndex))
        }
        val notificationTag = regularTag(scopeId, chatId)
        val generation = recentMessagingHistoryGenerations[scopedChatKey(scopeId, chatId)]
            ?: extras.getLong(EXTRA_CARD_GENERATION, 1L).coerceAtLeast(1L)
        val cutoff = maxOf(
            extras.getLong(EXTRA_RECEIVED_CUTOFF, 0),
            sortedMessages.maxOfOrNull { it.extras.getLong(EXTRA_SERVER_MESSAGE_ID, 0) } ?: 0,
        )
        val target = MessageNotificationTarget(scopeId, KIND_REGULAR, chatId, cutoff.toString())
        val notificationBuilder = NotificationCompat.Builder(context, template)
            .setStyle(style)
            .setOnlyAlertOnce(true)
            .setContentText(sortedMessages.lastOrNull()?.text?.toString().orEmpty())
            .setLargeIcon(template.largeIcon?.let { scaledBitmap(it, 64) })
            .setDeleteIntent(dismissPendingIntent(context, target, generation))
            .clearActions()
            .addExtras(Bundle().apply {
                putString(EXTRA_EVENT_ID, cutoff.toString())
                putLong(EXTRA_MESSAGE_ID, cutoff)
                putLong(EXTRA_RECEIVED_CUTOFF, cutoff)
                putLong(EXTRA_CARD_GENERATION, generation)
                putParcelableArrayList(EXTRA_MESSAGING_HISTORY, serializedMessagingHistory(sortedMessages))
            })
        if (cutoff > 0) {
            val identity = scopedChatKey(scopeId, chatId)
            val replyPendingIntent = PendingIntent.getBroadcast(
                context, identity.hashCode(), actionIntent(context, target, ACTION_REPLY),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE,
            )
            val remoteInput = RemoteInput.Builder(REPLY_INPUT_KEY)
                .setLabel(localizedContext(context).getString(R.string.notification_reply_hint)).build()
            notificationBuilder.addAction(NotificationCompat.Action.Builder(
                0, localizedContext(context).getString(R.string.notification_reply), replyPendingIntent,
            ).addRemoteInput(remoteInput)
                .setSemanticAction(NotificationCompat.Action.SEMANTIC_ACTION_REPLY).build())
            val readPendingIntent = PendingIntent.getBroadcast(
                context, identity.hashCode(), actionIntent(context, target, ACTION_MARK_AS_READ),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            )
            notificationBuilder.addAction(0, localizedContext(context).getString(R.string.notification_mark_read), readPendingIntent)
        }
        val notification = notificationBuilder.build()
        rememberMessagingHistory(scopedChatKey(scopeId, chatId), sortedMessages)
        recentMessagingHistoryGenerations[scopedChatKey(scopeId, chatId)] = generation
        rememberRegularNotificationTemplate(scopedChatKey(scopeId, chatId), notification)
        NotificationManagerCompat.from(context).notify(notificationTag, REGULAR_NOTIFICATION_ID, notification)
        if (legacy) context.getSystemService(NotificationManager::class.java).cancel(tag, id)
        activeNotificationChats.add(scopedChatKey(scopeId, chatId))
    }

    private fun findRegularNotification(context: Context, scopeId: String, chatId: String) =
        context.getSystemService(NotificationManager::class.java).activeNotifications.firstOrNull {
            (it.tag == regularTag(scopeId, chatId) && it.id == REGULAR_NOTIFICATION_ID) ||
                isLegacyRegularNotification(it, scopeId, chatId)
        }

    internal fun messagingHistoryFromNotification(
        notification: Notification,
    ): List<NotificationCompat.MessagingStyle.Message> {
        val history = notification.extras.getParcelableArrayList<Bundle>(EXTRA_MESSAGING_HISTORY)
        if (!history.isNullOrEmpty()) {
            return history.map { stored ->
                val person = stored.getBundle("person")?.let { Person.fromBundle(it) }
                NotificationCompat.MessagingStyle.Message(
                    stored.getCharSequence("text"), stored.getLong("timestamp"), person,
                ).also { message ->
                    message.extras.putAll(stored.getBundle("message_extras") ?: Bundle())
                }
            }
        }
        return NotificationCompat.MessagingStyle.extractMessagingStyleFromNotification(notification)
            ?.messages.orEmpty()
    }

    private fun rememberMessagingHistory(
        identity: String,
        messages: List<NotificationCompat.MessagingStyle.Message>,
    ) {
        recentMessagingHistory[identity] = messages.takeLast(MAX_MESSAGING_HISTORY)
        while (recentMessagingHistory.size > MAX_CACHED_MESSAGING_HISTORIES) {
            val evictedIdentity = recentMessagingHistory.keys.first()
            recentMessagingHistory.remove(evictedIdentity)
            recentMessagingHistoryGenerations.remove(evictedIdentity)
            recentRegularNotificationTemplates.remove(evictedIdentity)
        }
    }

    private fun rememberRegularNotificationTemplate(identity: String, notification: Notification) {
        recentRegularNotificationTemplates[identity] = notification
        while (recentRegularNotificationTemplates.size > MAX_CACHED_MESSAGING_HISTORIES) {
            recentRegularNotificationTemplates.remove(recentRegularNotificationTemplates.keys.first())
        }
    }

    internal fun serializedMessagingHistory(
        messages: List<NotificationCompat.MessagingStyle.Message>,
    ): ArrayList<Bundle> = ArrayList(messages.takeLast(MAX_MESSAGING_HISTORY).map { message ->
        Bundle().apply {
            putCharSequence("text", message.text)
            putLong("timestamp", message.timestamp)
            message.person?.let { putBundle("person", it.toBundle()) }
            putBundle("message_extras", Bundle(message.extras))
        }
    })

    private fun replyStatusText(context: Context, status: String): String = when (status) {
        "sent" -> localizedContext(context).getString(R.string.notification_reply_sent)
        "failed" -> localizedContext(context).getString(R.string.notification_reply_failed_short)
        else -> localizedContext(context).getString(R.string.notification_reply_queued)
    }

    private fun copyMessage(source: NotificationCompat.MessagingStyle.Message) =
        copyMessage(source, preserveSenderIcon = true)

    @Synchronized
    internal fun showReadError(context: Context, target: MessageNotificationTarget) {
        if (CacheScope.from(GlobalParam(context))?.id != target.scopeId) return
        val active = findRegularNotification(context, target.scopeId, target.chatId) ?: return
        val notification = NotificationCompat.Builder(context, active.notification)
            .setSubText(localizedContext(context).getString(R.string.notification_mark_read_failed))
            .setOnlyAlertOnce(true).build()
        NotificationManagerCompat.from(context).notify(active.tag, active.id, notification)
    }

    @Synchronized
    internal fun showReplyError(context: Context, target: MessageNotificationTarget) {
        if (CacheScope.from(GlobalParam(context))?.id != target.scopeId) return
        val active = findRegularNotification(context, target.scopeId, target.chatId) ?: return
        val notification = NotificationCompat.Builder(context, active.notification)
            .setSubText(localizedContext(context).getString(R.string.notification_reply_failed))
            .setOnlyAlertOnce(true).build()
        NotificationManagerCompat.from(context).notify(active.tag, active.id, notification)
    }

    internal fun hideEncryptedNotification(context: Context, target: MessageNotificationTarget) {
        if (target.kind != KIND_PRIVATE && target.kind != KIND_SECRET) return
        context.getSystemService(NotificationManager::class.java).cancel(encryptedTag(target), 0)
    }

    /**
     * Уведомление о запросе на приватный чат (type=private_chat_invite).
     * Упрощённая версия showMessageNotification: без action «Прочитано»,
     * тап открывает приватный чат в ChatActivity через MainActivity (EXTRA_IS_PRIVATE_CHAT).
     */
    fun showPrivateInviteNotification(
        context: Context,
        inviterName: String,
        chatId: String,
        avatarBitmap: Bitmap?
    ) {
        val scopeId = CacheScope.from(GlobalParam(context))?.id ?: return
        if (chatId.isBlank()) return
        try {
            val identity = scopedChatKey(scopeId, chatId)
            val target = MessageNotificationTarget(scopeId, KIND_PRIVATE, chatId, "invite", chatId)
            val notificationTag = "invite:$scopeId:$chatId"

            val contentIntent = Intent(context, MainActivity::class.java).apply {
                putExtra(EXTRA_CHAT_ID, chatId)
                putExtra(EXTRA_IS_PRIVATE_CHAT, true)
                putExtras(metadata(target))
                action = Intent.ACTION_VIEW
                data = actionUri(target, Intent.ACTION_VIEW)
                flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            }
            val contentPendingIntent = PendingIntent.getActivity(
                context,
                identity.hashCode(),
                contentIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )

            val effectiveBitmap = avatarBitmap ?: createPlaceholderBitmap(inviterName, chatId.hashCode().toLong())
            val softBitmap = scaledBitmap(effectiveBitmap, 64)

            val builder = NotificationCompat.Builder(context, CHANNEL_CHAT_MESSAGES)
                .setSmallIcon(R.drawable.ic_notification)
                .setLargeIcon(softBitmap)
                .setColor(context.resources.getColor(R.color.primary, null))
                .setContentTitle(inviterName)
                .setContentText(context.getString(R.string.private_chat_invite_incoming))
                .setAutoCancel(true)
                .setPriority(NotificationCompat.PRIORITY_HIGH)
                .setCategory(NotificationCompat.CATEGORY_MESSAGE)
                .setContentIntent(contentPendingIntent)

            try {
                NotificationManagerCompat.from(context).notify(notificationTag, 0, builder.build())
                activeNotificationChats.add(identity)
            } catch (e: SecurityException) {
                Log.w(TAG, "No notification permission", e)
            }
            Log.d(TAG, "Private invite notification shown: chatId=$chatId, inviter=$inviterName")
        } catch (e: Exception) {
            Log.e(TAG, "Failed to show private invite notification", e)
        }
    }

    fun showIncomingCallNotification(
        context: Context,
        callId: String,
        callerName: String,
        mediaType: String,
        callerUserId: Long = 0L,
        chatId: String = "",
        chatTitle: String = ""
    ) {
        if (callId.isBlank()) return

        val identity = callNotificationIdentity(context, callId)
        val notificationTag = "call:$identity"
        val notificationId = identity.hashCode()
        val displayName = callerName.ifBlank { chatTitle.ifBlank { context.getString(R.string.app_name) } }
        val contentIntent = Intent(context, IncomingCallActivity::class.java).apply {
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
            putExtra(CallExtras.EXTRA_CALLER_NAME, displayName)
            putExtra(CallExtras.EXTRA_CALLER_USER_ID, callerUserId)
            putExtra(CallExtras.EXTRA_CHAT_ID, chatId)
            putExtra(CallExtras.EXTRA_CHAT_TITLE, chatTitle)
            putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
            putExtra(EXTRA_SCOPE_ID, CacheScope.from(GlobalParam(context))?.id)
            data = Uri.parse("barkfluff://call/$identity/open")
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or
                Intent.FLAG_ACTIVITY_CLEAR_TOP or
                Intent.FLAG_ACTIVITY_NO_USER_ACTION
        }
        val contentPendingIntent = PendingIntent.getActivity(
            context,
            notificationId,
            contentIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val acceptIntent = Intent(context, IncomingCallActivity::class.java).apply {
            action = CallExtras.ACTION_ACCEPT_CALL
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
            putExtra(CallExtras.EXTRA_CALLER_NAME, displayName)
            putExtra(CallExtras.EXTRA_CALLER_USER_ID, callerUserId)
            putExtra(CallExtras.EXTRA_CHAT_ID, chatId)
            putExtra(CallExtras.EXTRA_CHAT_TITLE, chatTitle)
            putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
            putExtra(EXTRA_SCOPE_ID, CacheScope.from(GlobalParam(context))?.id)
            data = Uri.parse("barkfluff://call/$identity/accept")
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or
                Intent.FLAG_ACTIVITY_CLEAR_TOP or
                Intent.FLAG_ACTIVITY_NO_USER_ACTION
        }
        val acceptPendingIntent = PendingIntent.getActivity(
            context,
            notificationId + 1,
            acceptIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val rejectIntent = Intent(context, CallActionReceiver::class.java).apply {
            action = CallExtras.ACTION_REJECT_CALL
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
            putExtra(EXTRA_SCOPE_ID, CacheScope.from(GlobalParam(context))?.id)
            data = Uri.parse("barkfluff://call/$identity/reject")
        }
        val rejectPendingIntent = PendingIntent.getBroadcast(
            context,
            notificationId + 2,
            rejectIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        // Аватар, загруженный при обработке push до показа звонка; иначе — инициалы
        val avatarBitmap = IncomingCallPrefetch.avatarBitmap(callId)
            ?: createPlaceholderBitmap(displayName, callerUserId.takeIf { it > 0 } ?: callId.hashCode().toLong())

        val person = Person.Builder()
            .setName(displayName)
            .setKey(if (callerUserId > 0) callerUserId.toString() else callId)
            .setImportant(true)
            .setIcon(IconCompat.createWithBitmap(toSoftwareBitmap(avatarBitmap)))
            .build()

        val title = context.getString(
            if (mediaType.equals("video", ignoreCase = true)) {
                R.string.incoming_call_video
            } else {
                R.string.incoming_call_audio
            }
        )
        val alertIssue = incomingCallAlertIssue(context)
        when (alertIssue) {
            IncomingCallAlertIssue.NOTIFICATIONS_DISABLED -> {
                Log.e(TAG, "Incoming call alert skipped: notifications are disabled")
                return
            }
            IncomingCallAlertIssue.CHANNEL_NOT_ALERTING -> {
                Log.w(TAG, "Incoming call channel is not alerting; full-screen UI may not be shown")
            }
            IncomingCallAlertIssue.FULL_SCREEN_INTENT_DISABLED -> {
                Log.w(TAG, "Incoming call full-screen access is disabled; Android will show only the notification")
            }
            null -> Unit
        }

        val builder = NotificationCompat.Builder(context, CHANNEL_INCOMING_CALLS)
            .setSmallIcon(R.drawable.ic_notification)
            .setColor(context.resources.getColor(R.color.primary, null))
            .setContentTitle(displayName)
            .setContentText(title)
            .setCategory(NotificationCompat.CATEGORY_CALL)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setOngoing(true)
            .setAutoCancel(false)
            .setDefaults(NotificationCompat.DEFAULT_VIBRATE)
            .setVibrate(longArrayOf(0, 600, 600, 600))
            .setContentIntent(contentPendingIntent)
            .setFullScreenIntent(contentPendingIntent, true)
            .addPerson(person)
            .setStyle(NotificationCompat.CallStyle.forIncomingCall(person, rejectPendingIntent, acceptPendingIntent))
            .addExtras(Bundle().apply {
                putString(EXTRA_SCOPE_ID, CacheScope.from(GlobalParam(context))?.id)
                putString(CallExtras.EXTRA_CALL_ID, callId)
            })

        try {
            NotificationManagerCompat.from(context).notify(notificationTag, 0, builder.build())
            startIncomingCallRingtone(context, callId)
        } catch (e: SecurityException) {
            Log.w(TAG, "No notification permission for incoming call", e)
        }
    }


    fun buildOngoingCallNotification(
        context: Context,
        callId: String,
        title: String,
        mediaType: String,
        livekitUrl: String,
        accessToken: String
    ): android.app.Notification {
        val displayName = title.ifBlank { context.getString(R.string.call_title_default) }
        val identity = callNotificationIdentity(context, callId)
        val notificationId = identity.hashCode()
        val contentIntent = Intent(context, CallActivity::class.java).apply {
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
            putExtra(CallExtras.EXTRA_CALLER_NAME, displayName)
            putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
            putExtra(CallExtras.EXTRA_LIVEKIT_URL, livekitUrl)
            putExtra(CallExtras.EXTRA_ACCESS_TOKEN, accessToken)
            data = Uri.parse("barkfluff://call/$identity/ongoing")
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val contentPendingIntent = PendingIntent.getActivity(
            context,
            notificationId + 3,
            contentIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val endIntent = Intent(context, CallActionReceiver::class.java).apply {
            action = CallExtras.ACTION_END_CALL
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
            data = Uri.parse("barkfluff://call/$identity/end")
        }
        val endPendingIntent = PendingIntent.getBroadcast(
            context,
            notificationId + 4,
            endIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val person = Person.Builder()
            .setName(displayName)
            .setKey(callId)
            .setIcon(IconCompat.createWithBitmap(createPlaceholderBitmap(displayName, callId.hashCode().toLong())))
            .build()

        val callType = context.getString(
            if (mediaType.equals("video", ignoreCase = true)) {
                R.string.incoming_call_video
            } else {
                R.string.incoming_call_audio
            }
        )
        return NotificationCompat.Builder(context, CHANNEL_CALLS)
            .setSmallIcon(R.drawable.ic_notification)
            .setColor(context.resources.getColor(R.color.primary, null))
            .setContentTitle(displayName)
            .setContentText(context.getString(R.string.notification_call_in_progress, callType))
            .setCategory(NotificationCompat.CATEGORY_CALL)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setOngoing(true)
            .setSilent(true)
            .setAutoCancel(false)
            .setContentIntent(contentPendingIntent)
            .setStyle(NotificationCompat.CallStyle.forOngoingCall(person, endPendingIntent))
            .addExtras(Bundle().apply { putString(EXTRA_SCOPE_ID, CacheScope.from(GlobalParam(context))?.id) })
            .build()
    }
    fun dismissCall(context: Context, callId: String) {
        if (callId.isBlank()) return
        clearIncomingCallAlert(context, callId)
        // Не разрываем Telecom-соединение если звонок уже принят локально —
        // сервер рассылает dismiss_call на все устройства включая то, что ответило.
        if (!CallTelecomRegistry.isAnsweringOrActive(callId)) {
            CallTelecomRegistry.disconnect(callId, DisconnectCause.LOCAL)
        }
        Log.d(TAG, "Call notification dismissed: callId=$callId")
    }

    fun clearIncomingCallAlert(context: Context, callId: String) {
        if (callId.isBlank()) return
        stopIncomingCallRingtone(callId)
        IncomingCallPrefetch.clear(callId)
        val manager = context.getSystemService(NotificationManager::class.java)
        val scopeId = CacheScope.from(GlobalParam(context))?.id ?: return
        val tag = "call:$scopeId:$callId"
        manager.cancel(tag, 0)
        manager.activeNotifications.filter {
            it.tag == null && it.id == callId.hashCode() &&
                it.notification.extras.getString(EXTRA_SCOPE_ID) == scopeId &&
                it.notification.extras.getString(CallExtras.EXTRA_CALL_ID) == callId
        }.forEach { manager.cancel(it.tag, it.id) }
    }

    @Synchronized
    fun dismissForChat(context: Context, chatId: String, messageId: Long = 0) {
        val manager = context.getSystemService(NotificationManager::class.java)
        val scopeId = CacheScope.from(GlobalParam(context))?.id
        if (scopeId == null) return
        val identity = scopedChatKey(scopeId, chatId)
        if (messageId <= 0) {
            manager.cancel(regularTag(scopeId, chatId), REGULAR_NOTIFICATION_ID)
            manager.activeNotifications.filter { isLegacyRegularNotification(it, scopeId, chatId) }
                .forEach { manager.cancel(it.tag, it.id) }
            manager.activeNotifications.filter { active ->
                val extras = active.notification.extras
                extras.getString(EXTRA_SCOPE_ID) == scopeId &&
                    extras.getString(EXTRA_CHAT_ID) == chatId &&
                    extras.getString(EXTRA_KIND) in setOf(KIND_PRIVATE, KIND_SECRET)
            }.forEach { manager.cancel(it.tag, it.id) }
            recentMessagingHistory.remove(identity)
            recentMessagingHistoryGenerations.remove(identity)
            recentRegularNotificationTemplates.remove(identity)
            activeNotificationChats.remove(identity)
        } else {
            dismissReadMessages(context, MessageNotificationTarget(scopeId, KIND_REGULAR, chatId, messageId.toString()))
        }
        Log.d(TAG, "Notification dismissed for chatId=$chatId")
    }

    @Synchronized
    fun dismissAllForScope(context: Context, scopeId: String) {
        if (scopeId.isBlank()) return
        val manager = context.getSystemService(NotificationManager::class.java)
        val prefixes = listOf("regular:$scopeId:", "encrypted:$scopeId:", "invite:$scopeId:", "call:$scopeId:")
        manager.activeNotifications.filter { active ->
            active.notification.extras.getString(EXTRA_SCOPE_ID) == scopeId ||
                prefixes.any { active.tag?.startsWith(it) == true }
        }.forEach { manager.cancel(it.tag, it.id) }
        recentMessagingHistory.keys.removeAll { it.startsWith("$scopeId|") }
        recentMessagingHistoryGenerations.keys.removeAll { it.startsWith("$scopeId|") }
        recentRegularNotificationTemplates.keys.removeAll { it.startsWith("$scopeId|") }
        activeNotificationChats.removeAll { it.startsWith("$scopeId|") }
        val shortcutIds = ShortcutManagerCompat.getDynamicShortcuts(context)
            .map { it.id }
            .filter { it.startsWith("conversation:$scopeId|") }
        if (shortcutIds.isNotEmpty()) {
            ShortcutManagerCompat.removeDynamicShortcuts(context, shortcutIds)
            ShortcutManagerCompat.removeLongLivedShortcuts(context, shortcutIds)
        }
    }

    fun showSystemNotification(
        context: Context,
        title: String,
        text: String,
        notificationId: Int = title.hashCode()
    ) {
        try {
            val builder = NotificationCompat.Builder(context, CHANNEL_SYSTEM)
                .setSmallIcon(R.drawable.ic_notification)
                .setColor(context.resources.getColor(R.color.primary, null))
                .setContentTitle(title)
                .setContentText(text)
                .setAutoCancel(true)
                .setPriority(NotificationCompat.PRIORITY_DEFAULT)

            try {
                NotificationManagerCompat.from(context).notify(notificationId, builder.build())
            } catch (e: SecurityException) {
                Log.w(TAG, "No notification permission", e)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Failed to show system notification", e)
        }
    }

    private fun toSoftwareBitmap(bitmap: Bitmap): Bitmap {
        return if (bitmap.config == Bitmap.Config.HARDWARE) {
            bitmap.copy(Bitmap.Config.ARGB_8888, false)
        } else {
            bitmap
        }
    }

    @Synchronized
    private fun startIncomingCallRingtone(context: Context, callId: String) {
        val currentRingtone = incomingCallRingtone
        if (ringingCallId == callId && currentRingtone?.isPlaying == true) return

        stopIncomingCallRingtone()

        val ringtoneUri = RingtoneManager.getActualDefaultRingtoneUri(
            context,
            RingtoneManager.TYPE_RINGTONE
        ) ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE)

        incomingCallRingtone = RingtoneManager.getRingtone(context.applicationContext, ringtoneUri)?.apply {
            audioAttributes = AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_NOTIFICATION_RINGTONE)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                .build()
            isLooping = true
            play()
        }
        ringingCallId = callId
    }

    @Synchronized
    private fun stopIncomingCallRingtone(callId: String? = null) {
        if (callId != null && ringingCallId != callId) return

        runCatching { incomingCallRingtone?.stop() }
            .onFailure { Log.w(TAG, "Failed to stop incoming call ringtone", it) }
        incomingCallRingtone = null
        ringingCallId = null
    }

    fun canUseFullScreenIntent(context: Context): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.UPSIDE_DOWN_CAKE) return true
        return context.getSystemService(NotificationManager::class.java).canUseFullScreenIntent()
    }

    fun fullScreenIntentSettingsIntent(context: Context): Intent? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.UPSIDE_DOWN_CAKE || canUseFullScreenIntent(context)) {
            return null
        }
        return Intent(Settings.ACTION_MANAGE_APP_USE_FULL_SCREEN_INTENT).apply {
            data = Uri.parse("package:${context.packageName}")
        }
    }

    fun notificationSettingsIntent(context: Context): Intent =
        Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).apply {
            putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName)
        }

    private fun incomingCallAlertIssue(context: Context): IncomingCallAlertIssue? {
        val manager = context.getSystemService(NotificationManager::class.java)
        val channelImportance = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            manager.getNotificationChannel(CHANNEL_INCOMING_CALLS)?.importance
                ?: IncomingCallAlertPolicy.MIN_ALERTING_CHANNEL_IMPORTANCE
        } else {
            IncomingCallAlertPolicy.MIN_ALERTING_CHANNEL_IMPORTANCE
        }

        return IncomingCallAlertPolicy.issue(
            notificationsEnabled = NotificationManagerCompat.from(context).areNotificationsEnabled(),
            channelImportance = channelImportance,
            fullScreenIntentEnabled = canUseFullScreenIntent(context)
        )
    }

    private val PLACEHOLDER_COLORS = intArrayOf(
        0xFFE57373.toInt(), 0xFFFF8A65.toInt(), 0xFFFFB74D.toInt(),
        0xFFFFD54F.toInt(), 0xFFAED581.toInt(), 0xFF4DB6AC.toInt(),
        0xFF4FC3F7.toInt(), 0xFF7986CB.toInt(), 0xFFBA68C8.toInt(),
        0xFFF06292.toInt(), 0xFF90A4AE.toInt(), 0xFFA1887F.toInt(),
    )

    /**
     * Генерирует Bitmap-placeholder с инициалами на цветном круге.
     * Используется когда реальный аватар недоступен.
     */
    private fun createPlaceholderBitmap(name: String, id: Long): Bitmap {
        val size = 256
        val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(bitmap)

        val colorIndex = (id.hashCode() and 0x7FFFFFFF) % PLACEHOLDER_COLORS.size
        val bgPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = PLACEHOLDER_COLORS[colorIndex]
            style = Paint.Style.FILL
        }
        canvas.drawCircle(size / 2f, size / 2f, size / 2f, bgPaint)

        val initials = getInitials(name)
        val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.WHITE
            textSize = size * 0.38f
            typeface = Typeface.DEFAULT_BOLD
            textAlign = Paint.Align.CENTER
        }
        val textY = size / 2f - (textPaint.descent() + textPaint.ascent()) / 2f
        canvas.drawText(initials, size / 2f, textY, textPaint)

        return bitmap
    }

    private const val REGULAR_NOTIFICATION_ID = 0
    private const val MAX_MESSAGING_HISTORY = 25
    private const val MAX_CACHED_MESSAGING_HISTORIES = 100

    private fun regularTag(scopeId: String, chatId: String) = "regular:$scopeId:$chatId"
    private fun scopedChatKey(scopeId: String, chatId: String) = "$scopeId|$chatId"
    private fun callNotificationIdentity(context: Context, callId: String) =
        "${CacheScope.from(GlobalParam(context))?.id ?: "unscoped"}:$callId"

    private fun isLegacyRegularNotification(
        active: android.service.notification.StatusBarNotification,
        scopeId: String,
        chatId: String,
    ): Boolean = active.tag == null && active.notification.extras.getString(EXTRA_SCOPE_ID) == scopeId &&
        active.notification.extras.getString(EXTRA_KIND) == KIND_REGULAR &&
        active.notification.extras.getString(EXTRA_CHAT_ID) == chatId

    private fun readShownKeys(context: Context) = LinkedHashSet(
        context.getSharedPreferences(DEDUP_PREFS, Context.MODE_PRIVATE).getStringSet("shown", emptySet()).orEmpty()
    )

    private fun notificationMessageKey(scopeId: String, chatId: String, messageId: Long) =
        Uri.Builder().appendPath(scopeId).appendPath(KIND_REGULAR).appendPath(chatId)
            .appendPath(messageId.toString()).build().toString()

    private fun formatNotificationText(context: Context, input: RegularNotificationInput): String {
        val caption = MarkdownRenderer.strip(input.text).trim().take(512)
        val count = input.attachmentCount.coerceAtLeast(1)
        val text = when (input.attachmentKind.lowercase(Locale.ROOT)) {
            "image" -> if (caption.isBlank()) context.getString(R.string.notification_photo)
                else context.getString(R.string.notification_photo_caption, caption)
            "video" -> "🎬 ${context.resources.getQuantityString(R.plurals.videos_count, count, count)}"
            "gif" -> "🎞 ${context.resources.getQuantityString(R.plurals.gifs_count, count, count)}"
            "document", "file" -> "📄 ${context.resources.getQuantityString(R.plurals.files_count, count, count)}"
            "audio" -> "🎵 ${context.resources.getQuantityString(R.plurals.audios_count, count, count)}"
            "voice" -> context.getString(R.string.reply_voice)
            "sticker" -> context.getString(R.string.reply_sticker)
            else -> caption
        }
        return text.take(512)
    }

    private fun copyMessage(
        source: NotificationCompat.MessagingStyle.Message,
        preserveSenderIcon: Boolean,
    ): NotificationCompat.MessagingStyle.Message {
        val sourcePerson = source.person
        val person = sourcePerson?.let {
            Person.Builder().setName(it.name).setKey(it.key).apply {
                if (preserveSenderIcon) it.icon?.let(::setIcon)
            }.build()
        }
        return NotificationCompat.MessagingStyle.Message(source.text, source.timestamp, person).also { copied ->
            copied.extras.putAll(source.extras)
        }
    }

    private fun scaledBitmap(bitmap: Bitmap, size: Int): Bitmap {
        val software = toSoftwareBitmap(bitmap)
        return if (software.width == size && software.height == size) software
        else Bitmap.createScaledBitmap(software, size, size, true)
    }

    private fun getInitials(name: String): String {
        if (name.isBlank()) return "?"
        val parts = name.trim().split("\\s+".toRegex())
        return when {
            parts.size >= 2 -> "${parts[0].first().uppercaseChar()}${parts[1].first().uppercaseChar()}"
            parts[0].length >= 2 -> "${parts[0][0].uppercaseChar()}${parts[0][1].lowercaseChar()}"
            else -> parts[0].first().uppercaseChar().toString()
        }
    }
}
