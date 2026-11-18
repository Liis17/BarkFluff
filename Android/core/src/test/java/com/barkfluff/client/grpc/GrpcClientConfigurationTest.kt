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
            serviceId = 2,
            transportGeneration = 1,
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
            serviceId = 2,
            transportGeneration = 1,
        )

        assertTrue(canReuseGrpcClient(configuration, configuration, force = false))
        assertFalse(canReuseGrpcClient(configuration, configuration, force = true))
    }

    @Test
    fun `proxy generation invalidates an otherwise identical channel slot`() {
        val current = GrpcClientConfiguration(
            address = "identity.example:443",
            includeAuth = true,
            includeDeviceInfo = true,
            serviceId = 2,
            transportGeneration = 1,
        )

        assertFalse(canReuseGrpcClient(current, current.copy(transportGeneration = 2), force = false))
    }
}
