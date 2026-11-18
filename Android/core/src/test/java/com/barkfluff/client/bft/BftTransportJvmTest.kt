package com.barkfluff.client.bft

import org.conscrypt.Conscrypt
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.BeforeClass
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.JUnit4
import java.security.Security

@RunWith(JUnit4::class)
class BftTransportJvmTest {
    private val identity by lazy {
        val keystore = checkNotNull(javaClass.getResourceAsStream("/bft-test-identity.p12"))
        BftTransportTestSupport.loadIdentity(keystore)
    }

    @Before
    fun verifyJvmFallbackEnvironment() {
        assertTrue(
            "JVM fallback must use Conscrypt, got ${BftTransportTestSupport.tlsProviderName()}",
            BftTransportTestSupport.tlsProviderName().contains("Conscrypt", ignoreCase = true)
        )
        BftTransportTestSupport.assertOriginUnresolvable()
    }

    @Test
    fun grpcUnaryServerStreamingAndBidiUsePinnedTlsAndHttp2() =
        BftTransportTestSupport.verifyGrpc(identity)

    @Test
    fun okHttpHttp11UploadsAndDownloadsMultiMegabyteBodiesOverPinnedTls() =
        BftTransportTestSupport.verifyOkHttpHttp11(identity)

    @Test
    fun wrongExitPinRejectsTlsConnection() =
        BftTransportTestSupport.verifyWrongPinRejected(identity)

    @Test
    fun httpsUrlConnectionCannotRouteThroughVirtualSocketFactory() =
        BftTransportTestSupport.verifyUrlConnectionCannotRoute(identity)

    @Test
    fun layeredTlsFactoryCarriesHttp11OverAVirtualSocketWhenAddressIsLiteral() =
        BftTransportTestSupport.verifyLayeredTlsFactoryUsesVirtualSocket(identity)

    companion object {
        @BeforeClass
        @JvmStatic
        fun installConscrypt() {
            val provider = Conscrypt.newProvider()
            if (Security.getProvider(provider.name) == null) {
                Security.insertProviderAt(provider, 1)
            }
        }
    }
}
