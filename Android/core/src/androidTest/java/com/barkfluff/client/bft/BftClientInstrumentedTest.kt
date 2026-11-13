package com.barkfluff.client.bft

import android.security.NetworkSecurityPolicy
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.io.Closeable
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference

@RunWith(AndroidJUnit4::class)
class BftClientInstrumentedTest {
    private lateinit var identity: BftTransportTestSupport.TestIdentity

    @Before
    fun setUp() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        BftVectors.load(instrumentation.context.assets.open("bftv1-vectors.json"))
        identity = BftTransportTestSupport.loadIdentity(instrumentation.context.assets.open("bft-test-identity.p12"))
    }

    @Test
    fun appPolicyRejectsCleartextAndOriginCannotResolve() {
        val policy = NetworkSecurityPolicy.getInstance()
        assertFalse("BFT test APK must not allow cleartext traffic", policy.isCleartextTrafficPermitted)
        assertFalse(
            "BFT test origin must not allow cleartext traffic",
            policy.isCleartextTrafficPermitted(BftTransportTestSupport.ORIGIN)
        )
        BftTransportTestSupport.assertOriginUnresolvable()

        val provider = BftTransportTestSupport.tlsProviderName()
        assertTrue("Expected platform Conscrypt TLS provider, got $provider", provider.contains("Conscrypt", ignoreCase = true))
        System.out.println("BFT Android TLS provider: $provider")
    }

    @Test
    fun grpcUnaryServerStreamingAndBidiWorkOverTlsOnVirtualSocket() =
        BftTransportTestSupport.verifyGrpc(identity)

    @Test
    fun okHttpHttp11UploadDownloadAndSha256WorkOverVirtualSocket() =
        BftTransportTestSupport.verifyOkHttpHttp11(identity)

    @Test
    fun incorrectPinFailsOverVirtualSocket() =
        BftTransportTestSupport.verifyWrongPinRejected(identity)

    @Test
    fun httpsUrlConnectionCannotResolveAnUnroutableOrigin() =
        BftTransportTestSupport.verifyUrlConnectionCannotRoute(identity)

    @Test
    fun layeredTlsCanUseAConnectedVirtualSocket() =
        BftTransportTestSupport.verifyUrlConnectionUsesVirtualSocket(identity)

    @Test
    fun grpcUnaryServerStreamingAndBidiWorkOverBftTunnel() {
        val route = AtomicReference<Closeable?>()
        BftTransportTestSupport.verifyGrpcOverBftMux(
            identity,
            routeFactory = createMuxRoute(route),
            closeRoute = { route.getAndSet(null)?.close() }
        )
    }

    @Test
    fun okHttpUploadDownloadAndSha256WorkOverBftTunnel() {
        val route = AtomicReference<Closeable?>()
        BftTransportTestSupport.verifyOkHttpHttp11OverBftMux(
            identity,
            routeFactory = createMuxRoute(route),
            closeRoute = { route.getAndSet(null)?.close() }
        )
    }

    @Test
    fun incorrectPinFailsOverBftTunnel() {
        val route = AtomicReference<Closeable?>()
        BftTransportTestSupport.verifyWrongPinRejectedOverBftMux(
            identity,
            routeFactory = createMuxRoute(route),
            closeRoute = { route.getAndSet(null)?.close() }
        )
    }

    @Test
    fun deviceInteroperatesWithExitFromInstrumentationArgument() {
        val link = InstrumentationRegistry.getArguments().getString("bftConnection")
        assumeTrue("Run with BFT_TEST_CONNECTION from Android/tools/run_bft_instrumented_tests.ps1", !link.isNullOrBlank())

        val settings = BftConnectionString.parse(checkNotNull(link))
        try {
            assertEquals("127.0.0.1", settings.host)
            val socket = Socket()
            socket.connect(InetSocketAddress(settings.host, settings.port), 15_000)
            BftTunnel.connect(
                socket,
                link,
                BftTunnelOptions(handshakeTimeoutMillis = 15_000, helloTimeoutMillis = 15_000)
            ).use { tunnel ->
                assertEquals("android-device-test", tunnel.hello.proxyId)
                assertEquals(listOf(1, 2), tunnel.hello.serviceIds)
                assertEquals(32, tunnel.hello.tlsSpkiSha256.size)
                BftTransportTestSupport.verifyExitGrpcUnavailable(tunnel, 1)
                BftTransportTestSupport.verifyExitHttp11ReturnsBadGateway(tunnel, 2)
                BftTransportTestSupport.verifyWrongPinRejectedOverBftTunnel(tunnel, 1)
                assertEquals(BftTunnel.State.READY, tunnel.state)
            }
        } finally {
            settings.staticPublicKey.fill(0)
            settings.obfsKey.fill(0)
        }
    }

    private fun createMuxRoute(route: AtomicReference<Closeable?>): (Int, AtomicInteger) -> BftSocketFactory =
        { port, opened ->
            val peer = BftMuxTestPeer(upstreamPort = port)
            val tunnel = peer.connect()
            route.set(Closeable { tunnel.close(); peer.close() })
            BftSocketFactory {
                opened.incrementAndGet()
                tunnel.openStream(4).asSocketStreams()
            }
        }
}
