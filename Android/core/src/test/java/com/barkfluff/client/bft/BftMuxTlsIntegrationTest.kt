package com.barkfluff.client.bft

import org.conscrypt.Conscrypt
import org.junit.BeforeClass
import org.junit.Test
import java.io.Closeable
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference
import java.security.Security

class BftMuxTlsIntegrationTest {
    private val identity by lazy {
        val keystore = checkNotNull(javaClass.getResourceAsStream("/bft-test-identity.p12"))
        BftTransportTestSupport.loadIdentity(keystore)
    }

    @Test
    fun grpcUsesPinnedTlsAndHttp2OverTheRealMuxStream() {
        val route = AtomicReference<Closeable?>()
        BftTransportTestSupport.verifyGrpcOverBftMux(
            identity,
            routeFactory = { port, opened ->
                val peer = BftMuxTestPeer(upstreamPort = port)
                val tunnel = peer.connect()
                route.set(Closeable { tunnel.close(); peer.close() })
                BftSocketFactory {
                    opened.incrementAndGet()
                    tunnel.openStream(4).asSocketStreams()
                }
            },
            closeRoute = { route.getAndSet(null)?.close() }
        )
    }

    @Test
    fun okHttpHttp11UploadsAndDownloadsThroughTheRealMuxStream() {
        val route = AtomicReference<Closeable?>()
        BftTransportTestSupport.verifyOkHttpHttp11OverBftMux(
            identity,
            routeFactory = { port, opened ->
                val peer = BftMuxTestPeer(upstreamPort = port)
                val tunnel = peer.connect()
                route.set(Closeable { tunnel.close(); peer.close() })
                BftSocketFactory {
                    opened.incrementAndGet()
                    tunnel.openStream(4).asSocketStreams()
                }
            },
            closeRoute = { route.getAndSet(null)?.close() }
        )
    }

    companion object {
        @BeforeClass
        @JvmStatic
        fun installConscrypt() {
            val provider = Conscrypt.newProvider()
            if (Security.getProvider(provider.name) == null) Security.insertProviderAt(provider, 1)
        }
    }
}
