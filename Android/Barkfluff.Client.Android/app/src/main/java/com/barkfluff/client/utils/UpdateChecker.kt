package com.barkfluff.client.utils

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

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

    suspend fun hasUpdate(currentVersion: String): Boolean {
        val current = AppVersion.parse(currentVersion) ?: return false
        return if (current.isBeta) {
            val betaInfo = getVersionInfo("beta")
            val betaVersion = AppVersion.parse(betaInfo?.version)
            betaVersion != null && betaVersion > current
        } else {
            val releaseInfo = getVersionInfo("release")
            val releaseVersion = AppVersion.parse(releaseInfo?.version)
            releaseVersion != null && releaseVersion > current
        }
    }

    fun getDownloadUrl(channel: String): String {
        return "$BASE_URL/get/$CLIENT_NAME/$channel"
    }
}
