package com.barkfluff.client.security

import android.content.Context
import com.barkfluff.client.bft.BftDns
import com.barkfluff.client.bft.BftProxyManager
import com.barkfluff.client.bft.BftServiceRouter
import com.barkfluff.client.bft.BftTlsSocketFactory
import com.barkfluff.client.bft.BftVirtualSocket
import com.barkfluff.client.data.GlobalParam
import io.grpc.ManagedChannel
import io.grpc.ClientCall
import io.grpc.ClientInterceptor
import io.grpc.ForwardingClientCall
import io.grpc.Metadata
import io.grpc.okhttp.OkHttpChannelBuilder
import io.grpc.Status
import okhttp3.Call
import okhttp3.HttpUrl
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Request
import java.io.Closeable
import java.io.IOException
import java.net.InetAddress
import java.net.HttpURLConnection
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketAddress
import java.security.KeyStore
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLSession
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.TrustManagerFactory
import javax.net.ssl.X509TrustManager
import javax.net.SocketFactory

/**
 * The sole transport factory for client-facing network traffic. The release variant uses the
 * platform trust store unless a user has explicitly pinned a self-signed hostname.
 */
class TlsTransportFactory internal constructor(
    private val trustStore: TlsPinLookup,
    internal val bftProxyManager: BftProxyManager? = null,
    private val globalParam: GlobalParam? = null
) {
    constructor(context: Context) : this(
        TlsTrustStore(context.applicationContext),
        BftProxyManager.get(context.applicationContext),
        GlobalParam(context.applicationContext)
    )

    val allowsCleartext: Boolean
        get() = TlsVariantPolicy.allowCleartext

    val proxyEnabled: Boolean
        get() = bftProxyManager?.isEnabled == true

    val transportGeneration: Long
        get() = bftProxyManager?.transportGeneration ?: 0L

    fun addTransportGenerationListener(listener: (Long) -> Unit): Closeable? =
        bftProxyManager?.addTransportGenerationListener(listener)

    fun normalizeGrpcAddress(address: String): String {
        val endpoint = TlsEndpoint.parseAddress(address, TlsVariantPolicy.allowCleartext)
        return "${endpoint.scheme}://${endpoint.host}:${endpoint.port}"
    }

    fun isAllowed(url: HttpUrl): Boolean = runCatching {
        TlsEndpoint.requireUrl(url.toString(), TlsVariantPolicy.allowCleartext)
    }.isSuccess

    fun normalizeUrl(url: String): String =
        TlsEndpoint.requireUrl(url, TlsVariantPolicy.allowCleartext).toString()

    fun createGrpcChannel(address: String, serviceId: Int? = null): ManagedChannel {
        val endpoint = TlsEndpoint.parseAddress(address, TlsVariantPolicy.allowCleartext)
        val proxyManager = bftProxyManager
        val transportGeneration = proxyManager?.transportGeneration ?: 0L
        // HTTP/2 PING only while calls are active (streams stop in background): a connection silently
        // dropped by NAT or a dead network fails within ~50 s instead of hanging until TCP gives up. The timeout
        // leaves room for a PING queued behind a file upload on a slow uplink.
        val builder = if (proxyManager?.isEnabled == true) {
            val logicalService = serviceId ?: BftServiceRouter.serviceIdForEndpoint(address, globalParam?.bftServiceEndpoints.orEmpty())
                ?: throw IOException("BarkFluff service is unavailable through the proxy")
            OkHttpChannelBuilder.forAddress("127.0.0.1", endpoint.port)
                .overrideAuthority(BFT_TLS_NAME)
                .socketFactory(proxyManager.socketFactory(logicalService))
                .sslSocketFactory(BftLayeredTlsSocketFactory())
                .disableRetry()
        } else {
            OkHttpChannelBuilder.forAddress(endpoint.host, endpoint.port)
                .let { TlsVariantPolicy.configureGrpcBuilder(it, endpoint, trustStore) }
        }
            .keepAliveTime(GRPC_KEEPALIVE_TIME_SECONDS, TimeUnit.SECONDS)
            .keepAliveTimeout(GRPC_KEEPALIVE_TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .keepAliveWithoutCalls(false)
        if (proxyManager != null) {
            builder.intercept(TransportGenerationInterceptor(proxyManager, transportGeneration))
        }
        return builder.build()
    }

    fun configure(connection: HttpURLConnection) {
        if (bftProxyManager?.isEnabled == true) {
            throw IOException("HttpURLConnection is unavailable while the proxy is enabled")
        }
        val url = TlsEndpoint.requireUrl(connection.url.toString(), TlsVariantPolicy.allowCleartext)
        if (connection is HttpsURLConnection) {
            val config = socketConfig(TlsEndpoint.canonicalHost(url.host))
            connection.sslSocketFactory = config.socketFactory
        }
    }

    fun configure(builder: OkHttpClient.Builder, url: HttpUrl): OkHttpClient.Builder =
        configure(builder, url, serviceId = null, forceDirect = false)

    private fun configure(
        builder: OkHttpClient.Builder,
        url: HttpUrl,
        serviceId: Int?,
        forceDirect: Boolean
    ): OkHttpClient.Builder {
        val uri = TlsEndpoint.requireUrl(url.toString(), TlsVariantPolicy.allowCleartext)
        if (!forceDirect && bftProxyManager?.isEnabled == true) {
            val logicalService = serviceId ?: BftServiceRouter.serviceIdForHttpUrl(url, globalParam?.bftServiceEndpoints.orEmpty())
            if (logicalService != null) {
                builder.socketFactory(bftProxyManager.socketFactory(logicalService))
                    .dns(BftDns(InetAddress.getByAddress(byteArrayOf(127, 0, 0, 1))))
                    .sslSocketFactory(BftLayeredTlsSocketFactory(), BftLayeredTlsTrustManager)
                    // BftTlsSocketFactory validates the internal name bft.internal and the HELLO pin.
                    .hostnameVerifier(HostnameVerifier { _, _ -> true })
                    .retryOnConnectionFailure(false)
                if (logicalService == FILES_SERVICE_ID || logicalService == FILES_MEDIA_SERVICE_ID ||
                    logicalService == STORAGE_SERVICE_ID
                ) {
                    builder.protocols(listOf(Protocol.HTTP_1_1))
                }
                if (uri.scheme.equals(TlsEndpoint.HTTP, ignoreCase = true)) {
                    builder.addInterceptor { chain ->
                        val request = chain.request()
                        val secureUrl = request.url.newBuilder().scheme(TlsEndpoint.HTTPS).build()
                        chain.proceed(request.newBuilder().url(secureUrl).build())
                    }
                }
                return builder
            }
        }
        if (uri.scheme.equals(TlsEndpoint.HTTPS, ignoreCase = true)) {
            val config = socketConfig(TlsEndpoint.canonicalHost(uri.host ?: error("URL host is required")))
            builder.sslSocketFactory(config.socketFactory, config.trustManager)
        }
        return builder
    }

    fun createOkHttpClient(
        url: HttpUrl,
        customize: OkHttpClient.Builder.() -> Unit = {}
    ): OkHttpClient = createOkHttpClientInternal(url, null, forceDirect = false, customize)

    fun createOkHttpClientForService(
        url: HttpUrl,
        serviceId: Int,
        customize: OkHttpClient.Builder.() -> Unit = {}
    ): OkHttpClient = createOkHttpClientInternal(url, serviceId, forceDirect = false, customize)

    fun createDirectOkHttpClient(
        url: HttpUrl,
        customize: OkHttpClient.Builder.() -> Unit = {}
    ): OkHttpClient = createOkHttpClientInternal(url, null, forceDirect = true, customize)

    private fun createOkHttpClientInternal(
        url: HttpUrl,
        serviceId: Int?,
        forceDirect: Boolean,
        customize: OkHttpClient.Builder.() -> Unit
    ): OkHttpClient {
        val proxyManager = bftProxyManager
        val transportGeneration = proxyManager?.transportGeneration
        val builder = configure(OkHttpClient.Builder(), url, serviceId, forceDirect)
        builder.customize()
        if (!forceDirect && proxyManager != null && transportGeneration != null) {
            builder.addNetworkInterceptor { chain ->
                if (proxyManager.transportGeneration != transportGeneration) {
                    throw IOException("Transport configuration changed")
                }
                chain.proceed(chain.request())
            }
        }
        return builder.build()
    }

    private fun socketConfig(host: String): TlsSocketConfig =
        TlsVariantPolicy.socketConfig(TlsEndpoint.canonicalHost(host), trustStore)

    private companion object {
        const val BFT_TLS_NAME = "bft.internal"
        const val FILES_SERVICE_ID = 5
        const val FILES_MEDIA_SERVICE_ID = 6
        const val STORAGE_SERVICE_ID = 12
        const val GRPC_KEEPALIVE_TIME_SECONDS = 30L
        const val GRPC_KEEPALIVE_TIMEOUT_SECONDS = 20L
    }
}

private class TransportGenerationInterceptor(
    private val proxyManager: BftProxyManager,
    private val expectedGeneration: Long
) : ClientInterceptor {
    override fun <RequestT : Any?, ResponseT : Any?> interceptCall(
        method: io.grpc.MethodDescriptor<RequestT, ResponseT>,
        callOptions: io.grpc.CallOptions,
        next: io.grpc.Channel
    ): ClientCall<RequestT, ResponseT> {
        val call = next.newCall(method, callOptions)
        return object : ForwardingClientCall.SimpleForwardingClientCall<RequestT, ResponseT>(call) {
            override fun start(responseListener: Listener<ResponseT>, headers: Metadata) {
                if (proxyManager.transportGeneration != expectedGeneration) {
                    responseListener.onClose(
                        Status.UNAVAILABLE.withDescription("Transport configuration changed"),
                        Metadata()
                    )
                    return
                }
                super.start(responseListener, headers)
            }
        }
    }
}

private object BftLayeredTlsTrustManager : X509TrustManager {
    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) =
        throw CertificateException("BFT client certificates are not configured")

    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
        if (chain.isEmpty()) throw CertificateException("Exit did not provide a certificate")
        // The BFT TLS socket applies the session HELLO SPKI pin before OkHttp sees the session.
    }

    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
}

private class BftLayeredTlsSocketFactory : SSLSocketFactory() {
    override fun createSocket(socket: Socket, host: String, port: Int, autoClose: Boolean): Socket {
        val virtual = socket as? BftVirtualSocket ?: throw IOException("BFT TLS requires a virtual socket")
        val pin = virtual.tlsSpkiPin ?: throw IOException("BFT stream did not provide a TLS pin")
        return BftTlsSocketFactory(pin, tlsHostOverride = "bft.internal")
            .createSocket(virtual, "bft.internal", port, autoClose)
    }

    override fun createSocket(): Socket = unsupported()
    override fun createSocket(host: String, port: Int): Socket = unsupported()
    override fun createSocket(host: String, port: Int, localHost: InetAddress, localPort: Int): Socket = unsupported()
    override fun createSocket(host: InetAddress, port: Int): Socket = unsupported()
    override fun createSocket(address: InetAddress, port: Int, localAddress: InetAddress, localPort: Int): Socket = unsupported()
    override fun getDefaultCipherSuites(): Array<String> = emptyArray()
    override fun getSupportedCipherSuites(): Array<String> = emptyArray()

    private fun unsupported(): Nothing = throw IOException("BFT TLS must wrap a connected virtual socket")
}

/** Selects an OkHttp TLS configuration for each requested hostname. */
class TlsCallFactory(
    context: Context,
    private val customize: OkHttpClient.Builder.() -> Unit = {}
) : Call.Factory {
    private val transport = TlsTransportFactory(context)
    private val clients = mutableMapOf<String, OkHttpClient>()
    private val clientsLock = Any()
    @Volatile private var clientsGeneration = transport.bftProxyManager?.transportGeneration ?: 0L
    private val generationListener = transport.bftProxyManager?.addTransportGenerationListener { notifiedGeneration ->
        synchronized(clientsLock) {
            val currentGeneration = transport.bftProxyManager?.transportGeneration ?: notifiedGeneration
            if (clientsGeneration != currentGeneration) {
                closeClientsLocked()
                clientsGeneration = currentGeneration
            }
        }
    }

    override fun newCall(request: Request): Call {
        synchronized(clientsLock) {
            val generation = transport.bftProxyManager?.transportGeneration ?: 0L
            if (generation != clientsGeneration) {
                closeClientsLocked()
                clientsGeneration = generation
            }
            val url = request.url
            val key = "${url.scheme}://${url.host}:${url.port}"
            val client = clients[key] ?: transport.createOkHttpClient(url, customize).also { clients[key] = it }
            return client.newCall(request)
        }
    }

    fun close() {
        generationListener?.close()
        synchronized(clientsLock) { closeClientsLocked() }
    }

    private fun closeClientsLocked() {
        clients.values.forEach { client ->
            client.dispatcher.cancelAll()
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
