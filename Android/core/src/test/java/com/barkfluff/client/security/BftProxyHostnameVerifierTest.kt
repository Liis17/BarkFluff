package com.barkfluff.client.security

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.lang.reflect.Proxy
import javax.net.ssl.SSLSession

class BftProxyHostnameVerifierTest {
    @Test
    fun `BFT hostname verifier accepts only bft internal peer sessions`() {
        assertTrue(BftProxyHostnameVerifier.verify("files.example.test", session("bft.internal")))
        assertFalse(BftProxyHostnameVerifier.verify("bft.internal", session("files.example.test")))
        assertFalse(BftProxyHostnameVerifier.verify("bft.internal", session("127.0.0.1")))
    }

    private fun session(peerHost: String): SSLSession = Proxy.newProxyInstance(
        SSLSession::class.java.classLoader,
        arrayOf(SSLSession::class.java)
    ) { _, method, _ ->
        if (method.name == "getPeerHost") peerHost else null
    } as SSLSession
}
