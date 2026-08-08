package com.barkfluff.client.utils

import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.Request
import java.io.IOException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

data class ServicePingResult(
    val available: Boolean,
    val responseTimeMs: Long
)

/** Проверяет анонимный GET /ping на адресе микросервиса. */
class ServicePingChecker {

    private val httpsClient = createHttpsClient()
    private val http2Client = createHttp2Client()

    fun check(address: String): ServicePingResult {
        val startedAt = System.nanoTime()
        val pingUrl = buildPingUrl(address)
        if (pingUrl == null) {
            return ServicePingResult(false, elapsedMs(startedAt))
        }

        return try {
            val request = Request.Builder()
                .url(pingUrl)
                .get()
                .build()

            val client = if (pingUrl.isHttps) httpsClient else http2Client
            client.newCall(request).execute().use { response ->
                val contentType = response.body?.contentType()
                val isPlainText = contentType?.type == "text" && contentType.subtype == "plain"
                val isPong = response.code == 200 && isPlainText && response.body?.string()?.trim() == "pong"
                ServicePingResult(isPong, elapsedMs(startedAt))
            }
        } catch (_: IOException) {
            ServicePingResult(false, elapsedMs(startedAt))
        } catch (_: IllegalArgumentException) {
            ServicePingResult(false, elapsedMs(startedAt))
        }
    }

    fun close() {
        listOf(httpsClient, http2Client).forEach { client ->
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
        }
    }

    private fun buildPingUrl(address: String): HttpUrl? {
        val normalizedAddress = address.trim().let { value ->
            if (value.startsWith("http://", ignoreCase = true) ||
                value.startsWith("https://", ignoreCase = true)
            ) {
                value
            } else {
                "https://$value"
            }
        }

        return normalizedAddress.toHttpUrlOrNull()
            ?.newBuilder()
            ?.encodedPath("/ping")
            ?.query(null)
            ?.build()
    }

    private fun createHttpsClient(): OkHttpClient {
        val trustManager = trustAllManager()
        val sslSocketFactory = trustAllSocketFactory(trustManager)
        return baseClientBuilder()
            .sslSocketFactory(sslSocketFactory, trustManager)
            .hostnameVerifier(HostnameVerifier { _, _ -> true })
            .build()
    }

    private fun createHttp2Client(): OkHttpClient = baseClientBuilder()
        // Основной gRPC-listener сервисов работает на h2c без отдельного HTTP/1 порта.
        .protocols(listOf(Protocol.H2_PRIOR_KNOWLEDGE))
        .build()

    private fun baseClientBuilder(): OkHttpClient.Builder = OkHttpClient.Builder()
        .connectTimeout(PING_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        .readTimeout(PING_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        .writeTimeout(PING_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        .callTimeout(PING_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        .retryOnConnectionFailure(false)

    private fun trustAllManager(): X509TrustManager = object : X509TrustManager {
        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = Unit
        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) = Unit
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    private fun trustAllSocketFactory(trustManager: X509TrustManager): SSLSocketFactory {
        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(null, arrayOf<TrustManager>(trustManager), null)
        return sslContext.socketFactory
    }

    private fun elapsedMs(startedAt: Long): Long =
        TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - startedAt).coerceAtLeast(0L)

    private companion object {
        const val PING_TIMEOUT_MS = 3_000L
    }
}
