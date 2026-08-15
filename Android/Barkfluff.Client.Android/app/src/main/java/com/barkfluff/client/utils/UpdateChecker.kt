package com.barkfluff.client.utils

import android.util.Log
import com.barkfluff.client.BuildConfig
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import javax.net.ssl.HttpsURLConnection

data class ChannelVersionInfo(
    val version: String?,
    val uploadedAt: String?,
    val fileName: String?
)

object UpdateChecker {

    private const val TAG = "UpdateChecker"
    private const val BASE_URL = "https://storage.barkfluff.com"
    private const val CLIENT_NAME = "barkfluffkotlin"
    private const val TIMEOUT = 10_000

    suspend fun getVersionInfo(channel: String): ChannelVersionInfo? = withContext(Dispatchers.IO) {
        try {
            val url = URL("$BASE_URL/get/$CLIENT_NAME/$channel/version")
            val connection = url.openConnection() as HttpURLConnection
            // storage.barkfluff.com за приватным CA — системному хранилищу он не известен.
            val trust = UpdateServerTls.trust
            if (trust != null && connection is HttpsURLConnection) {
                connection.sslSocketFactory = trust.socketFactory
            }
            connection.requestMethod = "GET"
            connection.connectTimeout = TIMEOUT
            connection.readTimeout = TIMEOUT

            if (connection.responseCode == 200) {
                val body = connection.inputStream.bufferedReader().readText()
                val json = JSONObject(body)
                ChannelVersionInfo(
                    version = if (json.has("version") && !json.isNull("version")) json.getString("version") else null,
                    uploadedAt = if (json.has("uploadedAt") && !json.isNull("uploadedAt")) json.getString("uploadedAt") else null,
                    fileName = if (json.has("fileName") && !json.isNull("fileName")) json.getString("fileName") else null
                )
            } else {
                Log.w(TAG, "Failed to get $channel version: ${connection.responseCode}")
                null
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error checking $channel version", e)
            null
        }
    }

    /**
     * Следим только за своим каналом: у сборок разных каналов разные applicationId,
     * поэтому APK чужого канала не обновит приложение, а встанет рядом.
     */
    suspend fun hasUpdate(currentVersion: String): Boolean {
        val current = AppVersion.parse(currentVersion) ?: return false
        val info = getVersionInfo(BuildConfig.UPDATE_CHANNEL)
        val remote = AppVersion.parse(info?.version)
        return remote != null && remote > current
    }

    fun getDownloadUrl(channel: String): String {
        return "$BASE_URL/get/$CLIENT_NAME/$channel"
    }
}
