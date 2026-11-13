package com.barkfluff.client.bft

import android.content.pm.ApplicationInfo
import android.security.NetworkSecurityPolicy
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class BftTransportInstrumentedTest {
    private lateinit var identity: BftTransportTestSupport.TestIdentity

    @Before
    fun setUp() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val targetContext = instrumentation.targetContext
        assertFalse(
            "BFT transport tests must target a non-debuggable app variant",
            targetContext.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE != 0
        )
        assertFalse(NetworkSecurityPolicy.getInstance().isCleartextTrafficPermitted)
        assertFalse(NetworkSecurityPolicy.getInstance().isCleartextTrafficPermitted(BftTransportTestSupport.ORIGIN))
        BftTransportTestSupport.assertOriginUnresolvable()
        assertTrue(BftTransportTestSupport.tlsProviderName().contains("Conscrypt", ignoreCase = true))
        identity = BftTransportTestSupport.loadIdentity(
            instrumentation.context.assets.open("bft-test-identity.p12")
        )
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
    fun httpsUrlConnectionCanLayerPinnedTlsOverAVirtualSocketWhenAddressIsLiteral() =
        BftTransportTestSupport.verifyUrlConnectionUsesVirtualSocket(identity)
}
