package com.barkfluff.client.grpc

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class GrpcClientConfigurationTest {

    @Test
    fun `same endpoint with device metadata is not reused from an anonymous client`() {
        val anonymous = GrpcClientConfiguration(
            address = "identity.example:443",
            includeAuth = false,
            includeDeviceInfo = false,
        )
        val registration = anonymous.copy(
            includeAuth = true,
            includeDeviceInfo = true,
        )

        assertFalse(canReuseGrpcClient(anonymous, registration, force = false))
    }

    @Test
    fun `same client configuration is reused unless recreation is forced`() {
        val configuration = GrpcClientConfiguration(
            address = "identity.example:443",
            includeAuth = true,
            includeDeviceInfo = true,
        )

        assertTrue(canReuseGrpcClient(configuration, configuration, force = false))
        assertFalse(canReuseGrpcClient(configuration, configuration, force = true))
    }
}
