package com.barkfluff.client.bft

import barkfluff.beacon.BeaconApiGrpc
import barkfluff.beacon.BeaconApiOuterClass
import io.grpc.Metadata
import io.grpc.ServerCall
import io.grpc.ServerCallHandler
import io.grpc.ServerInterceptor
import io.grpc.ServerInterceptors
import io.grpc.ServerServiceDefinition
import io.grpc.okhttp.OkHttpServerBuilder
import io.grpc.stub.ServerCalls
import io.grpc.stub.StreamObserver
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.security.Security
import java.util.concurrent.atomic.AtomicReference
import org.conscrypt.Conscrypt

class BftBeaconTunnelProbeTest {
    @Test
    fun `beacon identity probe traverses TLS tunnel without auth token`() {
        val provider = Conscrypt.newProvider()
        if (Security.getProvider(provider.name) == null) Security.insertProviderAt(provider, 1)
        val identity = checkNotNull(javaClass.getResourceAsStream("/bft-test-identity.p12"))
            .let(BftTransportTestSupport::loadIdentity)
        val receivedToken = AtomicReference<String?>()
        val authKey = Metadata.Key.of("x-auth-token", Metadata.ASCII_STRING_MARSHALLER)
        val response = beaconResponse()
        val service = ServerServiceDefinition.builder("barkfluff.beacon.BeaconApi")
            .addMethod(
                BeaconApiGrpc.getGetServerInfoMethod(),
                ServerCalls.asyncUnaryCall { _: BeaconApiOuterClass.GetServerInfoRequest,
                    observer: StreamObserver<BeaconApiOuterClass.GetServerInfoResponse> ->
                    observer.onNext(response)
                    observer.onCompleted()
                }
            )
            .build()
        val port = ServerSocket(0, 1, InetAddress.getLoopbackAddress()).use { it.localPort }
        val server = OkHttpServerBuilder.forPort(
            InetSocketAddress(InetAddress.getLoopbackAddress(), port),
            identity.grpcServerCredentials()
        ).addService(ServerInterceptors.intercept(service, object : ServerInterceptor {
            override fun <ReqT : Any?, RespT : Any?> interceptCall(
                call: ServerCall<ReqT, RespT>,
                headers: Metadata,
                next: ServerCallHandler<ReqT, RespT>
            ): ServerCall.Listener<ReqT> {
                receivedToken.set(headers.get(authKey))
                return next.startCall(call, headers)
            }
        })).build().start()
        val peer = BftMuxTestPeer(upstreamPort = port)
        var tunnel: BftTunnel? = null
        try {
            tunnel = peer.connect()
            val endpoints = BftBeaconTunnelProbe.getEndpoints(
                tunnel,
                BftTransportTestSupport.ORIGIN,
                identity.pin
            )
            assertEquals(
                mapOf(
                    2 to "identity.node.test:443",
                    3 to "users.node.test:443",
                    4 to "messages.node.test:443",
                    5 to "files.node.test:443",
                    6 to "media.node.test:9443",
                    7 to "updates.node.test:443",
                    8 to "onliner.node.test:443",
                    9 to "auth.node.test:443",
                    10 to "calls.node.test:443"
                ),
                endpoints
            )
            assertFalse("Beacon identity lookup must not send an account token", receivedToken.get() != null)
        } finally {
            tunnel?.close()
            peer.close()
            server.shutdownNow().awaitTermination(5, java.util.concurrent.TimeUnit.SECONDS)
        }
    }

    private fun beaconResponse() = BeaconApiOuterClass.GetServerInfoResponse.newBuilder()
        .setIdentity(service("identity.node.test"))
        .setUsers(service("users.node.test"))
        .setMessages(service("messages.node.test"))
        .setFiles(service("files.node.test"))
        .setUpdates(service("updates.node.test"))
        .setOnliner(service("onliner.node.test"))
        .setFastAuth(service("auth.node.test"))
        .setCalls(service("calls.node.test"))
        .setFilesMediaEndpoint("media.node.test:9443")
        .build()

    private fun service(host: String) = BeaconApiOuterClass.Service.newBuilder()
        .setEndpoint(BeaconApiOuterClass.ServiceEndpoint.newBuilder().setHost(host).setPort(443))
        .build()
}
