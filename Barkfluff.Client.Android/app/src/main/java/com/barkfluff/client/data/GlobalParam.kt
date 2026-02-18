package com.barkfluff.client.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import java.security.SecureRandom
import java.util.UUID

/**
 * Глобальные параметры приложения
 * Аналог GlobalParam из WPF клиента
 */
class GlobalParam(private val context: Context) {
    
    private val sharedPreferences: SharedPreferences by lazy {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()
        
        EncryptedSharedPreferences.create(
            context,
            "barkfluff_prefs",
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }
    
    private val insecureSharedPreferences: SharedPreferences by lazy {
        context.getSharedPreferences("barkfluff_insecure", Context.MODE_PRIVATE)
    }
    
    // Приложение
    var socketBeacon: String
        get() = insecureSharedPreferences.getString(KEY_SOCKET_BEACON, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SOCKET_BEACON, value).apply()
    
    var socketUsers: String
        get() = insecureSharedPreferences.getString(KEY_SOCKET_USERS, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SOCKET_USERS, value).apply()
    
    var socketIdentity: String
        get() = insecureSharedPreferences.getString(KEY_SOCKET_IDENTITY, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SOCKET_IDENTITY, value).apply()
    
    var socketFiles: String
        get() = insecureSharedPreferences.getString(KEY_SOCKET_FILES, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SOCKET_FILES, value).apply()
    
    var socketMessages: String
        get() = insecureSharedPreferences.getString(KEY_SOCKET_MESSAGES, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SOCKET_MESSAGES, value).apply()
    
    var socketUpdates: String
        get() = insecureSharedPreferences.getString(KEY_SOCKET_UPDATES, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SOCKET_UPDATES, value).apply()
    
    var socketOnliner: String
        get() = insecureSharedPreferences.getString(KEY_SOCKET_ONLINER, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SOCKET_ONLINER, value).apply()
    
    var serverName: String
        get() = insecureSharedPreferences.getString(KEY_SERVER_NAME, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SERVER_NAME, value).apply()
    
    var serverDescription: String
        get() = insecureSharedPreferences.getString(KEY_SERVER_DESCRIPTION, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_SERVER_DESCRIPTION, value).apply()
    
    var deviceId: String
        get() {
            var id = insecureSharedPreferences.getString(KEY_DEVICE_ID, null)
            if (id == null) {
                id = UUID.randomUUID().toString()
                insecureSharedPreferences.edit().putString(KEY_DEVICE_ID, id).apply()
            }
            return id
        }
        set(value) = insecureSharedPreferences.edit().putString(KEY_DEVICE_ID, value).apply()
    
    var machineName: String
        get() = insecureSharedPreferences.getString(KEY_MACHINE_NAME, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_MACHINE_NAME, value).apply()
    
    var ipAddress: String
        get() = insecureSharedPreferences.getString(KEY_IP_ADDRESS, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_IP_ADDRESS, value).apply()
    
    var colors: ClientColors
        get() {
            val lite = insecureSharedPreferences.getString(KEY_COLOR_LITE, "") ?: ""
            val main = insecureSharedPreferences.getString(KEY_COLOR_MAIN, "") ?: ""
            val hard = insecureSharedPreferences.getString(KEY_COLOR_HARD, "") ?: ""
            return ClientColors(lite, main, hard)
        }
        set(value) {
            insecureSharedPreferences.edit().apply {
                putString(KEY_COLOR_LITE, value.liteHex)
                putString(KEY_COLOR_MAIN, value.mainHex)
                putString(KEY_COLOR_HARD, value.hardHex)
            }.apply()
        }
    
    // Пользователь
    var refreshToken: String?
        get() = sharedPreferences.getString(KEY_REFRESH_TOKEN, null)
        set(value) = sharedPreferences.edit().putString(KEY_REFRESH_TOKEN, value).apply()
    
    var accessToken: String?
        get() = sharedPreferences.getString(KEY_ACCESS_TOKEN, null)
        set(value) = sharedPreferences.edit().putString(KEY_ACCESS_TOKEN, value).apply()
    
    var userId: Long
        get() = insecureSharedPreferences.getLong(KEY_USER_ID, 0L)
        set(value) = insecureSharedPreferences.edit().putLong(KEY_USER_ID, value).apply()
    
    var userName: String
        get() = insecureSharedPreferences.getString(KEY_USER_NAME, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_USER_NAME, value).apply()
    
    var firstName: String
        get() = insecureSharedPreferences.getString(KEY_FIRST_NAME, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_FIRST_NAME, value).apply()
    
    var lastName: String
        get() = insecureSharedPreferences.getString(KEY_LAST_NAME, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_LAST_NAME, value).apply()
    
    var description: String
        get() = insecureSharedPreferences.getString(KEY_DESCRIPTION, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_DESCRIPTION, value).apply()
    
    var email: String
        get() = insecureSharedPreferences.getString(KEY_EMAIL, "") ?: ""
        set(value) = insecureSharedPreferences.edit().putString(KEY_EMAIL, value).apply()
    
    var appPin: String
        get() = sharedPreferences.getString(KEY_APP_PIN, "") ?: ""
        set(value) = sharedPreferences.edit().putString(KEY_APP_PIN, value).apply()
    
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
        private const val KEY_USER_ID = "user_id"
        private const val KEY_USER_NAME = "user_name"
        private const val KEY_FIRST_NAME = "first_name"
        private const val KEY_LAST_NAME = "last_name"
        private const val KEY_DESCRIPTION = "description"
        private const val KEY_EMAIL = "email"
        private const val KEY_APP_PIN = "app_pin"
        
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
    }
}
