package com.barkfluff.client.bft

import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrl
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
        val savedEndpoints = endpoints.toList()
        val key = "${url.host.lowercase()}:${url.port}"
        val mapped = savedEndpoints.firstOrNull { endpointKey(it.address) == key }?.serviceId
        if (mapped != null) return mapped
        if (url.host.equals(STORAGE_HOST, ignoreCase = true)) return STORAGE_SERVICE_ID
        if (isOwnedHost(url.host, savedEndpoints)) {
            throw IllegalArgumentException("BarkFluff endpoint is unavailable through the proxy")
        }
        return null
    }

    private fun isOwnedHost(host: String, endpoints: List<BftServiceEndpoint>): Boolean {
        val normalizedHost = host.trimEnd('.').lowercase()
        if (normalizedHost == BARKFLUFF_DOMAIN || normalizedHost.endsWith(".$BARKFLUFF_DOMAIN")) return true
        return nodeBaseDomains(endpoints).any { base -> normalizedHost == base || normalizedHost.endsWith(".$base") }
    }

    private fun nodeBaseDomains(endpoints: List<BftServiceEndpoint>): Set<String> {
        val hosts = endpoints.mapNotNull { endpoint -> endpointHost(endpoint.address) }
            .map { it.trim('[', ']').trimEnd('.').lowercase() }
            .distinct()
        val dnsHosts = hosts.filterNot(::isIpLiteral)
        val ipHosts = hosts.filter(::isIpLiteral).toSet()
        if (dnsHosts.size == 1) {
            val labels = dnsHosts.single().split('.')
            return (if (labels.size >= 2) setOf(labels.takeLast(2).joinToString(".")) else setOf(dnsHosts.single())) + ipHosts
        }
        if (dnsHosts.size > 1) {
            val reversed = dnsHosts.map { it.split('.').asReversed() }
            val shared = (0 until reversed.minOf { it.size })
                .takeWhile { index -> reversed.all { it[index] == reversed.first()[index] } }
                .map { index -> reversed.first()[index] }
            if (shared.size >= 2) return setOf(shared.asReversed().joinToString(".")) + ipHosts
        }
        return hosts.toSet()
    }

    private fun endpointHost(address: String): String? = runCatching {
        val candidate = address.trim().let {
            if (it.startsWith("http://", true) || it.startsWith("https://", true)) it else "https://$it"
        }
        candidate.toHttpUrl().host
    }.getOrNull()

    private fun isIpLiteral(host: String): Boolean =
        ':' in host || host.matches(Regex("\\d{1,3}(?:\\.\\d{1,3}){3}"))

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

    private const val BARKFLUFF_DOMAIN = "barkfluff.com"
    private const val STORAGE_HOST = "storage.barkfluff.com"
    private const val STORAGE_SERVICE_ID = 12
}
