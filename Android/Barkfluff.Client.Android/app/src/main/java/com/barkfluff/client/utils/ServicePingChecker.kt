package com.barkfluff.client.utils

import android.content.Context
import com.barkfluff.client.security.TlsTransportFactory
import com.barkfluff.client.security.TlsCallFactory
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.Protocol
import okhttp3.Request
import java.io.IOException
import java.util.UUID
import java.util.concurrent.TimeUnit

data class ServicePingResult(
    val available: Boolean,
    val responseTimeMs: Long
)

/** Checks service liveness using the same TLS policy as all application traffic. */
class ServicePingChecker(context: Context) {

    private val tlsTransport = TlsTransportFactory(context.applicationContext)
    private val tlsCalls = TlsCallFactory(context.applicationContext) {
        configureTimeouts()
    }
    private val directHttp2Calls = TlsCallFactory(context.applicationContext) {
        configureTimeouts()
        protocols(listOf(Protocol.H2_PRIOR_KNOWLEDGE))
    }

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
            val callFactory = if (!probeUrl.isHttps && !fileEndpoint && !tlsTransport.proxyEnabled) {
                directHttp2Calls
            } else {
                tlsCalls
            }
            callFactory.newCall(request).execute().use { response ->
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
        tlsCalls.close()
        directHttp2Calls.close()
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

    private fun okhttp3.OkHttpClient.Builder.configureTimeouts(): okhttp3.OkHttpClient.Builder = apply {
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
