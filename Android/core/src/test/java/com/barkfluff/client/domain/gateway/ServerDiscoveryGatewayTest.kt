package com.barkfluff.client.domain.gateway

import com.barkfluff.client.security.TlsEndpoint
import org.junit.Assert.assertEquals
import org.junit.Test

class ServerDiscoveryGatewayTest {

    @Test
    fun `default navigator endpoint includes an explicit grpc port`() {
        assertEquals(
            443,
            TlsEndpoint.parseAddress(ServerDiscoveryGateway.DEFAULT_NAVIGATOR_ENDPOINT).port,
        )
    }
}
