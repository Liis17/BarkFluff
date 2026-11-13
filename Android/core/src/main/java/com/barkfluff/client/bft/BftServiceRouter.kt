package com.barkfluff.client.bft

import okhttp3.HttpUrl
import java.net.URI

data class BftServiceEndpoint(val serviceId: Int, val address: String)

/** Resolves an existing endpoint to the logical service carried in a BFT OPEN frame. */
object BftServiceRouter {
    fun serviceIdForEndpoint(address: String, endpoints: Iterable<BftServiceEndpoint>): Int? {
        val key = endpointKey(address) ?: return null
        return endpoints.firstOrNull { endpointKey(it.address) == key }?.serviceId
    }

    /** Returns null for non-BarkFluff hosts, and throws for an unmapped BarkFluff endpoint. */
    fun serviceIdForHttpUrl(url: HttpUrl, endpoints: Iterable<BftServiceEndpoint>): Int? {
        val key = "${url.host.lowercase()}:${url.port}"
        val mapped = endpoints.firstOrNull { endpointKey(it.address) == key }?.serviceId
        if (mapped != null) return mapped
        if (url.host.equals("storage.barkfluff.com", ignoreCase = true)) return 12
        if (url.host.equals("barkfluff.com", ignoreCase = true) ||
            url.host.endsWith(".barkfluff.com", ignoreCase = true)
        ) {
            throw IllegalArgumentException("BarkFluff endpoint is unavailable through the proxy")
        }
        return null
    }

    private fun endpointKey(address: String): String? = runCatching {
        val candidate = address.trim().let {
            if (it.startsWith("http://", true) || it.startsWith("https://", true)) it else "https://$it"
        }
        val uri = URI(candidate)
        val host = uri.host ?: return null
        val port = when {
            uri.port >= 0 -> uri.port
            uri.scheme.equals("http", ignoreCase = true) -> 80
            else -> 443
        }
        "${host.lowercase()}:$port"
    }.getOrNull()
}
