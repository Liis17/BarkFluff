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
import kotlinx.coroutines.delay

object NotificationHelper {

    private const val TAG = "NotificationHelper"

    private const val DEDUP_MAX_SIZE = 100
    private const val DEDUP_PREFS = "message_notification_events"
    private val latestRegularTargets = mutableMapOf<Int, MessageNotificationTarget>()
    private val pendingReplyDismissals = mutableMapOf<Int, Int>()

    // Пул активных уведомлений — chatId'ы у которых сейчас висит уведомление в шторке
    val activeNotificationChats: MutableSet<String> =
        java.util.Collections.newSetFromMap(java.util.concurrent.ConcurrentHashMap())

    private var incomingCallRingtone: Ringtone? = null
    private var ringingCallId: String? = null

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
    const val KIND_REGULAR = "regular"
    const val KIND_PRIVATE = "private"
    const val KIND_SECRET = "secret"
    const val ACTION_MARK_AS_READ = "com.barkfluff.client.ACTION_MARK_AS_READ"
    const val ACTION_REPLY = "com.barkfluff.client.ACTION_NOTIFICATION_REPLY"
    const val ACTION_HIDE = "com.barkfluff.client.ACTION_NOTIFICATION_HIDE"
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
        if (CacheScope.from(GlobalParam(context))?.id != scopeId) return
        val target = MessageNotificationTarget(scopeId, KIND_REGULAR, chatId, messageId.toString())
        val strings = localizedContext(context)
        try {
            val notificationId = chatId.hashCode()

            // Content intent — открыть чат
            val contentIntent = Intent(context, MainActivity::class.java).apply {
                putExtra(EXTRA_CHAT_ID, chatId)
                putExtra(EXTRA_SCOPE_ID, scopeId)
                data = actionUri(target, Intent.ACTION_VIEW)
                action = Intent.ACTION_VIEW
                flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            }
            val contentPendingIntent = PendingIntent.getActivity(
                context,
                notificationId,
                contentIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )

            // "Прочитано" action
            val readIntent = actionIntent(context, target, ACTION_MARK_AS_READ)
            val readPendingIntent = PendingIntent.getBroadcast(
                context,
                0,
                readIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
            )

            // Иконка аватара — если bitmap нет, генерируем placeholder с инициалами
            val effectiveBitmap = avatarBitmap ?: createPlaceholderBitmap(senderName, chatId.hashCode().toLong())
            val softBitmap = toSoftwareBitmap(effectiveBitmap)
            val avatarIcon = IconCompat.createWithBitmap(softBitmap)

            // Person отправителя — его иконка станет большой круглой аватаркой
            val senderPerson = Person.Builder()
                .setName(senderName)
                .setKey(chatId)
                .setIcon(avatarIcon)
                .build()

            // Динамический Shortcut — обязателен для Android 11+ чтобы уведомление
            // попало в секцию "Диалоги" и аватар отображался большим кругом слева
            val shortcut = ShortcutInfoCompat.Builder(context, chatId)
                .setShortLabel(senderName)
                .setLongLabel(senderName)
                .setIcon(avatarIcon)
                .setIntent(contentIntent)
                .setLongLived(true)
                .setPerson(senderPerson)
                .build()
            ShortcutManagerCompat.pushDynamicShortcut(context, shortcut)

            // "Я" — текущий пользователь, передаётся в конструктор MessagingStyle
            val mePerson = Person.Builder().setName(strings.getString(R.string.notification_me)).build()

            // MessagingStyle — Android берёт иконку из senderPerson → большая круглая аватарка,
            // setSmallIcon → маленький бейдж в углу аватарки
            val cleanText = MarkdownRenderer.strip(messageText)
            val displayText = if (imageBitmap != null && cleanText.isBlank()) {
                strings.getString(R.string.notification_photo)
            } else if (imageBitmap != null) {
                strings.getString(R.string.notification_photo_caption, cleanText)
            } else {
                cleanText
            }

            val messagingStyle = NotificationCompat.MessagingStyle(mePerson)
                .addMessage(displayText, System.currentTimeMillis(), senderPerson)

            val builder = NotificationCompat.Builder(strings, CHANNEL_CHAT_MESSAGES)
                .setSmallIcon(R.drawable.ic_notification)
                .setLargeIcon(softBitmap)
                .setColor(context.resources.getColor(R.color.primary, null))
                .setAutoCancel(true)
                .setShortcutId(chatId)
                .setPriority(NotificationCompat.PRIORITY_HIGH)
                .setCategory(NotificationCompat.CATEGORY_MESSAGE)
                .setContentIntent(contentPendingIntent)
                .addExtras(metadata(target))

            if (messageId > 0) {
                val replyPendingIntent = PendingIntent.getBroadcast(
                    context, 0, actionIntent(context, target, ACTION_REPLY),
                    PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE
                )
                val remoteInput = RemoteInput.Builder(REPLY_INPUT_KEY)
                    .setLabel(strings.getString(R.string.notification_reply_hint))
                    .build()
                builder.addAction(NotificationCompat.Action.Builder(
                    0, strings.getString(R.string.notification_reply), replyPendingIntent
                ).addRemoteInput(remoteInput)
                    .setSemanticAction(NotificationCompat.Action.SEMANTIC_ACTION_REPLY)
                    .build())
                builder.addAction(0, strings.getString(R.string.notification_mark_read), readPendingIntent)
            }

            // BigPictureStyle для изображений, иначе MessagingStyle
            if (imageBitmap != null) {
                val bigPictureStyle = NotificationCompat.BigPictureStyle()
                    .bigPicture(toSoftwareBitmap(imageBitmap))
                    .setSummaryText(senderName)
                builder.setStyle(bigPictureStyle)
            } else {
                builder.setStyle(messagingStyle)
            }

            try {
                postMessage(context, target, null, notificationId, builder.build())
            } catch (e: SecurityException) {
                Log.w(TAG, "No notification permission", e)
            }
            Log.d(TAG, "Notification shown: chatId=$chatId, sender=$senderName, " +
                    "hasAvatar=${avatarBitmap != null}, hasImage=${imageBitmap != null}")
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
            putExtra(EXTRA_SCOPE_ID, scopeId)
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

    private fun actionUri(target: MessageNotificationTarget, action: String) = Uri.Builder()
        .scheme("barkfluff").authority("message-notification")
        .appendPath(target.scopeId).appendPath(target.kind).appendPath(target.threadId)
        .appendPath(target.eventId).appendPath(action).build()

    private fun metadata(target: MessageNotificationTarget) = Bundle().apply {
        putString(EXTRA_SCOPE_ID, target.scopeId)
        putString(EXTRA_KIND, target.kind)
        putString(EXTRA_CHAT_ID, target.chatId)
        putString(EXTRA_THREAD_ID, target.threadId)
        putString(EXTRA_EVENT_ID, target.eventId)
        putLong(EXTRA_MESSAGE_ID, target.eventId.toLongOrNull() ?: 0)
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
        if (intent.data != actionUri(target, intent.action ?: return null)) return null
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
            .appendPath(target.eventId).build().toString()
        val recent = LinkedHashSet(prefs.getStringSet("shown", emptySet()).orEmpty())
        val deduplicate = target.eventId != "0"
        if (deduplicate && key in recent) return
        val manager = context.getSystemService(NotificationManager::class.java)
        if (target.kind == KIND_REGULAR) {
            val previous = manager.activeNotifications.firstOrNull { it.tag == null && it.id == id }
            val previousId = previous?.notification?.takeIf {
                it.extras.getString(EXTRA_SCOPE_ID) == target.scopeId
            }?.extras?.getLong(EXTRA_MESSAGE_ID, 0) ?: 0
            val submittedId = latestRegularTargets[id]?.takeIf { it.scopeId == target.scopeId }
                ?.eventId?.toLongOrNull() ?: 0
            if (maxOf(previousId, submittedId) > (target.eventId.toLongOrNull() ?: 0)) return
        }
        NotificationManagerCompat.from(context).notify(tag, id, notification)
        if (target.kind == KIND_REGULAR) {
            // Keep submitted replacements until the system sees them, including active close loops.
            val activeIds = manager.activeNotifications.filter { it.tag == null }.map { it.id }.toSet()
            latestRegularTargets.keys.retainAll(activeIds + pendingReplyDismissals.keys + id)
            latestRegularTargets[id] = target
        }
        if (deduplicate) {
            recent.add(key)
            while (recent.size > DEDUP_MAX_SIZE) recent.remove(recent.first())
            prefs.edit().putStringSet("shown", recent).apply()
        }
        if (target.kind == KIND_REGULAR) activeNotificationChats.add(target.chatId)
    }

    internal suspend fun dismissAcceptedReply(context: Context, target: MessageNotificationTarget) {
        val manager = context.getSystemService(NotificationManager::class.java)
        val id = target.chatId.hashCode()
        fun currentNotification() = if (CacheScope.from(GlobalParam(context))?.id != target.scopeId ||
            latestRegularTargets[id]?.let { it != target } == true) null
        else manager.activeNotifications.firstOrNull {
            it.tag == null && it.id == id &&
                it.notification.extras.getString(EXTRA_SCOPE_ID) == target.scopeId &&
                it.notification.extras.getString(EXTRA_KIND) == KIND_REGULAR &&
                it.notification.extras.getString(EXTRA_EVENT_ID) == target.eventId
        }
        var pending = false
        try {
            synchronized(this) {
                val active = currentNotification() ?: return
                pendingReplyDismissals[id] = (pendingReplyDismissals[id] ?: 0) + 1
                pending = true
                // An app update releases SystemUI's Direct Reply lifetime extension.
                manager.notify(id, NotificationCompat.Builder(context, active.notification)
                    .setOnlyAlertOnce(true).build())
            }
            repeat(80) {
                delay(25)
                synchronized(this) {
                    currentNotification() ?: return
                    // A pending update may already be visible while the old posted record is retained.
                    manager.cancel(id)
                    activeNotificationChats.remove(target.chatId)
                }
            }
            Log.w(TAG, "Timed out closing accepted reply notification")
        } finally {
            if (pending) synchronized(this) {
                val remaining = requireNotNull(pendingReplyDismissals[id]) - 1
                if (remaining == 0) pendingReplyDismissals.remove(id) else pendingReplyDismissals[id] = remaining
            }
        }
    }

    internal fun hideEncryptedNotification(context: Context, target: MessageNotificationTarget) {
        if (target.kind != KIND_PRIVATE && target.kind != KIND_SECRET) return
        context.getSystemService(NotificationManager::class.java).cancel(encryptedTag(target), 0)
    }

    @Synchronized
    internal fun showReplyError(context: Context, target: MessageNotificationTarget) {
        val manager = context.getSystemService(NotificationManager::class.java)
        if (latestRegularTargets[target.chatId.hashCode()]?.let { it != target } == true) return
        val active = manager.activeNotifications.firstOrNull {
            it.tag == null && it.id == target.chatId.hashCode() &&
                it.notification.extras.getString(EXTRA_SCOPE_ID) == target.scopeId &&
                it.notification.extras.getString(EXTRA_EVENT_ID) == target.eventId
        } ?: return
        val notification = NotificationCompat.Builder(context, active.notification)
            .setSubText(localizedContext(context).getString(R.string.notification_reply_failed))
            .setOnlyAlertOnce(true).build()
        NotificationManagerCompat.from(context).notify(active.id, notification)
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
        try {
            val notificationId = chatId.hashCode()

            val contentIntent = Intent(context, MainActivity::class.java).apply {
                putExtra(EXTRA_CHAT_ID, chatId)
                putExtra(EXTRA_IS_PRIVATE_CHAT, true)
                action = Intent.ACTION_VIEW
                flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            }
            val contentPendingIntent = PendingIntent.getActivity(
                context,
                notificationId,
                contentIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE
            )

            val effectiveBitmap = avatarBitmap ?: createPlaceholderBitmap(inviterName, chatId.hashCode().toLong())
            val softBitmap = toSoftwareBitmap(effectiveBitmap)

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
                NotificationManagerCompat.from(context).notify(notificationId, builder.build())
                activeNotificationChats.add(chatId)
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

        val notificationId = callId.hashCode()
        val displayName = callerName.ifBlank { chatTitle.ifBlank { context.getString(R.string.app_name) } }
        val contentIntent = Intent(context, IncomingCallActivity::class.java).apply {
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
            putExtra(CallExtras.EXTRA_CALLER_NAME, displayName)
            putExtra(CallExtras.EXTRA_CALLER_USER_ID, callerUserId)
            putExtra(CallExtras.EXTRA_CHAT_ID, chatId)
            putExtra(CallExtras.EXTRA_CHAT_TITLE, chatTitle)
            putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
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

        try {
            NotificationManagerCompat.from(context).notify(notificationId, builder.build())
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
        val notificationId = callId.hashCode()
        val contentIntent = Intent(context, CallActivity::class.java).apply {
            putExtra(CallExtras.EXTRA_CALL_ID, callId)
            putExtra(CallExtras.EXTRA_CALLER_NAME, displayName)
            putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
            putExtra(CallExtras.EXTRA_LIVEKIT_URL, livekitUrl)
            putExtra(CallExtras.EXTRA_ACCESS_TOKEN, accessToken)
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
        context.getSystemService(NotificationManager::class.java).cancel(callId.hashCode())
    }

    @Synchronized
    fun dismissForChat(context: Context, chatId: String, messageId: Long = 0) {
        val manager = context.getSystemService(NotificationManager::class.java)
        val scopeId = CacheScope.from(GlobalParam(context))?.id
        if (messageId <= 0) {
            manager.cancel(chatId.hashCode())
            manager.activeNotifications.filter {
                it.notification.extras.getString(EXTRA_SCOPE_ID) == scopeId &&
                    it.notification.extras.getString(EXTRA_CHAT_ID) == chatId && it.tag != null
            }.forEach { manager.cancel(it.tag, it.id) }
            activeNotificationChats.remove(chatId)
        } else {
            val submitted = latestRegularTargets[chatId.hashCode()]
            if (submitted != null && submitted.scopeId == scopeId &&
                (submitted.eventId.toLongOrNull() ?: 0) > messageId) return
            val active = manager.activeNotifications.firstOrNull {
                it.tag == null && it.id == chatId.hashCode() &&
                    it.notification.extras.getString(EXTRA_SCOPE_ID) == scopeId &&
                    it.notification.extras.getString(EXTRA_KIND) == KIND_REGULAR &&
                    it.notification.extras.getLong(EXTRA_MESSAGE_ID, 0) in 1..messageId
            } ?: return
            manager.cancel(active.id)
            activeNotificationChats.remove(chatId)
        }
        Log.d(TAG, "Notification dismissed for chatId=$chatId")
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
