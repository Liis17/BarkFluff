package com.barkfluff.client.widget

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapShader
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.Shader
import android.graphics.drawable.BitmapDrawable
import android.util.Log
import android.view.View
import android.widget.RemoteViews
import coil.request.ImageRequest
import coil.request.SuccessResult
import com.barkfluff.client.ChatActivity
import com.barkfluff.client.MainActivity
import com.barkfluff.client.R
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withTimeout

/**
 * Рендерит RemoteViews для App Widget'а по WidgetConfig + снимку чатов.
 * Аватары грузит параллельно через Coil → круглый Bitmap → setImageViewBitmap.
 * Каждая загрузка ограничена по времени: при отказе или таймауте вместо аватара
 * подставляются инициалы, поэтому render всегда возвращает готовые RemoteViews.
 */
object WidgetRenderer {

    private const val TAG = "WidgetRenderer"
    private const val AVATAR_SIZE_DP = 40
    private const val ROW_REQUEST_CODE_MULTIPLIER = 10

    /** Лимит на один аватар. Аватары грузятся параллельно, поэтому это же и лимит на все. */
    private const val AVATAR_TIMEOUT_MS = 3_000L

    private val PLACEHOLDER_COLORS = intArrayOf(
        0xFFE57373.toInt(), 0xFFFF8A65.toInt(), 0xFFFFB74D.toInt(),
        0xFFFFD54F.toInt(), 0xFFAED581.toInt(), 0xFF4DB6AC.toInt(),
        0xFF4FC3F7.toInt(), 0xFF7986CB.toInt(), 0xFFBA68C8.toInt(),
        0xFFF06292.toInt(), 0xFF90A4AE.toInt(), 0xFFA1887F.toInt()
    )

    suspend fun render(
        context: Context,
        appWidgetId: Int,
        config: WidgetConfig,
        chats: List<GrpcManager.ChatData>,
        loggedIn: Boolean,
        grpcManager: GrpcManager?
    ): RemoteViews {
        val views = RemoteViews(context.packageName, R.layout.widget_pinned_chats)

        val title = config.name.ifBlank { context.getString(R.string.widget_default_name) }
        views.setTextViewText(R.id.widgetTitle, title)
        views.setOnClickPendingIntent(R.id.widgetRefresh, refreshPendingIntent(context, appWidgetId))
        views.setOnClickPendingIntent(R.id.widgetTitle, openAppPendingIntent(context, appWidgetId))

        // Контейнер строк — собираем заново через addView (одинаковые id внутри одного RemoteViews
        // адресуются только по первому вхождению, поэтому include + общий root не подходит).
        views.removeAllViews(R.id.widgetRowsContainer)

        if (!loggedIn) {
            views.setViewVisibility(R.id.widgetEmptyText, View.VISIBLE)
            views.setTextViewText(R.id.widgetEmptyText, context.getString(R.string.widget_login_required))
            views.setOnClickPendingIntent(R.id.widgetRoot, openAppPendingIntent(context, appWidgetId))
            return views
        }

        val chatsById = chats.associateBy { it.id }
        val orderedChats = config.chatIds
            .mapNotNull { chatsById[it] }
            .take(WidgetConfig.MAX_CHATS)

        if (orderedChats.isEmpty()) {
            views.setViewVisibility(R.id.widgetEmptyText, View.VISIBLE)
            views.setTextViewText(R.id.widgetEmptyText, context.getString(R.string.widget_empty_title))
            views.setOnClickPendingIntent(R.id.widgetRoot, openAppPendingIntent(context, appWidgetId))
            return views
        }

        views.setViewVisibility(R.id.widgetEmptyText, View.GONE)

        // Аватары грузим все сразу: последовательная загрузка упирается в сетевые таймауты
        // и не укладывается в бюджет обновления.
        val avatarBitmaps = coroutineScope {
            orderedChats
                .map { chat -> async { loadAvatarBitmap(context, chat, grpcManager) } }
                .awaitAll()
        }

        orderedChats.forEachIndexed { index, chat ->
            val rowViews = RemoteViews(context.packageName, R.layout.widget_chat_row)
            val displayTitle = chat.title.ifBlank { context.getString(R.string.widget_default_name) }
            rowViews.setTextViewText(R.id.rowTitle, displayTitle)
            rowViews.setTextViewText(R.id.rowPreview, buildPreview(context, chat))

            val unread = chat.countUnread
            if (unread > 0) {
                val text = if (unread > 99) "99+" else unread.toString()
                rowViews.setTextViewText(R.id.rowBadge, text)
                rowViews.setViewVisibility(R.id.rowBadge, View.VISIBLE)
            } else {
                rowViews.setViewVisibility(R.id.rowBadge, View.GONE)
            }

            val avatarBitmap = avatarBitmaps[index]
            if (avatarBitmap != null) {
                rowViews.setImageViewBitmap(R.id.rowAvatar, avatarBitmap)
            }

            rowViews.setOnClickPendingIntent(
                R.id.rowRoot,
                openChatPendingIntent(context, appWidgetId, index, chat.id, chat.title)
            )

            views.addView(R.id.widgetRowsContainer, rowViews)
        }

        return views
    }

    private fun buildPreview(context: Context, chat: GrpcManager.ChatData): String {
        val last = chat.lastMessage
        if (last != null) {
            val text = last.text
            if (text.isNotBlank()) return text
            return context.getString(R.string.attachment_generic)
        }
        return context.getString(R.string.messages_empty)
    }

    private suspend fun loadAvatarBitmap(
        context: Context,
        chat: GrpcManager.ChatData,
        grpcManager: GrpcManager?
    ): Bitmap? {
        val sizePx = dpToPx(context, AVATAR_SIZE_DP)
        val seedId = chat.id.hashCode().toLong()
        val displayName = chat.title.ifBlank { "?" }

        val fileId = chat.picturePreviewFileId.ifBlank { chat.pictureFileId }
        if (fileId.isBlank()) {
            return placeholderBitmap(displayName, seedId, sizePx)
        }

        // Под таймаутом вся сетевая часть: и резолв ссылки через gRPC, и сама загрузка.
        return try {
            withTimeout(AVATAR_TIMEOUT_MS) {
                var url: String? = AvatarLoader.urlCache[fileId]
                if (url == null) {
                    url = AvatarLoader.getUrlFromCache(fileId)
                }
                if (url == null && grpcManager != null) {
                    url = runCatching { grpcManager.getFileDownloadUrl(fileId).getOrNull() }.getOrNull()
                    if (!url.isNullOrBlank()) {
                        AvatarLoader.urlCache[fileId] = url!!
                        AvatarLoader.putUrlInCache(fileId, url!!)
                    }
                }

                if (url.isNullOrBlank()) {
                    return@withTimeout placeholderBitmap(displayName, seedId, sizePx)
                }

                val imageLoader = AvatarLoader.getImageLoader(context)
                val request = ImageRequest.Builder(context)
                    .data(url)
                    .memoryCacheKey(fileId)
                    .diskCacheKey(fileId)
                    .allowHardware(false)
                    .size(sizePx)
                    .build()
                val result = imageLoader.execute(request)
                if (result is SuccessResult) {
                    val src = (result.drawable as? BitmapDrawable)?.bitmap
                    if (src != null) circularCrop(src, sizePx) else placeholderBitmap(displayName, seedId, sizePx)
                } else {
                    placeholderBitmap(displayName, seedId, sizePx)
                }
            }
        } catch (e: TimeoutCancellationException) {
            Log.w(TAG, "Avatar load timed out (${AVATAR_TIMEOUT_MS}ms) for chat=${chat.id}")
            placeholderBitmap(displayName, seedId, sizePx)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "Failed to load avatar for chat=${chat.id}", e)
            placeholderBitmap(displayName, seedId, sizePx)
        }
    }

    private fun circularCrop(src: Bitmap, sizePx: Int): Bitmap {
        val srcMin = minOf(src.width, src.height)
        val cx = (src.width - srcMin) / 2
        val cy = (src.height - srcMin) / 2
        val cropped = if (src.width != src.height) Bitmap.createBitmap(src, cx, cy, srcMin, srcMin) else src
        val scaled = if (cropped.width != sizePx) Bitmap.createScaledBitmap(cropped, sizePx, sizePx, true) else cropped

        val out = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(out)
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        paint.shader = BitmapShader(scaled, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP)
        canvas.drawCircle(sizePx / 2f, sizePx / 2f, sizePx / 2f, paint)
        return out
    }

    private fun placeholderBitmap(displayName: String, seedId: Long, sizePx: Int): Bitmap {
        val out = Bitmap.createBitmap(sizePx, sizePx, Bitmap.Config.ARGB_8888)
        val canvas = Canvas(out)
        val color = PLACEHOLDER_COLORS[(seedId.hashCode() and 0x7FFFFFFF) % PLACEHOLDER_COLORS.size]
        val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        paint.color = color
        canvas.drawCircle(sizePx / 2f, sizePx / 2f, sizePx / 2f, paint)

        val initials = getInitials(displayName)
        val textPaint = Paint(Paint.ANTI_ALIAS_FLAG)
        textPaint.color = Color.WHITE
        textPaint.textSize = sizePx * 0.4f
        textPaint.textAlign = Paint.Align.CENTER
        textPaint.isFakeBoldText = true
        val bounds = Rect()
        textPaint.getTextBounds(initials, 0, initials.length, bounds)
        val y = sizePx / 2f + bounds.height() / 2f
        canvas.drawText(initials, sizePx / 2f, y, textPaint)
        return out
    }

    private fun getInitials(name: String): String {
        if (name.isBlank()) return "?"
        val parts = name.trim().split(Regex("\\s+"))
        return when {
            parts.size >= 2 -> "${parts[0].first().uppercaseChar()}${parts[1].first().uppercaseChar()}"
            parts[0].length >= 2 -> "${parts[0][0].uppercaseChar()}${parts[0][1].lowercaseChar()}"
            else -> parts[0].first().uppercaseChar().toString()
        }
    }

    private fun dpToPx(context: Context, dp: Int): Int =
        (dp * context.resources.displayMetrics.density).toInt()

    private fun refreshPendingIntent(context: Context, appWidgetId: Int): PendingIntent {
        val intent = Intent(context, PinnedChatsWidgetProvider::class.java).apply {
            action = PinnedChatsWidgetProvider.ACTION_REFRESH
            putExtra(PinnedChatsWidgetProvider.EXTRA_APPWIDGET_ID, appWidgetId)
        }
        return PendingIntent.getBroadcast(
            context,
            appWidgetId,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
    }

    private fun openAppPendingIntent(context: Context, appWidgetId: Int): PendingIntent {
        val intent = Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        return PendingIntent.getActivity(
            context,
            appWidgetId * ROW_REQUEST_CODE_MULTIPLIER,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
    }

    private fun openChatPendingIntent(
        context: Context,
        appWidgetId: Int,
        rowIndex: Int,
        chatId: String,
        chatTitle: String
    ): PendingIntent {
        val intent = Intent(context, ChatActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
            putExtra("chat_id", chatId)
            putExtra("chat_title", chatTitle)
        }
        val requestCode = appWidgetId * ROW_REQUEST_CODE_MULTIPLIER + rowIndex + 1
        return PendingIntent.getActivity(
            context,
            requestCode,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
    }
}
