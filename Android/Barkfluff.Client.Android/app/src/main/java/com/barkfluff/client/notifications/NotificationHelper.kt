package com.barkfluff.client.notifications

import android.app.NotificationChannel
import android.app.NotificationChannelGroup
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Typeface
import android.os.Build
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.app.Person
import androidx.core.content.pm.ShortcutInfoCompat
import androidx.core.content.pm.ShortcutManagerCompat
import androidx.core.graphics.drawable.IconCompat
import com.barkfluff.client.MainActivity
import com.barkfluff.client.R

object NotificationHelper {

    private const val TAG = "NotificationHelper"

    // Дедупликация уведомлений по messageId - публичный для использования в FirebaseMessagingService
    val recentlyShownMessages = mutableSetOf<Long>()
    private const val DEDUP_MAX_SIZE = 100

    // Пул активных уведомлений — chatId'ы у которых сейчас висит уведомление в шторке
    val activeNotificationChats: MutableSet<String> =
        java.util.Collections.newSetFromMap(java.util.concurrent.ConcurrentHashMap())

    // Группа
    private const val GROUP_ID = "barkfluff"
    private const val GROUP_NAME = "BarkFluff"

    // Канал: Личные сообщения
    const val CHANNEL_CHAT_MESSAGES = "chat_messages"
    private const val CHANNEL_CHAT_MESSAGES_NAME = "Сообщения чатов"
    private const val CHANNEL_CHAT_MESSAGES_DESC = "Уведомления о новых сообщениях в личных и групповых чатах"

    // Канал: Системные
    const val CHANNEL_SYSTEM = "system"
    private const val CHANNEL_SYSTEM_NAME = "Системные"
    private const val CHANNEL_SYSTEM_DESC = "Системные уведомления приложения"

    // Канал: Прочее
    const val CHANNEL_OTHER = "other"
    private const val CHANNEL_OTHER_NAME = "Прочее"
    private const val CHANNEL_OTHER_DESC = "Остальные уведомления"

    const val EXTRA_CHAT_ID = "extra_chat_id"
    const val EXTRA_MESSAGE_ID = "extra_message_id"
    const val ACTION_MARK_AS_READ = "com.barkfluff.client.ACTION_MARK_AS_READ"

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
                CHANNEL_CHAT_MESSAGES_NAME,
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = CHANNEL_CHAT_MESSAGES_DESC
                group = GROUP_ID
                enableVibration(true)
                setShowBadge(true)
            }

            val systemChannel = NotificationChannel(
                CHANNEL_SYSTEM,
                CHANNEL_SYSTEM_NAME,
                NotificationManager.IMPORTANCE_DEFAULT
            ).apply {
                description = CHANNEL_SYSTEM_DESC
                group = GROUP_ID
                setShowBadge(false)
            }

            val otherChannel = NotificationChannel(
                CHANNEL_OTHER,
                CHANNEL_OTHER_NAME,
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = CHANNEL_OTHER_DESC
                group = GROUP_ID
                setShowBadge(false)
            }

            manager.createNotificationChannels(listOf(chatChannel, systemChannel, otherChannel))
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
        imageBitmap: Bitmap? = null
    ) {
        try {
            // Проверяем дубликаты по messageId
            synchronized(recentlyShownMessages) {
                if (messageId > 0 && recentlyShownMessages.contains(messageId)) {
                    Log.d(TAG, "Skipping duplicate notification for messageId=$messageId")
                    return
                }
                if (messageId > 0) {
                    recentlyShownMessages.add(messageId)
                    if (recentlyShownMessages.size > DEDUP_MAX_SIZE) {
                        recentlyShownMessages.clear()
                    }
                }
            }

            val notificationId = chatId.hashCode()

            // Content intent — открыть чат
            val contentIntent = Intent(context, MainActivity::class.java).apply {
                putExtra(EXTRA_CHAT_ID, chatId)
                action = Intent.ACTION_VIEW
                flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
            }
            val contentPendingIntent = PendingIntent.getActivity(
                context,
                notificationId,
                contentIntent,
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE
            )

            // "Прочитано" action
            val readIntent = Intent(context, MarkAsReadReceiver::class.java).apply {
                action = ACTION_MARK_AS_READ
                putExtra(EXTRA_CHAT_ID, chatId)
                putExtra(EXTRA_MESSAGE_ID, messageId)
            }
            val readPendingIntent = PendingIntent.getBroadcast(
                context,
                notificationId,
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
            val mePerson = Person.Builder().setName("Я").build()

            // MessagingStyle — Android берёт иконку из senderPerson → большая круглая аватарка,
            // setSmallIcon → маленький бейдж в углу аватарки
            val displayText = if (imageBitmap != null && messageText.isBlank()) {
                "\uD83D\uDCF7 Фото"
            } else if (imageBitmap != null) {
                "\uD83D\uDCF7 $messageText"
            } else {
                messageText
            }

            val messagingStyle = NotificationCompat.MessagingStyle(mePerson)
                .addMessage(displayText, System.currentTimeMillis(), senderPerson)

            val builder = NotificationCompat.Builder(context, CHANNEL_CHAT_MESSAGES)
                .setSmallIcon(R.drawable.ic_notification)
                .setLargeIcon(softBitmap)
                .setColor(context.resources.getColor(R.color.primary, null))
                .setAutoCancel(true)
                .setShortcutId(chatId)
                .setPriority(NotificationCompat.PRIORITY_HIGH)
                .setCategory(NotificationCompat.CATEGORY_MESSAGE)
                .setContentIntent(contentPendingIntent)
                .addAction(0, "Прочитано", readPendingIntent)

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
                NotificationManagerCompat.from(context).notify(notificationId, builder.build())
                activeNotificationChats.add(chatId)
            } catch (e: SecurityException) {
                Log.w(TAG, "No notification permission", e)
            }
            Log.d(TAG, "Notification shown: chatId=$chatId, sender=$senderName, " +
                    "hasAvatar=${avatarBitmap != null}, hasImage=${imageBitmap != null}")
        } catch (e: Exception) {
            Log.e(TAG, "Failed to show notification", e)
        }
    }

    fun dismissForChat(context: Context, chatId: String) {
        if (!activeNotificationChats.remove(chatId)) return  // уведомления нет — выход
        context.getSystemService(NotificationManager::class.java).cancel(chatId.hashCode())
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
