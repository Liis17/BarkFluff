package com.barkfluff.client.bft

import io.grpc.CallOptions
import io.grpc.ManagedChannel
import io.grpc.MethodDescriptor
import io.grpc.Server
import io.grpc.ServerServiceDefinition
import io.grpc.TlsServerCredentials
import io.grpc.okhttp.OkHttpServerBuilder
import io.grpc.stub.ClientCalls
import io.grpc.stub.ServerCalls
import io.grpc.stub.StreamObserver
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.RequestBody
import okio.BufferedSink
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketException
import java.net.UnknownHostException
import java.security.KeyStore
import java.security.MessageDigest
import java.security.PrivateKey
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.Collections
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.KeyManager
import javax.net.ssl.KeyManagerFactory
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLServerSocket
import javax.net.ssl.SSLSocket
import javax.net.ssl.SSLHandshakeException
import javax.net.ssl.HostnameVerifier
import java.net.URL
import com.barkfluff.client.security.TlsPin
import com.barkfluff.client.security.TlsPinLookup
import com.barkfluff.client.security.TlsTransportFactory
import okhttp3.HttpUrl.Companion.toHttpUrl

internal object BftTransportTestSupport {
    const val ORIGIN = "bft-origin.invalid"

    private const val SERVICE_NAME = "bft.test.Transport"
    private const val TEST_KEY_ALIAS = "bft-test"
    private const val TEST_KEY_PASSWORD = "bft-test-only"
    private val LOOPBACK = InetAddress.getByAddress(byteArrayOf(127, 0, 0, 1))

    private val UNARY_METHOD = method("Unary", MethodDescriptor.MethodType.UNARY)
    private val SERVER_STREAM_METHOD = method("ServerStream", MethodDescriptor.MethodType.SERVER_STREAMING)
    private val BIDI_METHOD = method("Bidi", MethodDescriptor.MethodType.BIDI_STREAMING)

    fun loadIdentity(input: InputStream): TestIdentity {
        val password = TEST_KEY_PASSWORD.toCharArray()
        val keyStore = KeyStore.getInstance("PKCS12")
        input.use { keyStore.load(it, password) }
        val certificate = keyStore.getCertificate(TEST_KEY_ALIAS) as X509Certificate
        val privateKey = keyStore.getKey(TEST_KEY_ALIAS, password) as PrivateKey
        val keyManagerFactory = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm())
        keyManagerFactory.init(keyStore, password)
        return TestIdentity(certificate, privateKey, keyManagerFactory.keyManagers)
    }

    fun assertOriginUnresolvable() {
        assertThrows(UnknownHostException::class.java) { InetAddress.getAllByName(ORIGIN) }
    }

    fun tlsProviderName(): String = SSLContext.getInstance("TLS").provider.let {
        "${it.name} (${it.javaClass.name})"
    }

    fun verifyGrpc(identity: TestIdentity) = verifyGrpcWithRoute(identity, { port, opened ->
        BftSocketFactory {
            opened.incrementAndGet()
            openLoopbackPipe(port)
        }
    }, {})

    fun verifyGrpcOverBftMux(
        identity: TestIdentity,
        routeFactory: (Int, AtomicInteger) -> BftSocketFactory,
        closeRoute: () -> Unit
    ) = verifyGrpcWithRoute(identity, routeFactory, closeRoute)

    private fun verifyGrpcWithRoute(
        identity: TestIdentity,
        routeFactory: (Int, AtomicInteger) -> BftSocketFactory,
        closeRoute: () -> Unit
    ) {
        val server = TestGrpcServer(identity).start()
        val tlsSocketFactory = BftTlsSocketFactory(identity.pin)
        val openedPipes = AtomicInteger()
        val socketFactory = routeFactory(server.port, openedPipes)
        val channel = createGrpcClient(server.port, socketFactory, tlsSocketFactory)

        try {
            val unary = ClientCalls.blockingUnaryCall(
                channel,
                UNARY_METHOD,
                CallOptions.DEFAULT.withDeadlineAfter(10, TimeUnit.SECONDS),
                "hello".toByteArray()
            )
            assertEquals("unary:hello", unary.decodeToString())

            val streamResponses = ClientCalls.blockingServerStreamingCall(
                channel,
                SERVER_STREAM_METHOD,
                CallOptions.DEFAULT.withDeadlineAfter(10, TimeUnit.SECONDS),
                "item".toByteArray()
            ).asSequence().map(ByteArray::decodeToString).toList()
            assertEquals(listOf("item-1", "item-2", "item-3"), streamResponses)

            val bidiResponses = Collections.synchronizedList(mutableListOf<String>())
            val bidiError = AtomicReference<Throwable?>()
            val bidiDone = CountDownLatch(1)
            val requestObserver = ClientCalls.asyncBidiStreamingCall(
                channel.newCall(
                    BIDI_METHOD,
                    CallOptions.DEFAULT.withDeadlineAfter(10, TimeUnit.SECONDS)
                ),
                object : StreamObserver<ByteArray> {
                    override fun onNext(value: ByteArray) {
                        bidiResponses += value.decodeToString()
                    }

                    override fun onError(error: Throwable) {
                        bidiError.set(error)
                        bidiDone.countDown()
                    }

                    override fun onCompleted() {
                        bidiDone.countDown()
                    }
                }
            )
            requestObserver.onNext("one".toByteArray())
            requestObserver.onNext("two".toByteArray())
            requestObserver.onCompleted()

            assertTrue("bidi stream did not finish", bidiDone.await(10, TimeUnit.SECONDS))
            assertNull(bidiError.get())
            assertEquals(listOf("bidi:one", "bidi:two"), bidiResponses)
            assertTrue("gRPC did not use the BFT socket factory", openedPipes.get() > 0)
        } finally {
            channel.shutdownNow().awaitTermination(5, TimeUnit.SECONDS)
            closeRoute()
            server.close()
        }
    }

    fun verifyOkHttpHttp11(identity: TestIdentity) = verifyOkHttpHttp11WithRoute(identity, { port, opened ->
        BftSocketFactory {
            opened.incrementAndGet()
            openLoopbackPipe(port)
        }
    }, {})

    fun verifyOkHttpHttp11OverBftMux(
        identity: TestIdentity,
        routeFactory: (Int, AtomicInteger) -> BftSocketFactory,
        closeRoute: () -> Unit
    ) = verifyOkHttpHttp11WithRoute(identity, routeFactory, closeRoute)

    private fun verifyOkHttpHttp11WithRoute(
        identity: TestIdentity,
        routeFactory: (Int, AtomicInteger) -> BftSocketFactory,
        closeRoute: () -> Unit
    ) {
        val downloadBytes = payload(5 * 1024 * 1024)
        val uploadBytes = payload(6 * 1024 * 1024)
        val server = TestHttpServer(identity, downloadBytes).start()
        val tlsSocketFactory = BftTlsSocketFactory(identity.pin)
        val openedPipes = AtomicInteger()
        val client = OkHttpClient.Builder()
            .socketFactory(routeFactory(server.port, openedPipes))
            .dns(BftDns(LOOPBACK))
            .sslSocketFactory(tlsSocketFactory, tlsSocketFactory.trustManager)
            .protocols(listOf(Protocol.HTTP_1_1))
            .build()

        try {
            val uploadBody = object : RequestBody() {
                override fun contentType() = "application/octet-stream".toMediaType()
                override fun contentLength() = uploadBytes.size.toLong()
                override fun writeTo(sink: BufferedSink) {
                    sink.write(uploadBytes)
                }
            }
            val uploadRequest = Request.Builder()
                .url("https://$ORIGIN:${server.port}/upload")
                .post(uploadBody)
                .build()
            try {
                client.newCall(uploadRequest).execute().use { response ->
                    assertTrue("upload response was ${response.code}", response.isSuccessful)
                    assertEquals("ok", response.body?.string())
                }
            } catch (error: Throwable) {
                val serverError = server.failure.get()
                if (serverError != null) {
                    throw AssertionError("HTTP test server failed", serverError).also {
                        it.addSuppressed(error)
                    }
                }
                throw error
            }
            assertEquals(sha256(uploadBytes), server.uploadSha256.get())

            val downloadRequest = Request.Builder()
                .url("https://$ORIGIN:${server.port}/download")
                .get()
                .build()
            val downloaded = client.newCall(downloadRequest).execute().use { response ->
                assertTrue("download response was ${response.code}", response.isSuccessful)
                response.body!!.bytes()
            }

            assertEquals(sha256(downloadBytes), sha256(downloaded))
            assertArrayEquals(downloadBytes, downloaded)
            assertTrue("OkHttp did not use the BFT socket factory", openedPipes.get() >= 2)
            assertNull(server.failure.get())
        } finally {
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
            closeRoute()
            server.close()
        }
    }

    fun verifyWrongPinRejected(identity: TestIdentity) {
        val server = TestHttpServer(identity, payload(128)).start()
        val wrongPin = "sha256/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
        val tlsSocketFactory = BftTlsSocketFactory(wrongPin)
        val client = newHttpClient(server.port, tlsSocketFactory)

        try {
            val request = Request.Builder()
                .url("https://$ORIGIN:${server.port}/download")
                .build()
            val failure = assertThrows(IOException::class.java) {
                client.newCall(request).execute().use { it.close() }
            }
            assertTrue(
                "wrong pin did not fail during TLS: $failure",
                failure.causeChain().any { it is SSLHandshakeException || it is CertificateException }
            )
            assertEquals(1, server.acceptedConnections.get())
        } finally {
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
            server.close()
        }
    }

    fun verifyWrongPinRejectedOverBftTunnel(tunnel: BftTunnel, serviceId: Int) {
        val wrongPin = "sha256/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
        val tlsSocketFactory = BftTlsSocketFactory(wrongPin)
        val client = OkHttpClient.Builder()
            .socketFactory(BftSocketFactory(tunnel, serviceId))
            .dns(BftDns(LOOPBACK))
            .sslSocketFactory(tlsSocketFactory, tlsSocketFactory.trustManager)
            .protocols(listOf(Protocol.HTTP_1_1))
            .build()

        try {
            val request = Request.Builder().url("https://bft.internal:443/").build()
            val failure = assertThrows(IOException::class.java) {
                client.newCall(request).execute().use { it.close() }
            }
            assertTrue(
                "wrong pin did not fail during TLS: $failure",
                failure.causeChain().any { it is SSLHandshakeException || it is CertificateException }
            )
            assertEquals(BftTunnel.State.READY, tunnel.state)
        } finally {
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
        }
    }

    fun verifyWrongPinRejectedOverBftMux(
        identity: TestIdentity,
        routeFactory: (Int, AtomicInteger) -> BftSocketFactory,
        closeRoute: () -> Unit
    ) {
        val server = TestHttpServer(identity, payload(128)).start()
        val wrongPin = "sha256/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
        val tlsSocketFactory = BftTlsSocketFactory(wrongPin)
        val client = OkHttpClient.Builder()
            .socketFactory(routeFactory(server.port, AtomicInteger()))
            .dns(BftDns(LOOPBACK))
            .sslSocketFactory(tlsSocketFactory, tlsSocketFactory.trustManager)
            .protocols(listOf(Protocol.HTTP_1_1))
            .build()

        try {
            val request = Request.Builder().url("https://$ORIGIN:${server.port}/download").build()
            val failure = assertThrows(IOException::class.java) {
                client.newCall(request).execute().use { it.close() }
            }
            assertTrue(
                "wrong pin did not fail during TLS: $failure",
                failure.causeChain().any { it is SSLHandshakeException || it is CertificateException }
            )
            assertEquals(1, server.acceptedConnections.get())
        } finally {
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
            closeRoute()
            server.close()
        }
    }

    fun verifyExitGrpcUnavailable(tunnel: BftTunnel, serviceId: Int) {
        val tlsSocketFactory = BftTlsSocketFactory(helloPin(tunnel))
        val channel = io.grpc.okhttp.OkHttpChannelBuilder
            .forAddress("127.0.0.1", 443)
            .overrideAuthority("bft.internal")
            .socketFactory(BftSocketFactory(tunnel, serviceId))
            .sslSocketFactory(tlsSocketFactory)
            .build()

        try {
            val error = assertThrows(io.grpc.StatusRuntimeException::class.java) {
                ClientCalls.blockingUnaryCall(
                    channel,
                    UNARY_METHOD,
                    CallOptions.DEFAULT.withDeadlineAfter(10, TimeUnit.SECONDS),
                    "probe".toByteArray()
                )
            }
            assertEquals(io.grpc.Status.Code.UNAVAILABLE, error.status.code)
            assertTrue("Exit h2 response did not report upstream HTTP 502: ${error.status.description}",
                error.status.description?.contains("502") == true)
            assertEquals(BftTunnel.State.READY, tunnel.state)
        } finally {
            channel.shutdownNow().awaitTermination(5, TimeUnit.SECONDS)
        }
    }

    fun verifyExitHttp11ReturnsBadGateway(tunnel: BftTunnel, serviceId: Int) {
        val tlsSocketFactory = BftTlsSocketFactory(helloPin(tunnel))
        val client = OkHttpClient.Builder()
            .socketFactory(BftSocketFactory(tunnel, serviceId))
            .dns(BftDns(LOOPBACK))
            .sslSocketFactory(tlsSocketFactory, tlsSocketFactory.trustManager)
            .protocols(listOf(Protocol.HTTP_1_1))
            .build()

        try {
            val request = Request.Builder().url("https://bft.internal:443/").build()
            client.newCall(request).execute().use { response ->
                assertEquals(502, response.code)
                response.body?.close()
            }
            assertEquals(BftTunnel.State.READY, tunnel.state)
        } finally {
            client.connectionPool.evictAll()
            client.dispatcher.executorService.shutdown()
        }
    }

    fun verifyProductionFactoriesOverExit(connectionString: String) {
        val settingsStore = MemoryProxySettingsStore(connectionString)
        val manager = BftProxyManager(settingsStore, tunnelConnector = { link ->
            val settings = BftConnectionString.parse(link)
            try {
                val socket = Socket()
                socket.connect(InetSocketAddress(settings.host, settings.port), 15_000)
                BftTunnel.connect(
                    socket,
                    link,
                    BftTunnelOptions(handshakeTimeoutMillis = 15_000, helloTimeoutMillis = 15_000)
                )
            } finally {
                settings.staticPublicKey.fill(0)
                settings.obfsKey.fill(0)
            }
        })
        manager.setPersistentNetworkDemand(true)
        val transport = TlsTransportFactory(object : TlsPinLookup {
            override fun pinFor(host: String): TlsPin? = null
        }, manager)
        val channel = transport.createGrpcChannel("https://bft.internal:443", serviceId = 1)
        try {
            val error = assertThrows(io.grpc.StatusRuntimeException::class.java) {
                ClientCalls.blockingUnaryCall(
                    channel,
                    UNARY_METHOD,
                    CallOptions.DEFAULT.withDeadlineAfter(15, TimeUnit.SECONDS),
                    "android-production-grpc".toByteArray()
                )
            }
            assertEquals(io.grpc.Status.Code.UNAVAILABLE, error.status.code)
            assertTrue("production gRPC did not receive Exit's upstream response", error.status.description?.contains("502") == true)
        } finally {
            channel.shutdownNow().awaitTermination(5, TimeUnit.SECONDS)
        }

        val httpClient = transport.createOkHttpClientForService("https://bft.internal:443/".toHttpUrl(), 2) {
            protocols(listOf(Protocol.HTTP_1_1))
            retryOnConnectionFailure(false)
        }
        try {
            httpClient.newCall(Request.Builder().url("https://bft.internal:443/").build()).execute().use { response ->
                assertEquals(502, response.code)
            }
        } finally {
            httpClient.connectionPool.evictAll()
            httpClient.dispatcher.executorService.shutdownNow()
        }
        manager.setPersistentNetworkDemand(false)
    }

    private class MemoryProxySettingsStore(connectionString: String) : BftProxyManager.BftProxySettingsStore {
        private var stored = BftProxyManager.BftProxyStoredSettings(connectionString, "Android test", true)
        override fun read() = stored
        override fun write(connectionString: String, name: String, enabled: Boolean) {
            stored = BftProxyManager.BftProxyStoredSettings(connectionString, name, enabled)
        }
        override fun clear() { stored = stored.copy(enabled = false) }
    }

    fun verifyUrlConnectionCannotRoute(identity: TestIdentity) {
        val server = TestHttpServer(identity, payload(128)).start()
        val openedStreams = AtomicInteger()
        val virtualSocketFactory = BftSocketFactory {
            openedStreams.incrementAndGet()
            openLoopbackPipe(server.port)
        }
        val tlsSocketFactory = BftTlsSocketFactory(identity.pin, virtualSocketFactory, ORIGIN)
        val connection = URL("https://$ORIGIN:${server.port}/download").openConnection() as HttpsURLConnection
        connection.sslSocketFactory = tlsSocketFactory
        connection.connectTimeout = 2_000
        connection.readTimeout = 2_000

        try {
            val failure = assertThrows(IOException::class.java) { connection.responseCode }
            assertTrue(
                "URLConnection neither failed at origin DNS nor rejected direct socket creation: $failure",
                failure.causeChain().any {
                    it is UnknownHostException ||
                        (it is SocketException && it.message == "BFT TLS must wrap a connected virtual socket")
                }
            )
            assertEquals("URLConnection opened a connection despite unresolved origin", 0, server.acceptedConnections.get())
            assertEquals("URLConnection reached the BFT socket factory despite unresolved origin", 0, openedStreams.get())
        } finally {
            connection.disconnect()
            server.close()
        }
    }

    fun verifyUrlConnectionUsesVirtualSocket(identity: TestIdentity) {
        val downloadBytes = payload(1024)
        val server = TestHttpServer(identity, downloadBytes, requireAlpnH1 = false).start()
        val socketSink = TestSocketSink().start()
        val openedStreams = AtomicInteger()
        val virtualSocketFactory = BftSocketFactory {
            openedStreams.incrementAndGet()
            openLoopbackPipe(server.port)
        }
        val tlsSocketFactory = BftTlsSocketFactory(identity.pin, virtualSocketFactory, ORIGIN)
        val connection = URL("https://127.0.0.1:${socketSink.port}/download")
            .openConnection() as HttpsURLConnection
        connection.sslSocketFactory = tlsSocketFactory
        connection.hostnameVerifier = HostnameVerifier { host, session ->
            HttpsURLConnection.getDefaultHostnameVerifier().verify(host, session)
        }
        connection.connectTimeout = 2_000
        connection.readTimeout = 2_000

        try {
            assertEquals(200, connection.responseCode)
            val downloaded = connection.inputStream.use { it.readBytes() }
            assertEquals(sha256(downloadBytes), sha256(downloaded))
            assertArrayEquals(downloadBytes, downloaded)
            assertTrue("URLConnection did not use the BFT virtual socket", openedStreams.get() > 0)
            assertEquals(1, server.acceptedConnections.get())
            assertNull(server.failure.get())
        } finally {
            connection.disconnect()
            socketSink.close()
            server.close()
        }
    }

    fun verifyLayeredTlsFactoryUsesVirtualSocket(identity: TestIdentity) {
        val downloadBytes = payload(1024)
        val server = TestHttpServer(identity, downloadBytes, requireAlpnH1 = false).start()
        val socketSink = TestSocketSink().start()
        val openedStreams = AtomicInteger()
        val virtualSocketFactory = BftSocketFactory {
            openedStreams.incrementAndGet()
            openLoopbackPipe(server.port)
        }
        val tlsSocketFactory = BftTlsSocketFactory(identity.pin, virtualSocketFactory, ORIGIN)
        val rawSocket = Socket().apply {
            connect(InetSocketAddress(LOOPBACK, socketSink.port), 2_000)
        }

        try {
            val tlsSocket = tlsSocketFactory.createSocket(
                rawSocket,
                LOOPBACK.hostAddress,
                socketSink.port,
                true
            ) as SSLSocket
            tlsSocket.use {
                it.startHandshake()
                it.outputStream.write(
                    (
                        "GET /download HTTP/1.1\r\n" +
                        "Host: $ORIGIN:${server.port}\r\n" +
                        "Connection: close\r\n\r\n"
                    ).toByteArray(Charsets.US_ASCII)
                )
                it.outputStream.flush()
                val downloaded = readHttpResponseBody(it.inputStream)
                assertArrayEquals(downloadBytes, downloaded)
            }
            assertTrue("layered TLS did not use the BFT virtual socket", openedStreams.get() > 0)
            assertEquals(1, server.acceptedConnections.get())
            assertNull(server.failure.get())
        } finally {
            rawSocket.close()
            socketSink.close()
            server.close()
        }
    }

    private fun newHttpClient(port: Int, tlsSocketFactory: BftTlsSocketFactory): OkHttpClient =
        OkHttpClient.Builder()
            .socketFactory(BftSocketFactory { openLoopbackPipe(port) })
            .dns(BftDns(LOOPBACK))
            .sslSocketFactory(tlsSocketFactory, tlsSocketFactory.trustManager)
            .protocols(listOf(Protocol.HTTP_1_1))
            .build()

    private fun helloPin(tunnel: BftTunnel): String =
        "sha256/${java.util.Base64.getEncoder().encodeToString(tunnel.hello.tlsSpkiSha256)}"

    private class TestGrpcServer(identity: TestIdentity) : Closeable {
        private val service = ServerServiceDefinition.builder(SERVICE_NAME)
            .addMethod(
                UNARY_METHOD,
                ServerCalls.asyncUnaryCall { request: ByteArray, observer: StreamObserver<ByteArray> ->
                    observer.onNext("unary:${request.decodeToString()}".toByteArray())
                    observer.onCompleted()
                }
            )
            .addMethod(
                SERVER_STREAM_METHOD,
                ServerCalls.asyncServerStreamingCall { request: ByteArray, observer: StreamObserver<ByteArray> ->
                    repeat(3) { index ->
                        observer.onNext("${request.decodeToString()}-${index + 1}".toByteArray())
                    }
                    observer.onCompleted()
                }
            )
            .addMethod(
                BIDI_METHOD,
                ServerCalls.asyncBidiStreamingCall { observer: StreamObserver<ByteArray> ->
                    object : StreamObserver<ByteArray> {
                        override fun onNext(value: ByteArray) {
                            observer.onNext("bidi:${value.decodeToString()}".toByteArray())
                        }

                        override fun onError(error: Throwable) = observer.onError(error)
                        override fun onCompleted() = observer.onCompleted()
                    }
                }
            )
            .build()
        private val grpcPort = ServerSocket(0, 16, LOOPBACK).use { it.localPort }
        private val server: Server = OkHttpServerBuilder
            .forPort(InetSocketAddress(LOOPBACK, grpcPort), identity.grpcServerCredentials())
            .addService(service)
            .build()

        val port: Int get() = grpcPort

        fun start(): TestGrpcServer {
            server.start()
            return this
        }

        override fun close() {
            server.shutdownNow().awaitTermination(5, TimeUnit.SECONDS)
        }
    }

    private class TestHttpServer(
        identity: TestIdentity,
        private val downloadBytes: ByteArray,
        private val requireAlpnH1: Boolean = true
    ) : Closeable {
        private val running = AtomicBoolean(true)
        private val executor = Executors.newCachedThreadPool()
        private val serverSocket = identity.socketServerContext().serverSocketFactory
            .createServerSocket(0, 16, LOOPBACK) as SSLServerSocket
        private val acceptThread = Thread(::acceptConnections, "bft-http-test-server").apply {
            isDaemon = true
        }
        val uploadSha256 = AtomicReference<String>()
        val acceptedConnections = AtomicInteger()
        val failure = AtomicReference<Throwable?>()
        val port: Int get() = serverSocket.localPort

        init {
            serverSocket.soTimeout = 500
        }

        fun start(): TestHttpServer {
            acceptThread.start()
            return this
        }

        private fun acceptConnections() {
            while (running.get()) {
                try {
                    val socket = serverSocket.accept() as SSLSocket
                    acceptedConnections.incrementAndGet()
                    executor.execute { handle(socket) }
                } catch (_: java.net.SocketTimeoutException) {
                    continue
                } catch (error: IOException) {
                    if (running.get()) failure.compareAndSet(null, error)
                }
            }
        }

        private fun handle(socket: SSLSocket) {
            socket.use {
                try {
                    configureApplicationProtocols(it, arrayOf("http/1.1"))
                    it.startHandshake()
                    if ((requireAlpnH1 && it.applicationProtocol != "http/1.1") ||
                        (!requireAlpnH1 && it.applicationProtocol != null && it.applicationProtocol != "http/1.1")
                    ) {
                        throw IOException(
                            "Unexpected HTTP ALPN '${it.applicationProtocol}', " +
                                "server protocols=${it.sslParameters.applicationProtocols.contentToString()}"
                        )
                    }
                    val input = it.inputStream
                    val requestLine = readHttpLine(input)
                    val headers = mutableMapOf<String, String>()
                    while (true) {
                        val line = readHttpLine(input)
                        if (line.isEmpty()) break
                        val separator = line.indexOf(':')
                        if (separator > 0) {
                            headers[line.substring(0, separator).trim().lowercase()] =
                                line.substring(separator + 1).trim()
                        }
                    }

                    val contentLength = headers["content-length"]?.toLongOrNull() ?: 0L
                    val path = requestLine.split(' ').getOrNull(1)
                    val responseBody = when (path) {
                        "/upload" -> {
                            uploadSha256.set(hex(readBodyDigest(input, contentLength)))
                            "ok".toByteArray()
                        }
                        "/download" -> downloadBytes
                        else -> ByteArray(0)
                    }
                    val status = if (path == "/upload" || path == "/download") "200 OK" else "404 Not Found"
                    val output = it.outputStream
                    output.write("HTTP/1.1 $status\r\n".toByteArray())
                    output.write("Content-Length: ${responseBody.size}\r\n".toByteArray())
                    output.write("Connection: close\r\n\r\n".toByteArray())
                    output.write(responseBody)
                    output.flush()
                } catch (error: Throwable) {
                    if (running.get()) failure.compareAndSet(null, error)
                }
            }
        }

        override fun close() {
            running.set(false)
            serverSocket.close()
            acceptThread.join(1_000)
            executor.shutdownNow()
            executor.awaitTermination(2, TimeUnit.SECONDS)
        }
    }

    private class TestSocketSink : Closeable {
        private val running = AtomicBoolean(true)
        private val serverSocket = ServerSocket(0, 16, LOOPBACK).apply { soTimeout = 500 }
        private val acceptedSockets = Collections.synchronizedList(mutableListOf<Socket>())
        private val acceptThread = Thread(::acceptConnections, "bft-url-test-socket-sink").apply {
            isDaemon = true
        }
        val port: Int get() = serverSocket.localPort

        fun start(): TestSocketSink {
            acceptThread.start()
            return this
        }

        private fun acceptConnections() {
            while (running.get()) {
                try {
                    acceptedSockets += serverSocket.accept()
                } catch (_: java.net.SocketTimeoutException) {
                    continue
                } catch (error: IOException) {
                    if (running.get()) throw error
                }
            }
        }

        override fun close() {
            running.set(false)
            serverSocket.close()
            synchronized(acceptedSockets) {
                acceptedSockets.forEach { runCatching { it.close() } }
            }
            acceptThread.join(1_000)
        }
    }

    internal class TestIdentity(
        val certificate: X509Certificate,
        private val privateKey: PrivateKey,
        private val keyManagers: Array<KeyManager>
    ) {
        val pin: String = BftTlsSocketFactory.spkiPin(certificate)

        fun grpcServerCredentials() = TlsServerCredentials.newBuilder()
            .keyManager(
                ByteArrayInputStream(pem("CERTIFICATE", certificate.encoded)),
                ByteArrayInputStream(pem("PRIVATE KEY", privateKey.encoded))
            )
            .build()

        fun socketServerContext(): SSLContext = SSLContext.getInstance("TLS").apply {
            init(keyManagers, null, null)
        }
    }

    private object ByteArrayMarshaller : MethodDescriptor.Marshaller<ByteArray> {
        override fun stream(value: ByteArray) = ByteArrayInputStream(value)
        override fun parse(stream: InputStream) = stream.readBytes()
    }

    private fun method(name: String, type: MethodDescriptor.MethodType): MethodDescriptor<ByteArray, ByteArray> =
        MethodDescriptor.newBuilder<ByteArray, ByteArray>()
            .setType(type)
            .setFullMethodName(MethodDescriptor.generateFullMethodName(SERVICE_NAME, name))
            .setRequestMarshaller(ByteArrayMarshaller)
            .setResponseMarshaller(ByteArrayMarshaller)
            .build()

    private fun createGrpcClient(
        port: Int,
        socketFactory: BftSocketFactory,
        tlsSocketFactory: BftTlsSocketFactory
    ): ManagedChannel = io.grpc.okhttp.OkHttpChannelBuilder
        .forAddress("127.0.0.1", port)
        .overrideAuthority(ORIGIN)
        .socketFactory(socketFactory)
        .sslSocketFactory(tlsSocketFactory)
        .build()

    private fun openLoopbackPipe(port: Int): BftSocketStreams {
        val socket = Socket()
        socket.tcpNoDelay = true
        socket.connect(InetSocketAddress(LOOPBACK, port), 5_000)
        return BftSocketStreams(socket.getInputStream(), socket.getOutputStream()) {
            socket.close()
        }
    }

    private fun readHttpLine(input: InputStream): String {
        val bytes = ByteArrayOutputStream()
        while (true) {
            val next = input.read()
            if (next < 0) {
                if (bytes.size() == 0) return ""
                throw EOFException("Unexpected end of HTTP headers")
            }
            if (next == '\r'.code) {
                if (input.read() != '\n'.code) throw IOException("Invalid HTTP line ending")
                return String(bytes.toByteArray(), Charsets.US_ASCII)
            }
            bytes.write(next)
        }
    }

    private fun readHttpResponseBody(input: InputStream): ByteArray {
        assertTrue(readHttpLine(input).startsWith("HTTP/1.1 200"))
        val headers = mutableMapOf<String, String>()
        while (true) {
            val line = readHttpLine(input)
            if (line.isEmpty()) break
            val separator = line.indexOf(':')
            if (separator > 0) {
                headers[line.substring(0, separator).trim().lowercase()] =
                    line.substring(separator + 1).trim()
            }
        }
        val length = checkNotNull(headers["content-length"]?.toIntOrNull())
        val body = ByteArray(length)
        var offset = 0
        while (offset < body.size) {
            val count = input.read(body, offset, body.size - offset)
            if (count < 0) throw EOFException("HTTP response ended before Content-Length")
            offset += count
        }
        return body
    }

    private fun configureApplicationProtocols(socket: SSLSocket, protocols: Array<String>) {
        val conscrypt = runCatching { Class.forName("org.conscrypt.Conscrypt") }.getOrNull()
        val isConscrypt = conscrypt
            ?.getMethod("isConscrypt", SSLSocket::class.java)
            ?.invoke(null, socket) as? Boolean ?: false
        if (isConscrypt) {
            conscrypt.getMethod("setApplicationProtocols", SSLSocket::class.java, Array<String>::class.java)
                .invoke(null, socket, protocols)
        } else {
            socket.sslParameters = socket.sslParameters.apply {
                applicationProtocols = protocols
            }
        }
    }

    private fun readBodyDigest(input: InputStream, length: Long): ByteArray {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(16 * 1024)
        var remaining = length
        while (remaining > 0) {
            val count = input.read(buffer, 0, minOf(buffer.size.toLong(), remaining).toInt())
            if (count < 0) throw EOFException("Upload ended before Content-Length")
            digest.update(buffer, 0, count)
            remaining -= count
        }
        return digest.digest()
    }

    private fun payload(size: Int): ByteArray = ByteArray(size) { ((it * 31 + 7) % 251).toByte() }

    private fun sha256(bytes: ByteArray): String = hex(MessageDigest.getInstance("SHA-256").digest(bytes))

    private fun hex(bytes: ByteArray): String = bytes.joinToString("") {
        "%02x".format(it.toInt() and 0xff)
    }

    private fun pem(type: String, der: ByteArray): ByteArray {
        val encoded = java.util.Base64.getMimeEncoder(64, byteArrayOf('\n'.code.toByte())).encodeToString(der)
        return "-----BEGIN $type-----\n$encoded\n-----END $type-----\n".toByteArray()
    }

    private fun Throwable.causeChain(): Sequence<Throwable> = generateSequence(this) { it.cause }
}
