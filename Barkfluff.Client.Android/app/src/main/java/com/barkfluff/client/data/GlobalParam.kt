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

    var registrationDate: Long
        get() = sharedPreferences.getLong(KEY_REGISTRATION_DATE, 0L)
        set(value) = sharedPreferences.edit().putLong(KEY_REGISTRATION_DATE, value).apply()

    companion object {
        private const val KEY_SOCKET_BEACON = "socket_beacon"
        private const val KEY_SOCKET_USERS = "socket_users"
        private const val KEY_SOCKET_IDENTITY = "socket_identity"
        private const val KEY_SOCKET_FILES = "socket_files"
        private const val KEY_SOCKET_MESSAGES = "socket_messages"
        private const val KEY_SOCKET_UPDATES = "socket_updates"
        private const val KEY_SOCKET_ONLINER = "socket_onliner"
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
        private const val KEY_REGISTRATION_DATE = "registration_date"

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
         * Получает имя устройства (модель для Android)
         */
        fun getDeviceName(): String {
            return android.os.Build.MODEL
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
            return "BarkFluff"
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
