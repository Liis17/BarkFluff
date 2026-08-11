package com.barkfluff.client.security

internal object TlsVariantPolicy {
    const val allowCleartext = false

    fun socketConfig(host: String, trustStore: TlsTrustStore): TlsSocketConfig =
        socketConfigFor(PinnedTrustManager(host, trustStore))
}
