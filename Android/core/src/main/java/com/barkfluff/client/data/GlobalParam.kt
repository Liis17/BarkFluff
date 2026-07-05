package com.barkfluff.client.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import com.barkfluff.client.utils.NetworkUtils
import java.security.SecureRandom
import java.util.UUID

/**
 * Глобальные параметры приложения
 * Аналог GlobalParam из WPF клиента
 */
class GlobalParam(private val context: Context) {

    val sharedPreferences: SharedPreferences by lazy {
        context.getSharedPreferences("barkfluff_prefs", Context.MODE_PRIVATE)
    }

    private val securePreferences: SharedPreferences by lazy {
        val masterKeyAlias = MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC)
        EncryptedSharedPreferences.create(
            "barkfluff_secure_prefs",
            masterKeyAlias,
            context,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    // Приложение
    var socketBeacon: String
        get() = sharedPreferences.getString(KEY_SOCKET_BEACON, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_BEACON, value).apply()

    var socketUsers: String
        get() = sharedPreferences.getString(KEY_SOCKET_USERS, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_USERS, value).apply()

    var socketIdentity: String
        get() = sharedPreferences.getString(KEY_SOCKET_IDENTITY, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_IDENTITY, value).apply()

    var socketFiles: String
        get() = sharedPreferences.getString(KEY_SOCKET_FILES, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_FILES, value).apply()

    var socketMessages: String
        get() = sharedPreferences.getString(KEY_SOCKET_MESSAGES, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_MESSAGES, value).apply()

    var socketUpdates: String
        get() = sharedPreferences.getString(KEY_SOCKET_UPDATES, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_UPDATES, value).apply()

    var socketOnliner: String
        get() = sharedPreferences.getString(KEY_SOCKET_ONLINER, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_ONLINER, value).apply()

    var socketFastAuth: String
        get() = sharedPreferences.getString(KEY_SOCKET_FAST_AUTH, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_FAST_AUTH, value).apply()

    var socketCalls: String
        get() = sharedPreferences.getString(KEY_SOCKET_CALLS, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SOCKET_CALLS, value).apply()

    var livekitUrl: String
        get() = sharedPreferences.getString(KEY_LIVEKIT_URL, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_LIVEKIT_URL, value).apply()

    var serverName: String
        get() = sharedPreferences.getString(KEY_SERVER_NAME, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SERVER_NAME, value).apply()

    var serverDescription: String
        get() = sharedPreferences.getString(KEY_SERVER_DESCRIPTION, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_SERVER_DESCRIPTION, value).apply()

    var deviceId: String
        get() {
            var id = sharedPreferences.getString(KEY_DEVICE_ID, null)
            if (id == null) {
                id = UUID.randomUUID().toString()
                sharedPreferences.edit().putString(KEY_DEVICE_ID, id).apply()
            }
            return id
        }
        set(value) = sharedPreferences.edit().putString(KEY_DEVICE_ID, value).apply()

    var machineName: String
        get() = sharedPreferences.getString(KEY_MACHINE_NAME, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_MACHINE_NAME, value).apply()

    var ipAddress: String
        get() = sharedPreferences.getString(KEY_IP_ADDRESS, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_IP_ADDRESS, value).apply()

    var colors: ClientColors
        get() {
            val lite = sharedPreferences.getString(KEY_COLOR_LITE, "") ?: ""
            val main = sharedPreferences.getString(KEY_COLOR_MAIN, "") ?: ""
            val hard = sharedPreferences.getString(KEY_COLOR_HARD, "") ?: ""
            return ClientColors(lite, main, hard)
        }
        set(value) {
            sharedPreferences.edit().apply {
                putString(KEY_COLOR_LITE, value.liteHex)
                putString(KEY_COLOR_MAIN, value.mainHex)
                putString(KEY_COLOR_HARD, value.hardHex)
            }.apply()
        }

    // Пользователь — токены хранятся в зашифрованном хранилище
    var refreshToken: String?
        get() = securePreferences.getString(KEY_REFRESH_TOKEN, null)
        set(value) = securePreferences.edit().putString(KEY_REFRESH_TOKEN, value).apply()

    var accessToken: String?
        get() = securePreferences.getString(KEY_ACCESS_TOKEN, null)
        set(value) = securePreferences.edit().putString(KEY_ACCESS_TOKEN, value).apply()

    var accessTokenExpiration: Long
        get() = securePreferences.getLong(KEY_ACCESS_TOKEN_EXPIRATION, 0L)
        set(value) = securePreferences.edit().putLong(KEY_ACCESS_TOKEN_EXPIRATION, value).apply()

    var refreshTokenExpiration: Long
        get() = securePreferences.getLong(KEY_REFRESH_TOKEN_EXPIRATION, 0L)
        set(value) = securePreferences.edit().putLong(KEY_REFRESH_TOKEN_EXPIRATION, value).apply()

    var userId: Long
        get() = sharedPreferences.getLong(KEY_USER_ID, 0L)
        set(value) = sharedPreferences.edit().putLong(KEY_USER_ID, value).apply()

    var userName: String
        get() = sharedPreferences.getString(KEY_USER_NAME, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_USER_NAME, value).apply()

    var firstName: String
        get() = sharedPreferences.getString(KEY_FIRST_NAME, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_FIRST_NAME, value).apply()

    var lastName: String
        get() = sharedPreferences.getString(KEY_LAST_NAME, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_LAST_NAME, value).apply()

    var description: String
        get() = sharedPreferences.getString(KEY_DESCRIPTION, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_DESCRIPTION, value).apply()

    var email: String
        get() = sharedPreferences.getString(KEY_EMAIL, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_EMAIL, value).apply()

    var pictureId: String
        get() = sharedPreferences.getString(KEY_PICTURE_ID, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_PICTURE_ID, value).apply()

    var pictureFileId: String
        get() = sharedPreferences.getString(KEY_PICTURE_FILE_ID, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_PICTURE_FILE_ID, value).apply()

    var picturePreviewFileId: String
        get() = sharedPreferences.getString(KEY_PICTURE_PREVIEW_FILE_ID, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_PICTURE_PREVIEW_FILE_ID, value).apply()

    var pictureUrl: String
        get() = sharedPreferences.getString(KEY_PICTURE_URL, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_PICTURE_URL, value).apply()

    var picturePreviewUrl: String
        get() = sharedPreferences.getString(KEY_PICTURE_PREVIEW_URL, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_PICTURE_PREVIEW_URL, value).apply()

    var profilePictureUrl: String
        get() = sharedPreferences.getString(KEY_PROFILE_PICTURE_URL, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_PROFILE_PICTURE_URL, value).apply()

    var registrationDate: Long
        get() = sharedPreferences.getLong(KEY_REGISTRATION_DATE, 0L)
        set(value) = sharedPreferences.edit().putLong(KEY_REGISTRATION_DATE, value).apply()

    var notificationsEnabled: Boolean
        get() = sharedPreferences.getBoolean(KEY_NOTIFICATIONS_ENABLED, true)
        set(value) = sharedPreferences.edit().putBoolean(KEY_NOTIFICATIONS_ENABLED, value).apply()

    var firebaseToken: String
        get() = sharedPreferences.getString(KEY_FIREBASE_TOKEN, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_FIREBASE_TOKEN, value).apply()

    /** Локальный кэш chatId замьюченных чатов — guard для подавления локальных уведомлений. */
    var mutedChatIds: Set<String>
        get() = sharedPreferences.getStringSet(KEY_MUTED_CHAT_IDS, emptySet()) ?: emptySet()
        set(value) = sharedPreferences.edit().putStringSet(KEY_MUTED_CHAT_IDS, value).apply()

    /** Добавить/убрать один чат из локального кэша mute. */
    fun setChatMutedLocal(chatId: String, muted: Boolean) {
        val current = mutedChatIds.toMutableSet()
        if (muted) current.add(chatId) else current.remove(chatId)
        mutedChatIds = current
    }

    // --- Персонализация (локальные параметры) ---

    /** Закругление пузырей сообщений, 0..30 (dp). По умолчанию 20. */
    var chatMessageCornerRadius: Int
        get() = sharedPreferences.getInt(KEY_CHAT_CORNER_RADIUS, 20)
        set(value) = sharedPreferences.edit().putInt(KEY_CHAT_CORNER_RADIUS, value).apply()

    /** FileId выбранного фона чата (пусто = нет фона). */
    var chatBackgroundFileId: String
        get() = sharedPreferences.getString(KEY_CHAT_BACKGROUND_FILE_ID, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_CHAT_BACKGROUND_FILE_ID, value).apply()

    /** Применять ли блюр к фону чата. */
    var chatBackgroundBlur: Boolean
        get() = sharedPreferences.getBoolean(KEY_CHAT_BACKGROUND_BLUR, false)
        set(value) = sharedPreferences.edit().putBoolean(KEY_CHAT_BACKGROUND_BLUR, value).apply()

    /** Сила размытия фона чата, 0..25 (в dp-единицах для RenderEffect). По умолчанию 10. */
    var chatBackgroundBlurRadius: Int
        get() = sharedPreferences.getInt(KEY_CHAT_BACKGROUND_BLUR_RADIUS, 10)
        set(value) = sharedPreferences.edit().putInt(KEY_CHAT_BACKGROUND_BLUR_RADIUS, value).apply()

    /** Затемнение фона чата, 0..100 (%). 0 = прозрачно, 100 = чёрный. По умолчанию 0. */
    var chatBackgroundDim: Int
        get() = sharedPreferences.getInt(KEY_CHAT_BACKGROUND_DIM, 0)
        set(value) = sharedPreferences.edit().putInt(KEY_CHAT_BACKGROUND_DIM, value).apply()

    /** Компактные папки: показывать только иконку без имени. По умолчанию false. */
    var compactFolders: Boolean
        get() = sharedPreferences.getBoolean(KEY_COMPACT_FOLDERS, false)
        set(value) = sharedPreferences.edit().putBoolean(KEY_COMPACT_FOLDERS, value).apply()

    /** Убирать обводку у неактивных вкладок папок. По умолчанию false. */
    var folderTabsNoOutline: Boolean
        get() = sharedPreferences.getBoolean(KEY_FOLDER_TABS_NO_OUTLINE, false)
        set(value) = sharedPreferences.edit().putBoolean(KEY_FOLDER_TABS_NO_OUTLINE, value).apply()

    /** Убирать чаты, входящие хотя бы в одну папку, из вкладки «Все чаты». По умолчанию false. */
    var excludeFolderChatsFromAll: Boolean
        get() = sharedPreferences.getBoolean(KEY_EXCLUDE_FOLDER_CHATS_FROM_ALL, false)
        set(value) = sharedPreferences.edit().putBoolean(KEY_EXCLUDE_FOLDER_CHATS_FROM_ALL, value).apply()

    /** Язык приложения: "system" (по умолчанию) — использовать локаль устройства, "ru" / "en" / "de" / "es" / "zh-CN" — переопределение. */
    var appLanguage: String
        get() = sharedPreferences.getString(KEY_APP_LANGUAGE, LANGUAGE_SYSTEM) ?: LANGUAGE_SYSTEM
        set(value) = sharedPreferences.edit().putString(KEY_APP_LANGUAGE, value).apply()

    // --- Тестирование (dev/QA-флаги) ---

    /** Показывать UserId/ChatId в карточке профиля собеседника. */
    var showIdsInProfile: Boolean
        get() = sharedPreferences.getBoolean(KEY_TESTING_SHOW_IDS, false)
        set(value) = sharedPreferences.edit().putBoolean(KEY_TESTING_SHOW_IDS, value).apply()

    /** Показывать кнопку создания скрытых (приватных/секретных) чатов в шапке списка чатов. */
    var secretChatsEnabled: Boolean
        get() = sharedPreferences.getBoolean(KEY_TESTING_SECRET_CHATS, false)
        set(value) = sharedPreferences.edit().putBoolean(KEY_TESTING_SECRET_CHATS, value).apply()

    // --- Отложенная очистка APK обновления ---

    /** Абсолютный путь к скачанному APK обновления, который нужно удалить после установки. */
    var pendingUpdateApkPath: String?
        get() = sharedPreferences.getString(KEY_PENDING_UPDATE_APK_PATH, null)
        set(value) = sharedPreferences.edit().putString(KEY_PENDING_UPDATE_APK_PATH, value).apply()

    /** DownloadManager ID последней загрузки обновления (для DownloadManager.remove). -1 = нет. */
    var pendingUpdateDownloadId: Long
        get() = sharedPreferences.getLong(KEY_PENDING_UPDATE_DOWNLOAD_ID, -1L)
        set(value) = sharedPreferences.edit().putLong(KEY_PENDING_UPDATE_DOWNLOAD_ID, value).apply()

    /** Сбрасывает оба параметра отложенной очистки APK. */
    fun clearPendingUpdate() {
        sharedPreferences.edit()
            .remove(KEY_PENDING_UPDATE_APK_PATH)
            .remove(KEY_PENDING_UPDATE_DOWNLOAD_ID)
            .apply()
    }

    /**
     * Очищает все данные аккаунта при разлогине.
     * Сохраняет только адреса сервера (socket_*, server_name, server_description).
     * device_id удаляется — пересоздаётся автоматически при следующем запросе deviceId.
     */
    fun clearUserData() {
        // Сохраняем адреса сервера
        val beacon = socketBeacon
        val users = socketUsers
        val identity = socketIdentity
        val files = socketFiles
        val messages = socketMessages
        val updates = socketUpdates
        val onliner = socketOnliner
        val fastAuth = socketFastAuth
        val calls = socketCalls
        val livekit = livekitUrl
        val serverNameVal = serverName
        val serverDescVal = serverDescription
        val language = appLanguage

        // Полная очистка sharedPreferences
        sharedPreferences.edit().clear().apply()

        // Восстанавливаем только адреса сервера и выбранный язык приложения
        sharedPreferences.edit().apply {
            if (beacon.isNotBlank()) putString(KEY_SOCKET_BEACON, beacon)
            if (users.isNotBlank()) putString(KEY_SOCKET_USERS, users)
            if (identity.isNotBlank()) putString(KEY_SOCKET_IDENTITY, identity)
            if (files.isNotBlank()) putString(KEY_SOCKET_FILES, files)
            if (messages.isNotBlank()) putString(KEY_SOCKET_MESSAGES, messages)
            if (updates.isNotBlank()) putString(KEY_SOCKET_UPDATES, updates)
            if (onliner.isNotBlank()) putString(KEY_SOCKET_ONLINER, onliner)
            if (fastAuth.isNotBlank()) putString(KEY_SOCKET_FAST_AUTH, fastAuth)
            if (calls.isNotBlank()) putString(KEY_SOCKET_CALLS, calls)
            if (livekit.isNotBlank()) putString(KEY_LIVEKIT_URL, livekit)
            if (serverNameVal.isNotBlank()) putString(KEY_SERVER_NAME, serverNameVal)
            if (serverDescVal.isNotBlank()) putString(KEY_SERVER_DESCRIPTION, serverDescVal)
            putString(KEY_APP_LANGUAGE, language)
        }.apply()

        // Полная очистка зашифрованных настроек (токены)
        securePreferences.edit().clear().apply()
    }

    companion object {
        private const val KEY_SOCKET_BEACON = "socket_beacon"
        private const val KEY_SOCKET_USERS = "socket_users"
        private const val KEY_SOCKET_IDENTITY = "socket_identity"
        private const val KEY_SOCKET_FILES = "socket_files"
        private const val KEY_SOCKET_MESSAGES = "socket_messages"
        private const val KEY_SOCKET_UPDATES = "socket_updates"
        private const val KEY_SOCKET_ONLINER = "socket_onliner"
        private const val KEY_SOCKET_FAST_AUTH = "socket_fast_auth"
        private const val KEY_SOCKET_CALLS = "socket_calls"
        private const val KEY_LIVEKIT_URL = "livekit_url"
        private const val KEY_SERVER_NAME = "server_name"
        private const val KEY_SERVER_DESCRIPTION = "server_description"
        private const val KEY_DEVICE_ID = "device_id"
        private const val KEY_MACHINE_NAME = "machine_name"
        private const val KEY_IP_ADDRESS = "ip_address"
        private const val KEY_COLOR_LITE = "color_lite"
        private const val KEY_COLOR_MAIN = "color_main"
        private const val KEY_COLOR_HARD = "color_hard"

        private const val KEY_REFRESH_TOKEN = "refresh_token"
        private const val KEY_ACCESS_TOKEN = "access_token"
        private const val KEY_ACCESS_TOKEN_EXPIRATION = "access_token_expiration"
        private const val KEY_REFRESH_TOKEN_EXPIRATION = "refresh_token_expiration"
        private const val KEY_USER_ID = "user_id"
        private const val KEY_USER_NAME = "user_name"
        private const val KEY_FIRST_NAME = "first_name"
        private const val KEY_LAST_NAME = "last_name"
        private const val KEY_DESCRIPTION = "description"
        private const val KEY_EMAIL = "email"
        private const val KEY_PICTURE_ID = "picture_id"
        private const val KEY_PICTURE_FILE_ID = "picture_file_id"
        private const val KEY_PICTURE_PREVIEW_FILE_ID = "picture_preview_file_id"
        private const val KEY_PICTURE_URL = "picture_url"
        private const val KEY_PICTURE_PREVIEW_URL = "picture_preview_url"
        private const val KEY_PROFILE_PICTURE_URL = "profile_picture_url"
        private const val KEY_REGISTRATION_DATE = "registration_date"
        private const val KEY_NOTIFICATIONS_ENABLED = "notifications_enabled"
        private const val KEY_FIREBASE_TOKEN = "firebase_token"
        private const val KEY_MUTED_CHAT_IDS = "muted_chat_ids"

        // Персонализация
        private const val KEY_CHAT_CORNER_RADIUS = "chat_corner_radius"
        private const val KEY_CHAT_BACKGROUND_FILE_ID = "chat_background_file_id"
        private const val KEY_CHAT_BACKGROUND_BLUR = "chat_background_blur"
        private const val KEY_CHAT_BACKGROUND_BLUR_RADIUS = "chat_background_blur_radius"
        private const val KEY_CHAT_BACKGROUND_DIM = "chat_background_dim"
        private const val KEY_COMPACT_FOLDERS = "folders_compact"
        private const val KEY_FOLDER_TABS_NO_OUTLINE = "folders_no_outline"
        private const val KEY_EXCLUDE_FOLDER_CHATS_FROM_ALL = "folders_exclude_from_all"

        // Тестирование
        private const val KEY_TESTING_SHOW_IDS = "testing_show_ids_in_profile"
        private const val KEY_TESTING_SECRET_CHATS = "testing_secret_chats_enabled"

        // Отложенная очистка APK обновления
        private const val KEY_PENDING_UPDATE_APK_PATH = "pending_update_apk_path"
        private const val KEY_PENDING_UPDATE_DOWNLOAD_ID = "pending_update_download_id"

        // Язык приложения
        private const val KEY_APP_LANGUAGE = "app_language"
        const val LANGUAGE_SYSTEM = "system"
        const val LANGUAGE_RU = "ru"
        const val LANGUAGE_EN = "en"
        const val LANGUAGE_DE = "de"
        const val LANGUAGE_ES = "es"
        const val LANGUAGE_ZH = "zh-CN"

        /**
         * Генерирует уникальный ID устройства
         */
        fun generateDeviceId(): String {
            val random = SecureRandom()
            val bytes = ByteArray(16)
            random.nextBytes(bytes)
            return bytes.joinToString("") { "%02x".format(it) }
        }

        /**
         * Получает имя устройства
         * Возвращает производителя и модель устройства
         */
        fun getDeviceName(): String {
            val manufacturer = android.os.Build.MANUFACTURER
            val model = android.os.Build.MODEL
            return "$manufacturer $model"
        }

        /**
         * Получает версию ОС
         */
        fun getOsVersion(): String {
            return "Android ${android.os.Build.VERSION.RELEASE}"
        }

        /**
         * Получает имя приложения
         */
        fun getAppName(): String {
            return "Barkfluff Kotlin"
        }

        /**
         * Получает версию приложения
         */
        fun getAppVersion(context: Context): String {
            return try {
                val packageInfo = context.packageManager.getPackageInfo(context.packageName, 0)
                packageInfo.versionName ?: "1.0.0"
            } catch (e: Exception) {
                "1.0.0"
            }
        }

        /**
         * Загружает внешний IP-адрес и сохраняет в хранилище
         * Вызывается при старте приложения для обновления IP
         */
        suspend fun loadIpAddress(sharedPreferences: SharedPreferences) {
            val currentIp = sharedPreferences.getString(KEY_IP_ADDRESS, "") ?: ""
            if (currentIp.isBlank()) {
                // Если IP еще не сохранен, загружаем его
                val externalIp = NetworkUtils.getExternalIp()
                if (externalIp.isNotBlank()) {
                    sharedPreferences.edit().putString(KEY_IP_ADDRESS, externalIp).apply()
                }
            }
            // Если IP уже сохранен, используем его (не обновляем лишний раз)
        }
    }
}
