package com.barkfluff.client.security

import io.grpc.okhttp.OkHttpChannelBuilder
import java.security.cert.X509Certificate
import javax.net.ssl.X509TrustManager

/** Debug-only local-development transport. This source set is never packaged into release. */
internal object TlsVariantPolicy {
    const val allowCleartext = true

    fun socketConfig(host: String, trustStore: TlsPinLookup): TlsSocketConfig = socketConfigFor(TRUST_ALL)

    fun configureGrpcBuilder(
        builder: OkHttpChannelBuilder,
        endpoint: TlsEndpoint,
        trustStore: TlsPinLookup
    ): OkHttpChannelBuilder = if (endpoint.usesTls) {
        builder.sslSocketFactory(socketConfig(endpoint.host, trustStore).socketFactory)
    } else {
        builder.usePlaintext()
    }

    private val TRUST_ALL = object : X509TrustManager {
        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = Unit
        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) = Unit
        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }
}
