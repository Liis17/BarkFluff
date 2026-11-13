package com.barkfluff.client.bft

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.BeforeClass
import org.junit.Test
import org.junit.Assert.assertThrows
import java.io.FilterInputStream
import java.io.BufferedInputStream
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
import java.nio.file.Files
import java.nio.file.Path
import java.util.Comparator
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference
import com.barkfluff.client.security.TlsPin
import com.barkfluff.client.security.TlsPinLookup
import com.barkfluff.client.security.TlsTransportFactory
import io.grpc.CallOptions
import io.grpc.MethodDescriptor
import io.grpc.Server
import io.grpc.ServerServiceDefinition
import io.grpc.Status
import io.grpc.StatusRuntimeException
import io.grpc.stub.ClientCalls
import io.grpc.stub.ServerCalls
import io.grpc.stub.StreamObserver
import okhttp3.Call
import okhttp3.Callback
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.Response
import okio.BufferedSink
import javax.net.SocketFactory
import javax.net.ssl.SSLServerSocket
import javax.net.ssl.SSLSocket

class BftExitInteropTest {
    private val activeStreamMethod = MethodDescriptor.newBuilder<ByteArray, ByteArray>()
        .setType(MethodDescriptor.MethodType.BIDI_STREAMING)
        .setFullMethodName("bft.test.LiveTransport/ActiveStream")
        .setRequestMarshaller(ByteArrayMarshaller)
        .setResponseMarshaller(ByteArrayMarshaller)
        .build()

    companion object {
        private const val UNKNOWN_KEY_TIMEOUT_MILLIS = 12_000

        @BeforeClass
        @JvmStatic
        fun installConscrypt() {
            val provider = org.conscrypt.Conscrypt.newProvider()
            if (java.security.Security.getProvider(provider.name) == null) {
                java.security.Security.insertProviderAt(provider, 1)
            }
        }
    }

    @Test
    fun kotlinClientInteroperatesWithSeparatelyBuiltDotNetExit() {
        val temporaryRoot = Files.createTempDirectory("bft-exit-interop-")
        var proxyProcess: Process? = null
        var tunnel: BftTunnel? = null
        val parsedSettings = mutableListOf<BftConnectionSettings>()

        try {
            val sdkVersion = findDotnet10Sdk(temporaryRoot)
            assumeTrue(".NET 10 SDK is unavailable; Exit interoperability was not run", sdkVersion != null)

            val repositoryRoot = findRepositoryRoot()
            val sourceRoot = temporaryRoot.resolve("source")
            val proxySource = sourceRoot.resolve("Backend/BarkFluff.BftProxy")
            val bftSource = sourceRoot.resolve("Shared/BarkFluff.Bft")
            copySourceTree(repositoryRoot.resolve("Backend/BarkFluff.BftProxy"), proxySource)
            copySourceTree(repositoryRoot.resolve("Shared/BarkFluff.Bft"), bftSource)
            Files.write(
                sourceRoot.resolve("global.json"),
                """{"sdk":{"version":"$sdkVersion","rollForward":"disable"}}""".toByteArray(Charsets.UTF_8)
            )

            val proxyOutput = temporaryRoot.resolve("publish")
            Files.createDirectories(proxyOutput)
            buildProxy(sourceRoot, proxySource.resolve("BarkFluff.BftProxy.csproj"), proxyOutput, temporaryRoot.resolve("build.log"))
            val proxyDll = proxyOutput.resolve("BarkFluff.BftProxy.dll")
            assertTrue("Exit build output is missing", Files.isRegularFile(proxyDll))

            val keyFile = temporaryRoot.resolve("client-keys.txt")
            Files.write(keyFile, ByteArray(0))
            val listenPort = freePort()
            val cliEnvironment = mapOf(
                "BFT_STATIC_PRIVATE_KEY" to generatedStaticPrivateKey(proxyDll, temporaryRoot),
                "BFT_CLIENT_KEYS_FILE" to keyFile.toString()
            )
            val validLink = addClient(proxyDll, temporaryRoot, cliEnvironment, "allowed", listenPort)
            val unknownKeyLink = addClient(proxyDll, temporaryRoot, cliEnvironment, "unknown", listenPort)
            parsedSettings += BftConnectionString.parse(validLink)
            parsedSettings += BftConnectionString.parse(unknownKeyLink)
            val validKeyLine = Files.readAllLines(keyFile).single { it.startsWith("allowed ") }
            Files.write(keyFile, "$validKeyLine\n".toByteArray(Charsets.UTF_8))

            val upstreamBeaconPort = freePort()
            val upstreamIdentityPort = freePort()
            val proxyEnvironment = cliEnvironment + mapOf(
                "BFT_MODE" to "Exit",
                "BFT_LISTEN" to "127.0.0.1:$listenPort",
                "BFT_PROXY_ID" to "kotlin-interop-test",
                "BFT_ORIGIN_SECRET" to "a".repeat(64),
                "BFT_UPSTREAM_BEACON" to "https://127.0.0.1:$upstreamBeaconPort",
                "BFT_UPSTREAM_IDENTITY" to "https://127.0.0.1:$upstreamIdentityPort"
            )
            proxyProcess = startProxy(proxyDll, temporaryRoot, proxyEnvironment)
            awaitListening(proxyProcess, listenPort)

            assertUnknownKeyGetsNoResponse(listenPort, unknownKeyLink)

            val socket = Socket()
            socket.connect(InetSocketAddress(InetAddress.getByName("127.0.0.1"), listenPort), 5_000)
            tunnel = BftTunnel.connect(socket, validLink)
            assertEquals("kotlin-interop-test", tunnel.hello.proxyId)
            assertEquals(listOf(1, 2), tunnel.hello.serviceIds)
            assertEquals(32, tunnel.hello.tlsSpkiSha256.size)
            assertThrows(BftServiceUnavailableException::class.java) { tunnel.openStream(3) }

            BftTransportTestSupport.verifyExitGrpcUnavailable(tunnel, 1)
            BftTransportTestSupport.verifyExitHttp11ReturnsBadGateway(tunnel, 2)
            BftTransportTestSupport.verifyWrongPinRejectedOverBftTunnel(tunnel, 1)
            assertEquals(BftTunnel.State.READY, tunnel.state)

            verifyProductionTransport(validLink, upstreamBeaconPort, managerStore = MemorySettingsStore())

            Files.write(keyFile, ByteArray(0))
            val revokedReason = tunnel.completion.get(15, TimeUnit.SECONDS)
            assertEquals(BftTunnelCloseReason.Kind.GOAWAY, revokedReason.kind)
            assertEquals(BftGoAwayCode.SHUTDOWN, revokedReason.goAway?.code)
        } finally {
            runCatching { tunnel?.close() }
            proxyProcess?.let(::stopOwnedProcess)
            parsedSettings.forEach {
                it.staticPublicKey.fill(0)
                it.obfsKey.fill(0)
            }
            deleteTree(temporaryRoot)
        }
    }

    @Test
    fun productionFactoriesFailActiveGrpcAndUploadAcrossProxyChangesWithoutDirectFallbackOrRetries() {
        val temporaryRoot = Files.createTempDirectory("bft-exit-live-transport-")
        var proxyProcess: Process? = null
        var grpcUpstream: ActiveGrpcUpstream? = null
        var httpUpstream: ActiveUploadUpstream? = null
        var manager: BftProxyManager? = null
        val parsedSettings = mutableListOf<BftConnectionSettings>()
        try {
            val sdkVersion = findDotnet10Sdk(temporaryRoot)
            assumeTrue(".NET 10 SDK is unavailable; Exit active-transport test was not run", sdkVersion != null)

            val repositoryRoot = findRepositoryRoot()
            val sourceRoot = temporaryRoot.resolve("source")
            val proxySource = sourceRoot.resolve("Backend/BarkFluff.BftProxy")
            copySourceTree(repositoryRoot.resolve("Backend/BarkFluff.BftProxy"), proxySource)
            copySourceTree(repositoryRoot.resolve("Shared/BarkFluff.Bft"), sourceRoot.resolve("Shared/BarkFluff.Bft"))
            Files.write(
                sourceRoot.resolve("global.json"),
                """{"sdk":{"version":"$sdkVersion","rollForward":"disable"}}""".toByteArray(Charsets.UTF_8)
            )
            val proxyOutput = temporaryRoot.resolve("publish")
            Files.createDirectories(proxyOutput)
            buildProxy(sourceRoot, proxySource.resolve("BarkFluff.BftProxy.csproj"), proxyOutput, temporaryRoot.resolve("build.log"))
            val proxyDll = proxyOutput.resolve("BarkFluff.BftProxy.dll")
            assertTrue("Exit build output is missing", Files.isRegularFile(proxyDll))

            val identity = createBftInternalTestIdentity(temporaryRoot)
            val pemFile = temporaryRoot.resolve("bft-test-root.pem")
            val pemBody = java.util.Base64.getMimeEncoder(64, "\n".toByteArray()).encodeToString(identity.certificate.encoded)
            Files.write(pemFile, "-----BEGIN CERTIFICATE-----\n$pemBody\n-----END CERTIFICATE-----\n".toByteArray(Charsets.UTF_8))

            val runningGrpcUpstream = ActiveGrpcUpstream(identity, activeStreamMethod).start()
            val runningHttpUpstream = ActiveUploadUpstream(identity).start()
            grpcUpstream = runningGrpcUpstream
            httpUpstream = runningHttpUpstream
            val listenPort = freePort()
            val keyFile = temporaryRoot.resolve("client-keys.txt")
            Files.write(keyFile, ByteArray(0))
            val cliEnvironment = mapOf(
                "BFT_STATIC_PRIVATE_KEY" to generatedStaticPrivateKey(proxyDll, temporaryRoot),
                "BFT_CLIENT_KEYS_FILE" to keyFile.toString()
            )
            val firstLink = addClient(proxyDll, temporaryRoot, cliEnvironment, "active-first", listenPort)
            val replacementLink = addClient(proxyDll, temporaryRoot, cliEnvironment, "active-replacement", listenPort)
            parsedSettings += BftConnectionString.parse(firstLink)
            parsedSettings += BftConnectionString.parse(replacementLink)
            val allowedKeys = Files.readAllLines(keyFile).filter { it.startsWith("active-first ") || it.startsWith("active-replacement ") }
            assertEquals("both temporary test links must be allowlisted", 2, allowedKeys.size)
            Files.write(keyFile, allowedKeys.joinToString("\n", postfix = "\n").toByteArray(Charsets.UTF_8))

            val proxyEnvironment = cliEnvironment + mapOf(
                "BFT_MODE" to "Exit",
                "BFT_LISTEN" to "127.0.0.1:$listenPort",
                "BFT_PROXY_ID" to "active-transport-test",
                "BFT_ORIGIN_SECRET" to "b".repeat(64),
                "BFT_UPSTREAM_BEACON" to "https://127.0.0.1:${runningGrpcUpstream.port}",
                "BFT_UPSTREAM_FILES" to "https://127.0.0.1:${runningHttpUpstream.port}",
                "SSL_CERT_FILE" to pemFile.toString()
            )
            val runningProxy = startProxy(proxyDll, temporaryRoot, proxyEnvironment)
            proxyProcess = runningProxy
            awaitListening(runningProxy, listenPort)

            val directSocketAttempts = AtomicInteger()
            val directDnsAttempts = AtomicInteger()
            val memoryStore = MemorySettingsStore()
            val proxyManager = BftProxyManager(memoryStore, ::connectTestExit)
            manager = proxyManager
            proxyManager.saveConnectionString(firstLink, enable = true)
            proxyManager.setPersistentNetworkDemand(true)
            val transport = TlsTransportFactory(
                object : TlsPinLookup { override fun pinFor(host: String): TlsPin? = null },
                proxyManager,
                null,
                CountingDirectSocketFactory(directSocketAttempts),
                object : okhttp3.Dns {
                    override fun lookup(hostname: String): List<InetAddress> {
                        directDnsAttempts.incrementAndGet()
                        throw UnknownHostException(hostname)
                    }
                }
            )

            try {
                exerciseActiveTransportChanges(
                    proxyManager,
                    transport,
                    firstLink,
                    replacementLink,
                    runningGrpcUpstream,
                    runningHttpUpstream,
                    directSocketAttempts,
                    directDnsAttempts
                )
            } catch (failure: AssertionError) {
                val proxyLog = temporaryRoot.resolve("proxy.log")
                    .takeIf(Files::isRegularFile)
                    ?.let { String(Files.readAllBytes(it), Charsets.UTF_8).lowercase() }
                    .orEmpty()
                val tlsFailureMarkers = listOf("certificate", "untrusted", "trust relationship", "ssl", "tls", "authentication")
                    .filter(proxyLog::contains)
                val upstreamTlsTrustUnavailable = runningGrpcUpstream.requestCount.get() == 0 &&
                    runningHttpUpstream.requestCount.get() == 0 &&
                    tlsFailureMarkers.isNotEmpty()
                if (!upstreamTlsTrustUnavailable) {
                    throw AssertionError(
                        "Upstream probe failed without a recognizable TLS trust error " +
                            "(gRPC requests=${runningGrpcUpstream.requestCount.get()}, HTTP requests=${runningHttpUpstream.requestCount.get()}, " +
                            "proxy log length=${proxyLog.length}, TLS markers=${tlsFailureMarkers.joinToString()}).",
                        failure
                    )
                }

                System.out.println("Exit upstream TLS trust is unavailable with SSL_CERT_FILE on this host; running the same transport-change test against the BFT frame responder.")
                proxyManager.setPersistentNetworkDemand(false)
                manager = null
                stopOwnedProcess(runningProxy)
                proxyProcess = null
                runningGrpcUpstream.close()
                grpcUpstream = null
                runningHttpUpstream.close()
                httpUpstream = null
                runActiveTransportChangesAgainstFramePeer(identity, directSocketAttempts, directDnsAttempts)
            }
        } finally {
            runCatching { manager?.setPersistentNetworkDemand(false) }
            runCatching { grpcUpstream?.close() }
            runCatching { httpUpstream?.close() }
            proxyProcess?.let(::stopOwnedProcess)
            parsedSettings.forEach {
                it.staticPublicKey.fill(0)
                it.obfsKey.fill(0)
            }
            deleteTree(temporaryRoot)
        }
    }

    @Test
    fun productionFactoriesFailActiveGrpcAndUploadAcrossProxyChangesWithFrameResponder() {
        val temporaryRoot = Files.createTempDirectory("bft-frame-live-transport-")
        try {
            runActiveTransportChangesAgainstFramePeer(
                createBftInternalTestIdentity(temporaryRoot),
                AtomicInteger(),
                AtomicInteger()
            )
        } finally {
            deleteTree(temporaryRoot)
        }
    }

    private fun exerciseActiveTransportChanges(
        manager: BftProxyManager,
        transport: TlsTransportFactory,
        firstLink: String,
        replacementLink: String,
        grpcUpstream: ActiveGrpcUpstream,
        httpUpstream: ActiveUploadUpstream,
        directSocketAttempts: AtomicInteger,
        directDnsAttempts: AtomicInteger
    ) {
        manager.saveConnectionString(firstLink, enable = true)
        manager.setPersistentNetworkDemand(true)

        val firstRound = startActiveRound(transport)
        try {
            firstRound.awaitUpstreamRequests(grpcUpstream, httpUpstream)
            manager.setEnabled(false)
            manager.setEnabled(true)
            firstRound.releaseUpload()
            firstRound.assertFailed()
        } finally {
            firstRound.close()
        }

        val secondRound = startActiveRound(transport)
        try {
            secondRound.awaitUpstreamRequests(grpcUpstream, httpUpstream)
            manager.saveConnectionString(replacementLink, enable = true)
            secondRound.releaseUpload()
            secondRound.assertFailed()
        } finally {
            secondRound.close()
        }

        val replacementRound = startActiveRound(transport)
        try {
            replacementRound.awaitUpstreamRequests(grpcUpstream, httpUpstream)
            assertEquals("each non-idempotent gRPC request reached upstream exactly once", 3, grpcUpstream.requestCount.get())
            assertEquals("each HTTP upload reached upstream exactly once", 3, httpUpstream.requestCount.get())
            assertEquals("proxy-enabled production factories made no direct socket attempt", 0, directSocketAttempts.get())
            assertEquals("proxy-enabled production factories made no direct DNS attempt", 0, directDnsAttempts.get())
            manager.setEnabled(false)
            replacementRound.releaseUpload()
            replacementRound.assertFailed()
        } finally {
            replacementRound.close()
        }
    }

    private fun runActiveTransportChangesAgainstFramePeer(
        identity: BftTransportTestSupport.TestIdentity,
        directSocketAttempts: AtomicInteger,
        directDnsAttempts: AtomicInteger
    ) {
        val probeRoute = AtomicReference<Closeable?>()
        try {
            BftTransportTestSupport.verifyGrpcOverBftMux(
                identity,
                routeFactory = { port, opened ->
                    val peer = BftMuxTestPeer(upstreamPorts = mapOf(1 to port))
                    val tunnel = peer.connect()
                    probeRoute.set(Closeable { tunnel.close(); peer.close() })
                    BftSocketFactory {
                        opened.incrementAndGet()
                        tunnel.openStream(1).asSocketStreams()
                    }
                },
                closeRoute = { probeRoute.getAndSet(null)?.close() }
            )
        } finally {
            probeRoute.getAndSet(null)?.close()
        }
        val grpcUpstream = ActiveGrpcUpstream(identity, activeStreamMethod).start()
        val httpUpstream = ActiveUploadUpstream(identity).start()
        val peers = java.util.concurrent.CopyOnWriteArrayList<BftMuxTestPeer>()
        val probePeer = BftMuxTestPeer(upstreamPorts = mapOf(1 to grpcUpstream.port, 5 to httpUpstream.port))
        val probeTunnel = probePeer.connect()
        val serviceIds = probeTunnel.hello.serviceIds
        probeTunnel.close()
        probePeer.close()
        assertTrue("frame responder must advertise gRPC and upload services; advertised $serviceIds", 1 in serviceIds && 5 in serviceIds)
        val link = vectorConnectionString("frame-peer-first")
        val replacementLink = vectorConnectionString("frame-peer-replacement")
        val testSpkiPin = java.util.Base64.getDecoder().decode(identity.pin.substringAfter('/'))
        val manager = BftProxyManager(MemorySettingsStore(), { _ ->
            BftMuxTestPeer(
                upstreamPorts = mapOf(1 to grpcUpstream.port, 5 to httpUpstream.port),
                tlsSpkiSha256Override = testSpkiPin
            )
                .also(peers::add)
                .connect()
        })
        val transport = TlsTransportFactory(
            object : TlsPinLookup { override fun pinFor(host: String): TlsPin? = null },
            manager,
            null,
            CountingDirectSocketFactory(directSocketAttempts),
            object : okhttp3.Dns {
                override fun lookup(hostname: String): List<InetAddress> {
                    directDnsAttempts.incrementAndGet()
                    throw UnknownHostException(hostname)
                }
            }
        )
        try {
            exerciseActiveTransportChanges(
                manager,
                transport,
                link,
                replacementLink,
                grpcUpstream,
                httpUpstream,
                directSocketAttempts,
                directDnsAttempts
            )
        } finally {
            runCatching { manager.setPersistentNetworkDemand(false) }
            peers.forEach { runCatching { it.close() } }
            grpcUpstream.close()
            httpUpstream.close()
        }
    }

    private fun vectorConnectionString(name: String): String {
        val vector = BftVectors.objectAt(BftVectors.root, "connectionString")
        val settings = BftConnectionSettings(
            BftVectors.hexAt(vector, "staticPublicKey"),
            BftVectors.hexAt(vector, "obfsKey"),
            "proxy.example.org",
            8443,
            name = name
        )
        return try {
            settings.format()
        } finally {
            settings.staticPublicKey.fill(0)
            settings.obfsKey.fill(0)
        }
    }

    private fun createBftInternalTestIdentity(temporaryRoot: Path): BftTransportTestSupport.TestIdentity {
        val source = checkNotNull(javaClass.getResourceAsStream("/bft-test-identity.p12"))
        val keyStoreFile = temporaryRoot.resolve("bft-internal-test-identity.p12")
        source.use { Files.copy(it, keyStoreFile) }
        val originalIdentity = checkNotNull(javaClass.getResourceAsStream("/bft-test-identity.p12"))
            .let(BftTransportTestSupport::loadIdentity)
        val keytool = Path.of(
            System.getProperty("java.home"),
            "bin",
            if (System.getProperty("os.name").orEmpty().startsWith("Windows", ignoreCase = true)) "keytool.exe" else "keytool"
        )
        val keytoolLog = temporaryRoot.resolve("keytool.log")
        val process = ProcessBuilder(
            keytool.toString(),
            "-selfcert",
            "-alias", "bft-test",
            "-dname", "CN=bft.internal",
            "-validity", "365",
            "-ext", "SAN=dns:bft.internal,dns:bft-origin.invalid,ip:127.0.0.1",
            "-keystore", keyStoreFile.toString(),
            "-storetype", "PKCS12",
            "-storepass", "bft-test-only",
            "-keypass", "bft-test-only"
        ).redirectErrorStream(true).redirectOutput(keytoolLog.toFile()).start()
        if (!process.waitFor(30, TimeUnit.SECONDS)) {
            process.destroyForcibly()
            throw AssertionError("Generating the temporary BFT test identity timed out")
        }
        if (process.exitValue() != 0) {
            val detail = String(Files.readAllBytes(keytoolLog), Charsets.UTF_8).takeLast(2_000)
            throw AssertionError("Generating the temporary BFT test identity failed (exit ${process.exitValue()}): $detail")
        }
        val identity = Files.newInputStream(keyStoreFile).use(BftTransportTestSupport::loadIdentity)
        assertEquals("temporary BFT identity must preserve the pinned test key", originalIdentity.pin, identity.pin)
        return identity
    }

    private fun startActiveRound(transport: TlsTransportFactory): ActiveRound {
        val channel = transport.createGrpcChannel("https://bft.internal:443", serviceId = 1)
        val grpcDone = CountDownLatch(1)
        val grpcError = AtomicReference<Throwable?>()
        val requestObserver = ClientCalls.asyncBidiStreamingCall(
            channel.newCall(activeStreamMethod, CallOptions.DEFAULT.withDeadlineAfter(60, TimeUnit.SECONDS)),
            object : StreamObserver<ByteArray> {
                override fun onNext(value: ByteArray) = Unit
                override fun onError(error: Throwable) {
                    grpcError.set(error)
                    grpcDone.countDown()
                }
                override fun onCompleted() = grpcDone.countDown()
            }
        )
        requestObserver.onNext(byteArrayOf(7, 3, 1))

        val uploadGate = CountDownLatch(1)
        val uploadBody = GatedUploadBody(uploadGate)
        val uploadUrl = "https://bft.internal:443/upload".toHttpUrl()
        val httpClient = transport.createOkHttpClientForService(uploadUrl, serviceId = 5) {
            protocols(listOf(Protocol.HTTP_1_1))
            retryOnConnectionFailure(false)
        }
        val httpDone = CountDownLatch(1)
        val httpError = AtomicReference<Throwable?>()
        val httpStatus = AtomicInteger(0)
        val uploadCall = httpClient.newCall(
            Request.Builder().url(uploadUrl).post(uploadBody).build()
        )
        uploadCall.enqueue(object : Callback {
            override fun onFailure(call: Call, e: IOException) {
                httpError.set(e)
                httpDone.countDown()
            }

            override fun onResponse(call: Call, response: Response) {
                response.use { httpStatus.set(it.code) }
                httpDone.countDown()
            }
        })
        return ActiveRound(
            channel,
            requestObserver,
            grpcDone,
            grpcError,
            httpClient,
            uploadBody,
            uploadCall,
            httpDone,
            httpError,
            httpStatus
        )
    }

    private fun connectTestExit(connectionString: String): BftTunnel {
        val settings = BftConnectionString.parse(connectionString)
        val socket = Socket()
        return try {
            socket.connect(InetSocketAddress(settings.host, settings.port), 5_000)
            BftTunnel.connect(socket, connectionString)
        } catch (error: Throwable) {
            runCatching { socket.close() }
            throw error
        } finally {
            settings.staticPublicKey.fill(0)
            settings.obfsKey.fill(0)
        }
    }

    private fun verifyProductionTransport(
        link: String,
        directPort: Int,
        managerStore: MemorySettingsStore
    ) {
        var proxyConnections = 0
        val manager = BftProxyManager(managerStore, { connectionString ->
            proxyConnections++
            val settings = BftConnectionString.parse(connectionString)
            try {
                val socket = Socket()
                socket.connect(InetSocketAddress(settings.host, settings.port), 5_000)
                BftTunnel.connect(socket, connectionString)
            } finally {
                settings.staticPublicKey.fill(0)
                settings.obfsKey.fill(0)
            }
        })
        manager.saveConnectionString(link, enable = true)
        manager.setPersistentNetworkDemand(true)
        val transport = TlsTransportFactory(object : TlsPinLookup {
            override fun pinFor(host: String): TlsPin? = null
        }, manager)
        val descriptor = MethodDescriptor.newBuilder<ByteArray, ByteArray>()
            .setType(MethodDescriptor.MethodType.UNARY)
            .setFullMethodName("bft.test.Transport/Unary")
            .setRequestMarshaller(ByteArrayMarshaller)
            .setResponseMarshaller(ByteArrayMarshaller)
            .build()
        val channel = transport.createGrpcChannel("https://bft.internal:443", serviceId = 1)
        try {
            val failure = assertThrows(StatusRuntimeException::class.java) {
                ClientCalls.blockingUnaryCall(
                    channel,
                    descriptor,
                    CallOptions.DEFAULT.withDeadlineAfter(10, TimeUnit.SECONDS),
                    "production-grpc".toByteArray()
                )
            }
            assertEquals(Status.Code.UNAVAILABLE, failure.status.code)
            assertTrue("production gRPC response did not come from Exit", failure.status.description?.contains("502") == true)
        } finally {
            channel.shutdownNow().awaitTermination(5, TimeUnit.SECONDS)
        }

        val httpUrl = "http://bft.internal:80/".toHttpUrl()
        val httpClient = transport.createOkHttpClientForService(httpUrl, 2) {
            protocols(listOf(Protocol.HTTP_1_1))
            retryOnConnectionFailure(false)
        }
        try {
            httpClient.newCall(Request.Builder().url(httpUrl).build()).execute().use { response ->
                assertEquals(502, response.code)
            }
        } finally {
            httpClient.connectionPool.evictAll()
            httpClient.dispatcher.executorService.shutdownNow()
        }
        assertEquals("production gRPC and HTTP reused the app's one tunnel", 1, proxyConnections)

        manager.setEnabled(false)
        val directClient = transport.createOkHttpClient("https://127.0.0.1:$directPort/".toHttpUrl())
        try {
            assertThrows(IOException::class.java) {
                directClient.newCall(Request.Builder().url("https://127.0.0.1:$directPort/").build()).execute()
            }
        } finally {
            directClient.dispatcher.executorService.shutdownNow()
            directClient.connectionPool.evictAll()
        }
        assertEquals("disabled proxy did not open another BFT connection", 1, proxyConnections)
        manager.setPersistentNetworkDemand(false)
    }

    private class ActiveGrpcUpstream(
        identity: BftTransportTestSupport.TestIdentity,
        method: MethodDescriptor<ByteArray, ByteArray>
    ) : Closeable {
        val requestCount = AtomicInteger()
        private val requests = LinkedBlockingQueue<Unit>()
        private val service = ServerServiceDefinition.builder("bft.test.LiveTransport")
            .addMethod(
                method,
                ServerCalls.asyncBidiStreamingCall { responseObserver: StreamObserver<ByteArray> ->
                    val counted = AtomicBoolean()
                    object : StreamObserver<ByteArray> {
                        override fun onNext(value: ByteArray) {
                            if (counted.compareAndSet(false, true)) {
                                requestCount.incrementAndGet()
                                requests.offer(Unit)
                            }
                        }

                        override fun onError(error: Throwable) = Unit
                        override fun onCompleted() = Unit
                    }
                }
            )
            .build()
        private val server: Server = io.grpc.okhttp.OkHttpServerBuilder
            .forPort(0, identity.grpcServerCredentials())
            .addService(service)
            .build()
        val port: Int get() = server.port

        fun start(): ActiveGrpcUpstream {
            server.start()
            return this
        }

        fun awaitRequest(clientError: Throwable?) {
            if (requests.poll(15, TimeUnit.SECONDS) == null) {
                val status = (clientError as? StatusRuntimeException)?.status
                val causes = generateSequence(clientError) { it.cause }
                    .map { it.javaClass.simpleName }
                    .joinToString(" -> ")
                val causeMessages = generateSequence(clientError) { it.cause }
                    .mapNotNull { it.message }
                    .joinToString(" | ")
                throw AssertionError(
                    "gRPC upload did not reach its upstream; client status=${status?.code}, " +
                        "description=${status?.description}, causes=$causes, details=$causeMessages"
                )
            }
        }

        override fun close() {
            server.shutdownNow().awaitTermination(5, TimeUnit.SECONDS)
        }
    }

    private class ActiveUploadUpstream(identity: BftTransportTestSupport.TestIdentity) : Closeable {
        private val running = AtomicBoolean(true)
        private val executor = Executors.newCachedThreadPool()
        private val requests = LinkedBlockingQueue<Unit>()
        private val bodyBytes = CountDownLatch(1)
        private val serverSocket = identity.socketServerContext().serverSocketFactory
            .createServerSocket(0, 16, InetAddress.getByName("127.0.0.1")) as SSLServerSocket
        private val acceptThread = Thread(::acceptConnections, "bft-active-upload-upstream").apply {
            isDaemon = true
        }
        val requestCount = AtomicInteger()
        val port: Int get() = serverSocket.localPort

        init {
            serverSocket.soTimeout = 500
        }

        fun start(): ActiveUploadUpstream {
            acceptThread.start()
            return this
        }

        fun awaitRequest() {
            assertNotNull("HTTP upload did not reach the .NET Exit upstream", requests.poll(15, TimeUnit.SECONDS))
        }

        fun awaitBodyByte() {
            assertTrue("HTTP upload body did not reach the .NET Exit upstream", bodyBytes.await(15, TimeUnit.SECONDS))
        }

        private fun acceptConnections() {
            while (running.get()) {
                try {
                    val socket = serverSocket.accept() as SSLSocket
                    executor.execute { holdRequestOpen(socket) }
                } catch (_: java.net.SocketTimeoutException) {
                    continue
                } catch (error: IOException) {
                    if (running.get()) throw error
                }
            }
        }

        private fun holdRequestOpen(socket: SSLSocket) {
            socket.use { tlsSocket ->
                try {
                    val parameters = tlsSocket.sslParameters
                    parameters.applicationProtocols = arrayOf("http/1.1")
                    tlsSocket.sslParameters = parameters
                    tlsSocket.startHandshake()
                    val input = BufferedInputStream(tlsSocket.inputStream)
                    val requestLine = readHttpLine(input)
                    check(requestLine.startsWith("POST ")) { "Expected an HTTP upload request" }
                    while (readHttpLine(input).isNotEmpty()) Unit
                    requestCount.incrementAndGet()
                    requests.offer(Unit)
                    if (input.read() >= 0) {
                        bodyBytes.countDown()
                        while (input.read() >= 0) Unit
                    }
                } catch (_: IOException) {
                    // Closing the proxy tunnel interrupts the deliberately unfinished upload.
                }
            }
        }

        private fun readHttpLine(input: InputStream): String {
            val line = ByteArrayOutputStream()
            while (true) {
                val value = input.read()
                if (value < 0) throw EOFException("HTTP request ended before its headers")
                if (value == '\n'.code) break
                if (value != '\r'.code) line.write(value)
            }
            return line.toString(Charsets.US_ASCII.name())
        }

        override fun close() {
            running.set(false)
            serverSocket.close()
            acceptThread.join(1_000)
            executor.shutdownNow()
            executor.awaitTermination(2, TimeUnit.SECONDS)
        }
    }

    private class GatedUploadBody(private val release: CountDownLatch) : RequestBody() {
        private val started = CountDownLatch(1)
        override fun contentType() = "application/octet-stream".toMediaType()
        override fun contentLength() = UPLOAD_BYTES.toLong()

        override fun writeTo(sink: BufferedSink) {
            sink.writeByte(0x42).flush()
            started.countDown()
            if (!release.await(30, TimeUnit.SECONDS)) throw IOException("Test upload gate timed out")
            val remaining = ByteArray(UPLOAD_BYTES - 1) { 0x2a }
            sink.write(remaining)
        }

        fun awaitStarted() {
            assertTrue("HTTP request body did not start", started.await(15, TimeUnit.SECONDS))
        }

        fun release() = release.countDown()

        private companion object {
            const val UPLOAD_BYTES = 512 * 1024
        }
    }

    private class ActiveRound(
        private val channel: io.grpc.ManagedChannel,
        private val requestObserver: StreamObserver<ByteArray>,
        private val grpcDone: CountDownLatch,
        private val grpcError: AtomicReference<Throwable?>,
        private val httpClient: okhttp3.OkHttpClient,
        private val uploadBody: GatedUploadBody,
        private val uploadCall: Call,
        private val httpDone: CountDownLatch,
        private val httpError: AtomicReference<Throwable?>,
        private val httpStatus: AtomicInteger
    ) {
        fun awaitUpstreamRequests(grpc: ActiveGrpcUpstream, http: ActiveUploadUpstream) {
            grpc.awaitRequest(grpcError.get())
            http.awaitRequest()
            uploadBody.awaitStarted()
            http.awaitBodyByte()
        }

        fun releaseUpload() = uploadBody.release()

        fun assertFailed() {
            assertTrue("active gRPC stream did not terminate", grpcDone.await(15, TimeUnit.SECONDS))
            assertNotNull("active gRPC stream completed without an error", grpcError.get())
            assertTrue("active HTTP upload did not terminate", httpDone.await(15, TimeUnit.SECONDS))
            assertTrue(
                "active HTTP upload succeeded with status ${httpStatus.get()}",
                httpError.get() != null || httpStatus.get() >= 400
            )
        }

        fun close() {
            releaseUpload()
            runCatching { requestObserver.onError(IOException("Test round finished")) }
            uploadCall.cancel()
            channel.shutdownNow().awaitTermination(5, TimeUnit.SECONDS)
            httpClient.dispatcher.cancelAll()
            httpClient.connectionPool.evictAll()
            httpClient.dispatcher.executorService.shutdownNow()
        }
    }

    private class CountingDirectSocketFactory(private val attempts: AtomicInteger) : SocketFactory() {
        private fun rejected(): Nothing {
            attempts.incrementAndGet()
            throw SocketException("Direct socket attempted with BFT proxy enabled")
        }

        override fun createSocket(): Socket = rejected()
        override fun createSocket(host: String, port: Int): Socket = rejected()
        override fun createSocket(host: String, port: Int, localHost: InetAddress, localPort: Int): Socket = rejected()
        override fun createSocket(host: InetAddress, port: Int): Socket = rejected()
        override fun createSocket(address: InetAddress, port: Int, localAddress: InetAddress, localPort: Int): Socket = rejected()
    }

    private object ByteArrayMarshaller : MethodDescriptor.Marshaller<ByteArray> {
        override fun stream(value: ByteArray) = java.io.ByteArrayInputStream(value)
        override fun parse(stream: InputStream) = stream.readBytes()
    }

    private class MemorySettingsStore : BftProxyManager.BftProxySettingsStore {
        private var settings: BftProxyManager.BftProxyStoredSettings? = null
        override fun read() = settings
        override fun write(connectionString: String, name: String, enabled: Boolean) {
            settings = BftProxyManager.BftProxyStoredSettings(connectionString, name, enabled)
        }
        override fun clear() { settings = null }
    }

    private fun assertUnknownKeyGetsNoResponse(port: Int, link: String) {
        val socket = Socket()
        socket.connect(InetSocketAddress(InetAddress.getByName("127.0.0.1"), port), 5_000)
        socket.soTimeout = UNKNOWN_KEY_TIMEOUT_MILLIS
        val countedInput = CountingInputStream(socket.getInputStream())
        try {
            assertThrows(BftTunnelConnectException::class.java) {
                BftTunnel.connect(
                    countedInput,
                    socket.getOutputStream(),
                    link,
                    BftTunnelOptions(handshakeTimeoutMillis = UNKNOWN_KEY_TIMEOUT_MILLIS.toLong())
                )
            }
            assertEquals("Exit returned bytes for a client key that is not allowlisted", 0, countedInput.bytesRead)
        } finally {
            runCatching { socket.close() }
        }
    }

    private fun findRepositoryRoot(): Path {
        var current = Path.of(System.getProperty("user.dir")).toAbsolutePath()
        while (!Files.isRegularFile(current.resolve("Backend/BarkFluff.BftProxy/BarkFluff.BftProxy.csproj"))) {
            current = current.parent ?: error("Could not find repository root for BFT Exit build")
        }
        return current
    }

    private fun findDotnet10Sdk(workingDirectory: Path): String? {
        val process = try {
            ProcessBuilder("dotnet", "--list-sdks").directory(workingDirectory.toFile()).start()
        } catch (_: IOException) {
            return null
        }
        val output = process.inputStream.bufferedReader().use { it.readText() }
        if (!process.waitFor(10, TimeUnit.SECONDS) || process.exitValue() != 0) return null
        val versions = Regex("(?m)^\\s*(10\\.0\\.\\d+)\\s+\\[")
            .findAll(output)
            .map { it.groupValues[1] }
            .toList()
        return versions.maxWithOrNull(compareBy<String> { it.substringAfterLast('.').toInt() })
    }

    private fun copySourceTree(source: Path, destination: Path) {
        Files.walk(source).use { paths ->
            paths.filter { path ->
                val relative = source.relativize(path)
                !relative.any { it.toString() in setOf("bin", "obj", ".git") }
            }.forEach { path ->
                val target = destination.resolve(source.relativize(path))
                if (Files.isDirectory(path)) Files.createDirectories(target)
                else {
                    Files.createDirectories(target.parent)
                    Files.copy(path, target)
                }
            }
        }
    }

    private fun buildProxy(sourceRoot: Path, projectFile: Path, output: Path, log: Path) {
        val process = ProcessBuilder(
            "dotnet", "build", projectFile.toString(), "--configuration", "Release", "--output", output.toString(), "--nologo", "--verbosity", "quiet"
        ).directory(sourceRoot.toFile()).redirectErrorStream(true).redirectOutput(log.toFile()).start()
        if (!process.waitFor(5, TimeUnit.MINUTES)) {
            process.destroyForcibly()
            throw AssertionError(".NET Exit build timed out")
        }
        if (process.exitValue() != 0) {
            val detail = String(Files.readAllBytes(log), Charsets.UTF_8).takeLast(4_000)
            throw AssertionError(".NET Exit build failed (exit ${process.exitValue()}):\n$detail")
        }
    }

    private fun generatedStaticPrivateKey(proxyDll: Path, workingDirectory: Path): String {
        val output = runCli(proxyDll, workingDirectory, emptyMap(), "genkey")
        return output.lineSequence()
            .firstOrNull { it.startsWith("BFT_STATIC_PRIVATE_KEY=") }
            ?.substringAfter('=')
            ?: throw AssertionError("Exit genkey did not return a static private key")
    }

    private fun addClient(
        proxyDll: Path,
        workingDirectory: Path,
        environment: Map<String, String>,
        label: String,
        port: Int
    ): String {
        val output = runCli(
            proxyDll,
            workingDirectory,
            environment,
            "add-client", "--label", label, "--name", "Interop test", "--host", "127.0.0.1", "--port", port.toString()
        ).trim()
        if (!output.startsWith("bft://")) throw AssertionError("Exit add-client did not return a BFT connection")
        return output
    }

    private fun runCli(proxyDll: Path, workingDirectory: Path, processEnvironment: Map<String, String>, vararg args: String): String {
        val builder = ProcessBuilder(listOf("dotnet", proxyDll.toString()) + args)
        builder.directory(workingDirectory.toFile())
        builder.redirectErrorStream(true)
        builder.environment().putAll(processEnvironment)
        val process = builder.start()
        val output = process.inputStream.bufferedReader().use { it.readText() }
        if (!process.waitFor(10, TimeUnit.SECONDS)) {
            process.destroyForcibly()
            throw AssertionError("Exit CLI command timed out")
        }
        if (process.exitValue() != 0) throw AssertionError("Exit CLI command failed (exit ${process.exitValue()})")
        return output
    }

    private fun startProxy(proxyDll: Path, workingDirectory: Path, processEnvironment: Map<String, String>): Process =
        ProcessBuilder("dotnet", proxyDll.toString()).let { builder ->
            builder.directory(workingDirectory.toFile())
            builder.redirectErrorStream(true)
            builder.redirectOutput(workingDirectory.resolve("proxy.log").toFile())
            builder.environment().putAll(processEnvironment)
            builder.start()
        }

    private fun awaitListening(process: Process, port: Int) {
        val deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(15)
        while (System.nanoTime() < deadline) {
            if (!process.isAlive) throw AssertionError("Exit process stopped before opening its listener")
            try {
                Socket().use { it.connect(InetSocketAddress(InetAddress.getByName("127.0.0.1"), port), 250) }
                return
            } catch (_: IOException) {
                Thread.sleep(25)
            }
        }
        throw AssertionError("Exit process did not open its listener")
    }

    private fun freePort(): Int = ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")).use { it.localPort }

    private fun stopOwnedProcess(process: Process) {
        if (!process.isAlive) return
        process.destroy()
        if (!process.waitFor(5, TimeUnit.SECONDS)) {
            process.destroyForcibly()
            process.waitFor(5, TimeUnit.SECONDS)
        }
    }

    private fun deleteTree(root: Path) {
        if (!Files.exists(root)) return
        Files.walk(root).use { paths ->
            paths.sorted(Comparator.reverseOrder()).forEach { Files.deleteIfExists(it) }
        }
    }

    private class CountingInputStream(input: InputStream) : FilterInputStream(input) {
        var bytesRead = 0
            private set

        override fun read(): Int = super.read().also { if (it >= 0) bytesRead++ }

        override fun read(buffer: ByteArray, offset: Int, length: Int): Int =
            super.read(buffer, offset, length).also { if (it > 0) bytesRead += it }
    }
}
