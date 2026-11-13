package com.barkfluff.client.utils

import android.content.Context
import android.util.Log
import com.barkfluff.client.BuildConfig
import com.barkfluff.client.security.TlsTransportFactory
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.Protocol
import okhttp3.Request
import org.json.JSONObject
import java.io.IOException
import java.util.concurrent.TimeUnit

data class ChannelVersionInfo(
    val version: String?,
    val uploadedAt: String?,
    val fileName: String?
)

object UpdateChecker {

    private const val TAG = "UpdateChecker"
    private const val BASE_URL = "https://storage.barkfluff.com"
    private const val CLIENT_NAME = "barkfluffkotlin"
    private const val TIMEOUT = 10_000L
    private const val STORAGE_SERVICE_ID = 12

    suspend fun getVersionInfo(channel: String, context: Context): ChannelVersionInfo? = withContext(Dispatchers.IO) {
        val transport = TlsTransportFactory(context.applicationContext)
        try {
            if (transport.proxyEnabled) {
                getVersionInfoOnce(channel, transport, embeddedTrust = null)
            } else {
                UpdateServerTls.withFallback { trust ->
                    getVersionInfoOnce(channel, transport, trust)
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error checking $channel version", e)
            null
        }
    }

    private fun getVersionInfoOnce(
        channel: String,
        transport: TlsTransportFactory,
        embeddedTrust: UpdateServerTls.Trust?
    ): ChannelVersionInfo? {
        val url = "$BASE_URL/get/$CLIENT_NAME/$channel/version".toHttpUrlOrNull() ?: return null
        val client = transport.createOkHttpClientForService(url, STORAGE_SERVICE_ID) {
            connectTimeout(TIMEOUT, TimeUnit.MILLISECONDS)
            readTimeout(TIMEOUT, TimeUnit.MILLISECONDS)
            protocols(listOf(Protocol.HTTP_1_1))
            embeddedTrust?.let { sslSocketFactory(it.socketFactory, it.trustManager) }
        }
        val request = Request.Builder().url(url).get().build()
        val expectedGeneration = transport.transportGeneration
        val call = client.newCall(request)
        val generationListener = transport.addTransportGenerationListener { generation ->
            if (generation != expectedGeneration) call.cancel()
        }
        try {
            if (transport.transportGeneration != expectedGeneration) {
                throw IOException("Transport configuration changed")
            }
            call.execute().use { response ->
                if (response.code != 200) {
                    Log.w(TAG, "Failed to get $channel version: ${response.code}")
                    return null
                }

                val body = response.body?.string().orEmpty()
                val json = JSONObject(body)
                return ChannelVersionInfo(
                    version = if (json.has("version") && !json.isNull("version")) json.getString("version") else null,
                    uploadedAt = if (json.has("uploadedAt") && !json.isNull("uploadedAt")) json.getString("uploadedAt") else null,
                    fileName = if (json.has("fileName") && !json.isNull("fileName")) json.getString("fileName") else null
                )
            }
        } finally {
            generationListener?.close()
        }
    }

    /**
     * Следим только за своим каналом: у сборок разных каналов разные applicationId,
     * поэтому APK чужого канала не обновит приложение, а встанет рядом.
     */
    suspend fun hasUpdate(currentVersion: String, context: Context): Boolean {
        val current = AppVersion.parse(currentVersion) ?: return false
        val info = getVersionInfo(BuildConfig.UPDATE_CHANNEL, context)
        val remote = AppVersion.parse(info?.version)
        return remote != null && remote > current
    }

    fun getDownloadUrl(channel: String): String = "$BASE_URL/get/$CLIENT_NAME/$channel"
}
