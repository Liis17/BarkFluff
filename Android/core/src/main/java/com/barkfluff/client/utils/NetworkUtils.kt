package com.barkfluff.client.utils

import android.util.Log
import okhttp3.OkHttpClient
import okhttp3.Request
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.concurrent.TimeUnit
import java.util.regex.Pattern

/**
 * Утилиты для работы с сетью
 * Аналог SystemInfo из WPF клиента
 */
object NetworkUtils {

    private const val TAG = "NetworkUtils"
    private const val IP_CHECK_URL = "https://ifconfig.me"
    private const val TIMEOUT_MS = 5000

    /**
     * Получает внешний IP-адрес через сервис ifconfig.me
     * Аналог SystemInfo.GetExternalIp() из WPF клиента
     * @return IP-адрес или пустую строку в случае ошибки
     */
    suspend fun getExternalIp(): String = withContext(Dispatchers.IO) {
        try {
            val client = OkHttpClient.Builder()
                .connectTimeout(TIMEOUT_MS.toLong(), TimeUnit.MILLISECONDS)
                .readTimeout(TIMEOUT_MS.toLong(), TimeUnit.MILLISECONDS)
                .retryOnConnectionFailure(false)
                .build()
            val request = Request.Builder()
                .url(IP_CHECK_URL)
                .header("User-Agent", "BarkFluff-Android")
                .get()
                .build()

            client.newCall(request).execute().use { result ->
                if (result.code == 200) {
                    val response = result.body?.string().orEmpty()

                // Проверяем формат HTML с id="ip_address"
                val htmlPattern = Pattern.compile("""<strong\s+id="ip_address">\s*([\da-fA-F\.:]+)\s*</strong>""", Pattern.CASE_INSENSITIVE)
                val htmlMatch = htmlPattern.matcher(response)
                if (htmlMatch.find()) {
                    return@withContext htmlMatch.group(1).trim()
                }

                // Проверяем простой текстовый формат (просто IP)
                val ipPattern = Pattern.compile("""^([\da-fA-F\.:]+)$""", Pattern.MULTILINE)
                val ipMatch = ipPattern.matcher(response)
                if (ipMatch.find()) {
                    return@withContext ipMatch.group(1).trim()
                }
            }

            Log.w(TAG, "Не удалось получить IP: код ответа ${result.code}")
            ""
            }
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения внешнего IP", e)
            ""
        }
    }
}
