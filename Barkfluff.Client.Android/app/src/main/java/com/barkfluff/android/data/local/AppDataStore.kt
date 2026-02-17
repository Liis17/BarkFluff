package com.barkfluff.android.data.local

import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import com.barkfluff.android.data.model.ServerConfig
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch

class AppDataStore(
    private val dataStore: DataStore<Preferences>,
    @ApplicationScope private val scope: CoroutineScope,
) {
    companion object {
        val KEY_DEVICE_ID = stringPreferencesKey("device_id")
        val KEY_ACCESS_TOKEN = stringPreferencesKey("access_token")
        val KEY_ACCESS_TOKEN_EXPIRY = longPreferencesKey("access_token_expiry")
        val KEY_REFRESH_TOKEN = stringPreferencesKey("refresh_token")
        val KEY_SERVER_NAME = stringPreferencesKey("server_name")
        val KEY_IDENTITY_HOST = stringPreferencesKey("identity_host")
        val KEY_IDENTITY_PORT = longPreferencesKey("identity_port")
        val KEY_IDENTITY_TLS = booleanPreferencesKey("identity_tls")
        val KEY_MESSAGES_HOST = stringPreferencesKey("messages_host")
        val KEY_MESSAGES_PORT = longPreferencesKey("messages_port")
        val KEY_MESSAGES_TLS = booleanPreferencesKey("messages_tls")
        val KEY_COLOR_MAIN = stringPreferencesKey("color_main")
        val KEY_COLOR_LITE = stringPreferencesKey("color_lite")
        val KEY_COLOR_HARD = stringPreferencesKey("color_hard")
    }

    // In-memory caches for synchronous access from the gRPC interceptor
    private val _accessToken = MutableStateFlow<String?>(null)
    val accessToken: StateFlow<String?> = _accessToken.asStateFlow()

    private val _deviceId = MutableStateFlow<String?>(null)

    init {
        scope.launch {
            dataStore.data.collect { prefs ->
                _accessToken.value = prefs[KEY_ACCESS_TOKEN]
                _deviceId.value = prefs[KEY_DEVICE_ID]
            }
        }
    }

    fun getAccessTokenSync(): String? = _accessToken.value
    fun getDeviceIdSync(): String = _deviceId.value ?: ""

    suspend fun getDeviceId(): String {
        return dataStore.data.first()[KEY_DEVICE_ID] ?: ""
    }

    suspend fun saveDeviceId(deviceId: String) {
        dataStore.edit { it[KEY_DEVICE_ID] = deviceId }
    }

    suspend fun saveTokens(
        accessToken: String,
        accessTokenExpiry: Long,
        refreshToken: String,
    ) {
        dataStore.edit { prefs ->
            prefs[KEY_ACCESS_TOKEN] = accessToken
            prefs[KEY_ACCESS_TOKEN_EXPIRY] = accessTokenExpiry
            prefs[KEY_REFRESH_TOKEN] = refreshToken
        }
    }

    suspend fun saveAccessToken(accessToken: String, accessTokenExpiry: Long) {
        dataStore.edit { prefs ->
            prefs[KEY_ACCESS_TOKEN] = accessToken
            prefs[KEY_ACCESS_TOKEN_EXPIRY] = accessTokenExpiry
        }
    }

    suspend fun getAccessTokenExpiry(): Long {
        return dataStore.data.first()[KEY_ACCESS_TOKEN_EXPIRY] ?: 0L
    }

    suspend fun getRefreshToken(): String {
        return dataStore.data.first()[KEY_REFRESH_TOKEN] ?: ""
    }

    suspend fun clearTokens() {
        dataStore.edit { prefs ->
            prefs.remove(KEY_ACCESS_TOKEN)
            prefs.remove(KEY_ACCESS_TOKEN_EXPIRY)
            prefs.remove(KEY_REFRESH_TOKEN)
        }
    }

    suspend fun saveServerConfig(config: ServerConfig) {
        dataStore.edit { prefs ->
            prefs[KEY_SERVER_NAME] = config.serverName
            prefs[KEY_IDENTITY_HOST] = config.identityHost
            prefs[KEY_IDENTITY_PORT] = config.identityPort.toLong()
            prefs[KEY_IDENTITY_TLS] = config.identityTls
            prefs[KEY_MESSAGES_HOST] = config.messagesHost
            prefs[KEY_MESSAGES_PORT] = config.messagesPort.toLong()
            prefs[KEY_MESSAGES_TLS] = config.messagesTls
            prefs[KEY_COLOR_MAIN] = config.colorMain
            prefs[KEY_COLOR_LITE] = config.colorLite
            prefs[KEY_COLOR_HARD] = config.colorHard
        }
    }

    suspend fun getServerConfig(): ServerConfig? {
        val prefs = dataStore.data.first()
        val host = prefs[KEY_IDENTITY_HOST] ?: return null
        if (host.isEmpty()) return null
        return ServerConfig(
            serverName = prefs[KEY_SERVER_NAME] ?: "",
            identityHost = host,
            identityPort = (prefs[KEY_IDENTITY_PORT] ?: 443L).toInt(),
            identityTls = prefs[KEY_IDENTITY_TLS] ?: true,
            messagesHost = prefs[KEY_MESSAGES_HOST] ?: "",
            messagesPort = (prefs[KEY_MESSAGES_PORT] ?: 443L).toInt(),
            messagesTls = prefs[KEY_MESSAGES_TLS] ?: true,
            colorMain = prefs[KEY_COLOR_MAIN] ?: "#6200EE",
            colorLite = prefs[KEY_COLOR_LITE] ?: "#BB86FC",
            colorHard = prefs[KEY_COLOR_HARD] ?: "#3700B3",
        )
    }

    suspend fun getServerName(): String {
        return dataStore.data.first()[KEY_SERVER_NAME] ?: ""
    }

    suspend fun getColorMain(): String {
        return dataStore.data.first()[KEY_COLOR_MAIN] ?: "#6200EE"
    }

    suspend fun hasServerConfig(): Boolean {
        val prefs = dataStore.data.first()
        return !(prefs[KEY_IDENTITY_HOST].isNullOrEmpty())
    }

    suspend fun isLoggedIn(): Boolean {
        val prefs = dataStore.data.first()
        return !prefs[KEY_ACCESS_TOKEN].isNullOrEmpty() && !prefs[KEY_REFRESH_TOKEN].isNullOrEmpty()
    }

    suspend fun clearServerConfig() {
        dataStore.edit { prefs ->
            prefs.remove(KEY_SERVER_NAME)
            prefs.remove(KEY_IDENTITY_HOST)
            prefs.remove(KEY_IDENTITY_PORT)
            prefs.remove(KEY_IDENTITY_TLS)
            prefs.remove(KEY_MESSAGES_HOST)
            prefs.remove(KEY_MESSAGES_PORT)
            prefs.remove(KEY_MESSAGES_TLS)
            prefs.remove(KEY_COLOR_MAIN)
            prefs.remove(KEY_COLOR_LITE)
            prefs.remove(KEY_COLOR_HARD)
        }
    }
}
