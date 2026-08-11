package com.barkfluff.client.security

import android.content.Context
import io.grpc.ManagedChannel
import io.grpc.okhttp.OkHttpChannelBuilder
import okhttp3.Call
import okhttp3.HttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import java.net.HttpURLConnection
import java.security.KeyStore
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.ConcurrentHashMap
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.TrustManagerFactory
import javax.net.ssl.X509TrustManager

/**
 * The sole transport factory for client-facing network traffic. The release variant uses the
 * platform trust store unless a user has explicitly pinned a self-signed hostname.
 */
class TlsTransportFactory(context: Context) {
    val trustStore = TlsTrustStore(context)

    val allowsCleartext: Boolean
        get() = TlsVariantPolicy.allowCleartext

    fun normalizeGrpcAddress(address: String): String {
        val endpoint = TlsEndpoint.parseAddress(address, TlsVariantPolicy.allowCleartext)
        return "${endpoint.scheme}://${endpoint.host}:${endpoint.port}"
    }

    fun isAllowed(url: HttpUrl): Boolean = runCatching {
        TlsEndpoint.requireUrl(url.toString(), TlsVariantPolicy.allowCleartext)
    }.isSuccess

    fun normalizeUrl(url: String): String =
        TlsEndpoint.requireUrl(url, TlsVariantPolicy.allowCleartext).toString()

    fun createGrpcChannel(address: String): ManagedChannel {
        val endpoint = TlsEndpoint.parseAddress(address, TlsVariantPolicy.allowCleartext)
        val builder = OkHttpChannelBuilder.forAddress(endpoint.host, endpoint.port)
        if (endpoint.usesTls) {
            val config = socketConfig(endpoint.host)
            builder.sslSocketFactory(config.socketFactory)
        } else {
            builder.usePlaintext()
        }
        return builder.build()
    }

    fun configure(connection: HttpURLConnection) {
        val url = TlsEndpoint.requireUrl(connection.url.toString(), TlsVariantPolicy.allowCleartext)
        if (connection is HttpsURLConnection) {
            val config = socketConfig(TlsEndpoint.canonicalHost(url.host))
            connection.sslSocketFactory = config.socketFactory
        }
    }

    fun configure(builder: OkHttpClient.Builder, url: HttpUrl): OkHttpClient.Builder {
        val uri = TlsEndpoint.requireUrl(url.toString(), TlsVariantPolicy.allowCleartext)
        if (uri.scheme.equals(TlsEndpoint.HTTPS, ignoreCase = true)) {
            val config = socketConfig(TlsEndpoint.canonicalHost(uri.host ?: error("URL host is required")))
            builder.sslSocketFactory(config.socketFactory, config.trustManager)
        }
        return builder
    }

    fun createOkHttpClient(
        url: HttpUrl,
        customize: OkHttpClient.Builder.() -> Unit = {}
    ): OkHttpClient {
        val builder = configure(OkHttpClient.Builder(), url)
        builder.customize()
        return builder.build()
    }

    private fun socketConfig(host: String): TlsSocketConfig =
        TlsVariantPolicy.socketConfig(TlsEndpoint.canonicalHost(host), trustStore)
}

/** Selects an OkHttp TLS configuration for each requested hostname. */
class TlsCallFactory(
    context: Context,
    private val customize: OkHttpClient.Builder.() -> Unit = {}
) : Call.Factory {
    private val transport = TlsTransportFactory(context)
    private val clients = ConcurrentHashMap<String, OkHttpClient>()

    override fun newCall(request: Request): Call {
        val url = request.url
        val key = "${url.scheme}://${url.host}:${url.port}"
        val client = clients[key] ?: synchronized(clients) {
            clients[key] ?: transport.createOkHttpClient(url, customize).also { clients[key] = it }
        }
        return client.newCall(request)
    }

    fun close() {
        clients.values.forEach { client ->
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
        }
        clients.clear()
    }
}

internal data class TlsSocketConfig(
    val socketFactory: SSLSocketFactory,
    val trustManager: X509TrustManager
)

internal class PinnedTrustManager(
    private val host: String,
    private val trustStore: TlsPinLookup,
    private val platformTrustManager: X509TrustManager = platformTrustManager()
) : X509TrustManager {
    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {
        platformTrustManager.checkClientTrusted(chain, authType)
    }

    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
        val leaf = chain.firstOrNull() ?: throw CertificateException("Server did not provide a certificate")
        leaf.checkValidity()

        val pin = trustStore.pinFor(host)
        if (pin != null) {
            val observed = TlsCertificate.spkiSha256(leaf)
            if (pin.spkiSha256 != observed) {
                throw CertificateException("TLS public key for $host changed")
            }
            return
        }

        platformTrustManager.checkServerTrusted(chain, authType)
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = platformTrustManager.acceptedIssuers
}

internal object TlsCertificate {
    fun spkiSha256(certificate: X509Certificate): String {
        val digest = java.security.MessageDigest.getInstance("SHA-256")
            .digest(certificate.publicKey.encoded)
        val encoded = java.util.Base64.getEncoder().encodeToString(digest)
        return "sha256/$encoded"
    }
}

internal fun platformTrustManager(): X509TrustManager {
    val factory = TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm())
    factory.init(null as KeyStore?)
    return factory.trustManagers.filterIsInstance<X509TrustManager>().single()
}

internal fun socketConfigFor(trustManager: X509TrustManager): TlsSocketConfig {
    val context = SSLContext.getInstance("TLS")
    context.init(null, arrayOf(trustManager), null)
    return TlsSocketConfig(context.socketFactory, trustManager)
}
