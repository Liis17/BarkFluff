package com.barkfluff.client.security

import io.grpc.okhttp.OkHttpChannelBuilder

internal object TlsVariantPolicy {
    const val allowCleartext = false

    fun socketConfig(host: String, trustStore: TlsPinLookup): TlsSocketConfig =
        socketConfigFor(PinnedTrustManager(host, trustStore))

    fun configureGrpcBuilder(
        builder: OkHttpChannelBuilder,
        endpoint: TlsEndpoint,
        trustStore: TlsPinLookup
    ): OkHttpChannelBuilder {
        require(endpoint.usesTls) { "Release gRPC transport requires TLS" }
        val config = socketConfig(endpoint.host, trustStore)
        return builder.sslSocketFactory(config.socketFactory)
    }
}
