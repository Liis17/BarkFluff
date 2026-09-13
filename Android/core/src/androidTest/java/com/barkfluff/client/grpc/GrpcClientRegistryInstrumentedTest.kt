package com.barkfluff.client.grpc

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.security.TlsTransportFactory
import io.grpc.CallOptions
import io.grpc.ClientCall
import io.grpc.ManagedChannel
import io.grpc.Metadata
import io.grpc.MethodDescriptor
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotSame
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayInputStream
import java.util.Base64
import java.util.concurrent.TimeUnit

@RunWith(AndroidJUnit4::class)
class GrpcClientRegistryInstrumentedTest {

    @Test
    fun `registration replaces cached anonymous identity channel and sends device metadata`() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val createdChannels = mutableListOf<RecordingManagedChannel>()
        var registrationChannel: RecordingManagedChannel? = null
        val globalParam = GlobalParam(context)
        val endpoint = "https://identity.example:443"
        val previousIdentityEndpoint = globalParam.socketIdentity
        val registry = GrpcClientRegistry(
            context = context,
            tlsTransport = TlsTransportFactory(context),
            channelFactory = {
                RecordingManagedChannel().also(createdChannels::add)
            },
        )

        try {
            globalParam.socketIdentity = endpoint
            assertTrue(registry.createIdentityClient(endpoint).isSuccess)
            val anonymousChannel = createdChannels.single()
            assertTrue(registry.identityClient != null)
            assertEquals(1, createdChannels.size)

            assertTrue(
                registry.createIdentityClient(
                    endpoint,
                    context,
                    includeDeviceInfo = true,
                ).isSuccess,
            )
            val newRegistrationChannel = createdChannels.last()
            registrationChannel = newRegistrationChannel

            assertNotSame(anonymousChannel, newRegistrationChannel)
            assertTrue(anonymousChannel.shutdownRequested)
            assertTrue(anonymousChannel.shutdownNowRequested)

            val call = registry.identityChannel!!.newCall(unitMethod, CallOptions.DEFAULT)
            call.start(object : ClientCall.Listener<Unit>() {}, Metadata())

            val deviceNameKey = Metadata.Key.of("x-device-name", Metadata.ASCII_STRING_MARSHALLER)
            val expectedDeviceName = Base64.getEncoder().encodeToString(
                GlobalParam.getDeviceName().toByteArray(Charsets.UTF_8),
            )
            assertEquals(expectedDeviceName, newRegistrationChannel.startedHeaders?.get(deviceNameKey))
        } finally {
            globalParam.socketIdentity = previousIdentityEndpoint
            registry.shutdown()
        }

        assertTrue(registrationChannel?.shutdownRequested == true)
        assertTrue(registrationChannel?.shutdownNowRequested == true)
    }

    private class RecordingManagedChannel : ManagedChannel() {
        var startedHeaders: Metadata? = null
            private set
        var shutdownRequested = false
            private set
        var shutdownNowRequested = false
            private set

        override fun <RequestT, ResponseT> newCall(
            methodDescriptor: MethodDescriptor<RequestT, ResponseT>,
            callOptions: CallOptions,
        ): ClientCall<RequestT, ResponseT> = object : ClientCall<RequestT, ResponseT>() {
            override fun start(responseListener: Listener<ResponseT>, headers: Metadata) {
                this@RecordingManagedChannel.startedHeaders = headers
            }

            override fun request(numMessages: Int) = Unit

            override fun cancel(message: String?, cause: Throwable?) = Unit

            override fun halfClose() = Unit

            override fun sendMessage(message: RequestT) = Unit
        }

        override fun authority(): String = "test"

        override fun shutdown(): ManagedChannel {
            shutdownRequested = true
            return this
        }

        override fun isShutdown(): Boolean = shutdownRequested

        override fun isTerminated(): Boolean = shutdownNowRequested

        override fun shutdownNow(): ManagedChannel {
            shutdownRequested = true
            shutdownNowRequested = true
            return this
        }

        override fun awaitTermination(timeout: Long, unit: TimeUnit): Boolean = shutdownNowRequested
    }

    private object UnitMarshaller : MethodDescriptor.Marshaller<Unit> {
        override fun stream(value: Unit) = ByteArrayInputStream(ByteArray(0))

        override fun parse(stream: java.io.InputStream): Unit = Unit
    }

    private companion object {
        val unitMethod = MethodDescriptor.newBuilder<Unit, Unit>()
            .setType(MethodDescriptor.MethodType.UNARY)
            .setFullMethodName("test.Identity/CreateAccount")
            .setRequestMarshaller(UnitMarshaller)
            .setResponseMarshaller(UnitMarshaller)
            .build()
    }
}
