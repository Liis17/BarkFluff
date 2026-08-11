package com.barkfluff.client.security

import java.net.IDN
import java.net.URI
import java.util.Locale

/** Normalized server endpoint. Cleartext is accepted only when explicitly requested by debug code. */
data class TlsEndpoint(
    val host: String,
    val port: Int,
    val scheme: String = HTTPS
) {
    val usesTls: Boolean
        get() = scheme == HTTPS

    companion object {
        const val HTTPS = "https"
        const val HTTP = "http"
        const val WSS = "wss"
        const val WS = "ws"
        const val DEFAULT_TLS_PORT = 443

        fun parseAddress(address: String, allowCleartext: Boolean = false): TlsEndpoint {
            val uri = parseUri(address)
            val scheme = uri.scheme?.lowercase(Locale.ROOT)
                ?: throw IllegalArgumentException("Endpoint must use HTTPS")
            require(scheme == HTTPS || (allowCleartext && scheme == HTTP)) {
                "Endpoint must use HTTPS"
            }
            require(uri.rawUserInfo == null) { "Endpoint credentials are not allowed" }
            require(uri.rawQuery == null && uri.rawFragment == null) { "Endpoint query is not allowed" }
            require(uri.rawPath.isNullOrEmpty() || uri.rawPath == "/") { "Endpoint path is not allowed" }

            val host = canonicalHost(uri.host ?: throw IllegalArgumentException("Endpoint host is required"))
            require(uri.port in 1..65535) { "Endpoint port is required" }
            return TlsEndpoint(host, uri.port, scheme)
        }

        fun requireUrl(value: String, allowCleartext: Boolean = false): URI {
            val uri = try {
                URI(value.trim())
            } catch (error: Exception) {
                throw IllegalArgumentException("Invalid endpoint URL", error)
            }
            val scheme = uri.scheme?.lowercase(Locale.ROOT)
                ?: throw IllegalArgumentException("URL must use HTTPS")
            require(
                scheme == HTTPS || scheme == WSS ||
                    (allowCleartext && (scheme == HTTP || scheme == WS))
            ) {
                "URL must use HTTPS"
            }
            require(uri.rawUserInfo == null) { "URL credentials are not allowed" }
            require(uri.host != null) { "URL host is required" }
            return uri
        }

        fun parseUrlEndpoint(value: String): TlsEndpoint {
            val uri = requireUrl(value)
            val host = canonicalHost(uri.host ?: throw IllegalArgumentException("URL host is required"))
            val port = if (uri.port == -1) DEFAULT_TLS_PORT else uri.port
            require(port in 1..65535) { "URL port is invalid" }
            return TlsEndpoint(host, port)
        }

        fun canonicalHost(host: String): String {
            val normalized = host.trim().trimEnd('.').lowercase(Locale.ROOT)
            require(normalized.isNotBlank()) { "Endpoint host is required" }
            return if (normalized.contains(':')) normalized else IDN.toASCII(normalized)
        }

        private fun parseUri(address: String): URI {
            val value = address.trim()
            require(value.isNotBlank()) { "Endpoint is required" }
            val withScheme = if (value.contains("://")) value else "$HTTPS://$value"
            return try {
                URI(withScheme)
            } catch (error: Exception) {
                throw IllegalArgumentException("Invalid endpoint", error)
            }
        }
    }
}
