package com.barkfluff.client.utils

import android.util.Log
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.HttpURLConnection
import java.net.URL
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
            val url = URL(IP_CHECK_URL)
            val connection = url.openConnection() as HttpURLConnection
            connection.connectTimeout = TIMEOUT_MS
            connection.readTimeout = TIMEOUT_MS
            connection.requestMethod = "GET"
            connection.setRequestProperty("User-Agent", "BarkFluff-Android")

            val responseCode = connection.responseCode
            if (responseCode == HttpURLConnection.HTTP_OK) {
                val response = connection.inputStream.bufferedReader().use { it.readText() }

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

            Log.w(TAG, "Не удалось получить IP: код ответа $responseCode")
            ""
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения внешнего IP", e)
            ""
        }
    }
}
