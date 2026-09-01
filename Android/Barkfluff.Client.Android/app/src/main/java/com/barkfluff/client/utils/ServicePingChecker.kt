package com.barkfluff.client.utils

import android.content.Context
import com.barkfluff.client.security.TlsTransportFactory
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Request
import java.io.IOException
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

data class ServicePingResult(
    val available: Boolean,
    val responseTimeMs: Long
)

/** Checks service liveness using the same TLS policy as all application traffic. */
class ServicePingChecker(context: Context) {

    private val tlsTransport = TlsTransportFactory(context.applicationContext)
    private val httpsClients = ConcurrentHashMap<String, OkHttpClient>()
    private val httpClient: OkHttpClient? = if (tlsTransport.allowsCleartext) createHttpClient() else null
    private val http2Client: OkHttpClient? = if (tlsTransport.allowsCleartext) createHttp2Client() else null

    fun check(address: String, fileEndpoint: Boolean = false): ServicePingResult {
        val startedAt = System.nanoTime()
        val probePath = if (fileEndpoint) {
            "/web/download/${UUID.randomUUID()}"
        } else {
            "/ping"
        }
        val probeUrl = buildProbeUrl(address, probePath)
            ?: return ServicePingResult(false, elapsedMs(startedAt))

        return try {
            val request = Request.Builder().url(probeUrl).get().build()
            val client = if (probeUrl.isHttps) {
                httpsClientFor(probeUrl)
            } else if (fileEndpoint) {
                httpClient
            } else {
                http2Client
            }
                ?: return ServicePingResult(false, elapsedMs(startedAt))
            client.newCall(request).execute().use { response ->
                val available = if (fileEndpoint) {
                    // A missing random file returns 404, which still proves that the HTTP listener responds.
                    true
                } else {
                    val contentType = response.body?.contentType()
                    val isPlainText = contentType?.type == "text" && contentType.subtype == "plain"
                    response.code == 200 && isPlainText && response.body?.string()?.trim() == "pong"
                }
                ServicePingResult(available, elapsedMs(startedAt))
            }
        } catch (_: IOException) {
            ServicePingResult(false, elapsedMs(startedAt))
        } catch (_: IllegalArgumentException) {
            ServicePingResult(false, elapsedMs(startedAt))
        }
    }

    fun close() {
        (httpsClients.values + listOfNotNull(httpClient, http2Client)).forEach { client ->
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
        }
        httpsClients.clear()
    }

    private fun buildProbeUrl(address: String, path: String): HttpUrl? {
        val normalizedAddress = address.trim().let { value ->
            if (value.startsWith("http://", ignoreCase = true) ||
                value.startsWith("https://", ignoreCase = true)
            ) {
                value
            } else {
                "https://$value"
            }
        }

        val probeUrl = normalizedAddress.toHttpUrlOrNull()
            ?.newBuilder()
            ?.encodedPath(path)
            ?.query(null)
            ?.build()
            ?: return null
        return probeUrl.takeIf(tlsTransport::isAllowed)
    }

    private fun httpsClientFor(url: HttpUrl): OkHttpClient {
        val key = "${url.scheme}://${url.host}:${url.port}"
        return httpsClients[key] ?: synchronized(httpsClients) {
            httpsClients[key] ?: tlsTransport.createOkHttpClient(url) {
                configureTimeouts()
            }.also { httpsClients[key] = it }
        }
    }

    private fun createHttpClient(): OkHttpClient = OkHttpClient.Builder()
        .configureTimeouts()
        .build()

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
