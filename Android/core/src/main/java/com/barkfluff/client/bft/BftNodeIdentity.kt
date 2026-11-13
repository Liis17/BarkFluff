package com.barkfluff.client.bft

import okhttp3.HttpUrl.Companion.toHttpUrlOrNull

/** Beacon does not expose a stable node ID, so compare every endpoint it does expose. */
internal object BftNodeIdentity {
    fun matches(
        savedEndpoints: Iterable<BftServiceEndpoint>,
        beaconEndpoints: Map<Int, String>
    ): Boolean {
        val saved = savedEndpoints
            .filter { it.serviceId != BEACON_SERVICE_ID }
            .mapNotNull { endpoint -> endpointKey(endpoint.address)?.let { endpoint.serviceId to it } }
            .toMap()
        val returned = mutableMapOf<Int, String>()
        for ((serviceId, address) in beaconEndpoints) {
            returned[serviceId] = endpointKey(address) ?: return false
        }
        return returned.isNotEmpty() && returned.all { (serviceId, address) -> saved[serviceId] == address }
    }

    private fun endpointKey(address: String): String? {
        val candidate = address.trim().let {
            if (it.startsWith("http://", true) || it.startsWith("https://", true)) it else "https://$it"
        }
        val url = candidate.toHttpUrlOrNull() ?: return null
        return "${url.host.lowercase()}:${url.port}"
    }

    private const val BEACON_SERVICE_ID = 1
}

object BftProxySessionPolicy {
    fun hasSession(accessToken: String?, refreshToken: String?, userId: Long = 0L): Boolean =
        !accessToken.isNullOrBlank() || !refreshToken.isNullOrBlank() || userId > 0L

    fun mayApplyServerInfo(proxyEnabled: Boolean, hasSession: Boolean): Boolean =
        !proxyEnabled || !hasSession

    fun beaconAddressForSelection(proxyEnabled: Boolean, selectedAddress: String): String =
        if (proxyEnabled) "" else selectedAddress

    fun needsServerSelectionForDirectBeacon(beaconAddress: String): Boolean = beaconAddress.isBlank()
}

enum class BftProxyErrorCode { NO_CONNECTION, KEY_REJECTED, SERVER_MISMATCH, SERVER_IDENTITY_UNAVAILABLE, SERVICE_UNAVAILABLE }

class BftProxyException(val code: BftProxyErrorCode, cause: Throwable? = null) : java.io.IOException(null, cause)
