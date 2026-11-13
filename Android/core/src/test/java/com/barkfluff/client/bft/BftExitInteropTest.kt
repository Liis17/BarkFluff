package com.barkfluff.client.bft

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.BeforeClass
import org.junit.Test
import org.junit.Assert.assertThrows
import java.io.FilterInputStream
import java.io.IOException
import java.io.InputStream
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.nio.file.Files
import java.nio.file.Path
import java.util.Comparator
import java.util.concurrent.TimeUnit
import com.barkfluff.client.security.TlsPin
import com.barkfluff.client.security.TlsPinLookup
import com.barkfluff.client.security.TlsTransportFactory
import io.grpc.CallOptions
import io.grpc.MethodDescriptor
import io.grpc.Status
import io.grpc.StatusRuntimeException
import io.grpc.stub.ClientCalls
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.Protocol
import okhttp3.Request

class BftExitInteropTest {
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
