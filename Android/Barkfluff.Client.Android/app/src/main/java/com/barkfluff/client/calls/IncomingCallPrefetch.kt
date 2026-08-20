package com.barkfluff.client.calls

import android.content.Context
import android.graphics.Bitmap
import android.graphics.drawable.BitmapDrawable
import android.util.Log
import coil.request.ImageRequest
import coil.size.Size
import coil.transform.CircleCropTransformation
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.utils.AvatarLoader
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.concurrent.ConcurrentHashMap

/**
 * Готовит аватар звонящего до показа входящего звонка.
 *
 * При убитом приложении процесс поднимает FCM, а основной флоу инициализации gRPC-клиентов
 * (Splash/Login/Main) не выполняется — поэтому клиенты в GrpcManager теперь поднимаются
 * лениво при первом чтении свойств (по адресам из GlobalParam). Здесь остаётся только
 * проверка токена, скачивание аватара в кэш Coil и готовый Bitmap в [avatarBitmaps] —
 * оттуда его берут нотификация и IncomingCallActivity.
 */
object IncomingCallPrefetch {

    private const val TAG = "IncomingCallPrefetch"

    private val GUID_REGEX = Regex(
        "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
        RegexOption.IGNORE_CASE
    )

    private val avatarBitmaps = ConcurrentHashMap<String, Bitmap>()

    fun avatarBitmap(callId: String): Bitmap? = avatarBitmaps[callId]

    fun clear(callId: String) {
        avatarBitmaps.remove(callId)
    }

    /**
     * Обновляет токен и проверяет доступность files-клиента.
     * Клиенты поднимаются лениво самими свойствами GrpcManager при чтении.
     *
     * @return true если files-клиент доступен (по нему запрашивается URL аватара)
     */
    suspend fun ensureClients(context: Context): Boolean = withContext(Dispatchers.IO) {
        val appContext = context.applicationContext
        val grpcManager = (appContext as BarkFluffApplication).grpcManager
        val globalParam = GlobalParam(appContext)

        if (globalParam.refreshToken.isNullOrBlank()) {
            Log.d(TAG, "ensureClients: пользователь не авторизован")
            return@withContext false
        }
        if (!grpcManager.ensureTokenValid(appContext)) {
            Log.w(TAG, "ensureClients: не удалось обновить токен")
            return@withContext false
        }

        grpcManager.filesClient != null
    }

    /**
     * Загружает аватар звонящего: push-поле avatar_url, иначе профиль по userId.
     * Результат сохраняется по callId и переиспользуется нотификацией и экраном звонка.
     */
    suspend fun prepareAvatar(
        context: Context,
        callId: String,
        callerUserId: Long,
        rawAvatarUrl: String?
    ) {
        if (callId.isBlank()) return
        if (!ensureClients(context)) return

        val rawAvatar = rawAvatarUrl?.takeIf { it.isNotBlank() }
            ?: fetchAvatarFromProfile(context, callerUserId)
            ?: return

        val cacheKey = fileIdOf(rawAvatar) ?: rawAvatar
        val avatarUrl = resolveAvatarUrl(context, rawAvatar) ?: return
        val bitmap = loadAvatarBitmap(context, avatarUrl, cacheKey) ?: return

        avatarBitmaps[callId] = bitmap
        Log.d(TAG, "prepareAvatar: аватар готов до показа звонка, callId=$callId")
    }

    /**
     * Достаёт fileId аватара из профиля — fallback когда сервер не прислал avatar_url в push.
     */
    private suspend fun fetchAvatarFromProfile(context: Context, callerUserId: Long): String? {
        if (callerUserId <= 0L) return null

        val grpcManager = (context.applicationContext as BarkFluffApplication).grpcManager
        val user = grpcManager.getUserData(callerUserId).getOrNull() ?: return null
        return user.profilePicturePreviewFileId.takeIf { it.isNotBlank() }
            ?: user.profilePictureFileId.takeIf { it.isNotBlank() }
    }

    /**
     * Превращает fileId (или URL с fileId) во временный URL для скачивания, используя кэши.
     */
    private suspend fun resolveAvatarUrl(context: Context, rawAvatar: String): String? {
        val fileId = fileIdOf(rawAvatar) ?: return rawAvatar

        AvatarLoader.urlCache[fileId]?.let { return it }
        AvatarLoader.getUrlFromCache(fileId)?.let {
            AvatarLoader.urlCache[fileId] = it
            return it
        }

        val grpcManager = (context.applicationContext as BarkFluffApplication).grpcManager
        val url = grpcManager.getFileDownloadUrl(fileId).getOrNull() ?: return null
        AvatarLoader.urlCache[fileId] = url
        AvatarLoader.putUrlInCache(fileId, url)
        return url
    }

    /**
     * @return fileId, либо null если это прямая ссылка без fileId в пути
     */
    private fun fileIdOf(rawAvatar: String): String? {
        if (!rawAvatar.startsWith("http://") && !rawAvatar.startsWith("https://")) {
            return rawAvatar
        }
        val lastSegment = rawAvatar.substringBefore('?').substringAfterLast('/')
        return lastSegment.takeIf { GUID_REGEX.matches(it) }
    }

    private suspend fun loadAvatarBitmap(context: Context, avatarUrl: String, cacheKey: String): Bitmap? {
        return try {
            val request = ImageRequest.Builder(context.applicationContext)
                .data(avatarUrl)
                .memoryCacheKey(cacheKey)
                .diskCacheKey(cacheKey)
                .transformations(CircleCropTransformation())
                .size(Size.ORIGINAL)
                // Bitmap уходит в нотификацию, а hardware-битмапы там не поддерживаются
                .allowHardware(false)
                .build()

            val result = AvatarLoader.getImageLoader(context.applicationContext).execute(request)
            (result.drawable as? BitmapDrawable)?.bitmap
        } catch (e: Exception) {
            Log.w(TAG, "loadAvatarBitmap: не удалось загрузить аватар", e)
            null
        }
    }
}
