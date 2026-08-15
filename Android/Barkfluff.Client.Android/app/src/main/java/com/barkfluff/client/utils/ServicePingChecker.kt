package com.barkfluff.client.utils

import android.content.Context
import com.barkfluff.client.security.TlsTransportFactory
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Request
import java.io.IOException
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

data class ServicePingResult(
    val available: Boolean,
    val responseTimeMs: Long
)

/** Checks the anonymous GET /ping endpoint using the same TLS policy as all application traffic. */
class ServicePingChecker(context: Context) {

    private val tlsTransport = TlsTransportFactory(context.applicationContext)
    private val httpsClients = ConcurrentHashMap<String, OkHttpClient>()
    private val http2Client: OkHttpClient? = if (tlsTransport.allowsCleartext) createHttp2Client() else null

    fun check(address: String): ServicePingResult {
        val startedAt = System.nanoTime()
        val pingUrl = buildPingUrl(address)
            ?: return ServicePingResult(false, elapsedMs(startedAt))

        return try {
            val request = Request.Builder().url(pingUrl).get().build()
            val client = if (pingUrl.isHttps) httpsClientFor(pingUrl) else http2Client
                ?: return ServicePingResult(false, elapsedMs(startedAt))
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
        (httpsClients.values + listOfNotNull(http2Client)).forEach { client ->
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
        }
        httpsClients.clear()
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

        val pingUrl = normalizedAddress.toHttpUrlOrNull()
            ?.newBuilder()
            ?.encodedPath("/ping")
            ?.query(null)
            ?.build()
            ?: return null
        return pingUrl.takeIf(tlsTransport::isAllowed)
    }

    private fun httpsClientFor(url: HttpUrl): OkHttpClient {
        val key = "${url.scheme}://${url.host}:${url.port}"
        return httpsClients[key] ?: synchronized(httpsClients) {
            httpsClients[key] ?: tlsTransport.createOkHttpClient(url) {
                configureTimeouts()
            }.also { httpsClients[key] = it }
        }
    }

    private fun createHttp2Client(): OkHttpClient = OkHttpClient.Builder()
        .configureTimeouts()
        // The development gRPC listener exposes its liveness route over h2c.
        .protocols(listOf(Protocol.H2_PRIOR_KNOWLEDGE))
        .build()

    private fun OkHttpClient.Builder.configureTimeouts(): OkHttpClient.Builder = apply {
        connectTimeout(PING_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        readTimeout(PING_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        writeTimeout(PING_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        callTimeout(PING_TIMEOUT_MS, TimeUnit.MILLISECONDS)
        retryOnConnectionFailure(false)
    }

    private fun elapsedMs(startedAt: Long): Long =
        TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - startedAt).coerceAtLeast(0L)

    private companion object {
        const val PING_TIMEOUT_MS = 3_000L
    }
}
