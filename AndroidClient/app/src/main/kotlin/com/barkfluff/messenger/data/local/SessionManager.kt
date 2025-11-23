package com.barkfluff.messenger.data.local

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.barkfluff.messenger.domain.model.AuthToken
import com.barkfluff.messenger.domain.model.ServerInfo
import com.barkfluff.messenger.domain.model.ServerColor
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import java.time.Instant
import javax.inject.Inject
import javax.inject.Singleton

val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "session_prefs")

@Singleton
class SessionManager @Inject constructor(
    @ApplicationContext private val context: Context
) {
    private val dataStore = context.dataStore

    companion object {
        private val ACCESS_TOKEN = stringPreferencesKey("access_token")
        private val REFRESH_TOKEN = stringPreferencesKey("refresh_token")
        private val ACCESS_TOKEN_EXPIRATION = longPreferencesKey("access_token_expiration")
        private val REFRESH_TOKEN_EXPIRATION = longPreferencesKey("refresh_token_expiration")
        private val USER_ID = longPreferencesKey("user_id")

        private val SERVER_NAME = stringPreferencesKey("server_name")
        private val SERVER_DESCRIPTION = stringPreferencesKey("server_description")
        private val SERVER_COLOR_LITE = stringPreferencesKey("server_color_lite")
        private val SERVER_COLOR_MAIN = stringPreferencesKey("server_color_main")
        private val SERVER_COLOR_HARD = stringPreferencesKey("server_color_hard")

        private val BEACON_ENDPOINT = stringPreferencesKey("beacon_endpoint")
        private val IDENTITY_ENDPOINT = stringPreferencesKey("identity_endpoint")
        private val USERS_ENDPOINT = stringPreferencesKey("users_endpoint")
        private val FILES_ENDPOINT = stringPreferencesKey("files_endpoint")
        private val MESSAGES_ENDPOINT = stringPreferencesKey("messages_endpoint")
        private val UPDATES_ENDPOINT = stringPreferencesKey("updates_endpoint")
    }

    suspend fun saveToken(token: AuthToken) {
        dataStore.edit { prefs ->
            prefs[ACCESS_TOKEN] = token.accessToken
            prefs[REFRESH_TOKEN] = token.refreshToken
            prefs[ACCESS_TOKEN_EXPIRATION] = token.accessTokenExpiration.epochSecond
            prefs[REFRESH_TOKEN_EXPIRATION] = token.refreshTokenExpiration.epochSecond
        }
    }

    suspend fun saveServerInfo(serverInfo: ServerInfo) {
        dataStore.edit { prefs ->
            prefs[SERVER_NAME] = serverInfo.name
            prefs[SERVER_DESCRIPTION] = serverInfo.description
            prefs[SERVER_COLOR_LITE] = serverInfo.color.liteHex
            prefs[SERVER_COLOR_MAIN] = serverInfo.color.mainHex
            prefs[SERVER_COLOR_HARD] = serverInfo.color.hardHex
            prefs[BEACON_ENDPOINT] = serverInfo.beaconEndpoint
            prefs[IDENTITY_ENDPOINT] = serverInfo.identityEndpoint
            prefs[USERS_ENDPOINT] = serverInfo.usersEndpoint
            prefs[FILES_ENDPOINT] = serverInfo.filesEndpoint
            prefs[MESSAGES_ENDPOINT] = serverInfo.messagesEndpoint
            prefs[UPDATES_ENDPOINT] = serverInfo.updatesEndpoint
        }
    }

    suspend fun saveUserId(userId: Long) {
        dataStore.edit { prefs ->
            prefs[USER_ID] = userId
        }
    }

    val authToken: Flow<AuthToken?> = dataStore.data.map { prefs ->
        val accessToken = prefs[ACCESS_TOKEN]
        val refreshToken = prefs[REFRESH_TOKEN]
        val accessExp = prefs[ACCESS_TOKEN_EXPIRATION]
        val refreshExp = prefs[REFRESH_TOKEN_EXPIRATION]

        if (accessToken != null && refreshToken != null && accessExp != null && refreshExp != null) {
            AuthToken(
                accessToken = accessToken,
                refreshToken = refreshToken,
                accessTokenExpiration = Instant.ofEpochSecond(accessExp),
                refreshTokenExpiration = Instant.ofEpochSecond(refreshExp)
            )
        } else {
            null
        }
    }

    val serverInfo: Flow<ServerInfo?> = dataStore.data.map { prefs ->
        val name = prefs[SERVER_NAME]
        val description = prefs[SERVER_DESCRIPTION]
        val colorLite = prefs[SERVER_COLOR_LITE]
        val colorMain = prefs[SERVER_COLOR_MAIN]
        val colorHard = prefs[SERVER_COLOR_HARD]
        val beacon = prefs[BEACON_ENDPOINT]
        val identity = prefs[IDENTITY_ENDPOINT]
        val users = prefs[USERS_ENDPOINT]
        val files = prefs[FILES_ENDPOINT]
        val messages = prefs[MESSAGES_ENDPOINT]
        val updates = prefs[UPDATES_ENDPOINT]

        if (name != null && description != null && colorLite != null &&
            colorMain != null && colorHard != null && beacon != null &&
            identity != null && users != null && files != null &&
            messages != null && updates != null) {
            ServerInfo(
                name = name,
                description = description,
                color = ServerColor(colorLite, colorMain, colorHard),
                beaconEndpoint = beacon,
                identityEndpoint = identity,
                usersEndpoint = users,
                filesEndpoint = files,
                messagesEndpoint = messages,
                updatesEndpoint = updates
            )
        } else {
            null
        }
    }

    val userId: Flow<Long?> = dataStore.data.map { prefs ->
        prefs[USER_ID]
    }

    suspend fun clearSession() {
        dataStore.edit { it.clear() }
    }
}
