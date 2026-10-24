package com.barkfluff.client.utils

import com.barkfluff.client.domain.model.ServerInfo
import com.barkfluff.client.security.TlsCertificateInfo
import com.barkfluff.client.security.TlsCertificateProbe
import com.barkfluff.client.security.TlsPin
import com.barkfluff.client.security.TlsTrustStore
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import java.security.cert.CertificateException

class TlsEndpointSecurityException(address: String, cause: Throwable) :
    IllegalStateException("TLS certificate validation failed for $address", cause)

/**
 * Finds the first endpoint whose certificate needs an explicit user decision. Unreachable
 * endpoints are deliberately ignored: an unavailable service must not create a speculative pin.
 */
class TlsServerCertificatePreflight internal constructor(
    private val pinFor: (String) -> TlsPin?,
    private val inspectUrl: (String) -> TlsCertificateInfo,
) {
    constructor(trustStore: TlsTrustStore, certificateProbe: TlsCertificateProbe) :
        this(trustStore::pinFor, certificateProbe::inspectUrl)

    /**
     * Each endpoint is a separate TLS handshake, so they are probed concurrently (up to ten in a row
     * made Splash wait ~2 s); results are then evaluated in endpoint order, as a sequential check would.
     */
    suspend fun approvalRequired(serverInfo: ServerInfo): TlsCertificateInfo? {
        val addresses = serverInfo.endpointAddresses()
        val probes = coroutineScope {
            addresses.map { address -> async(Dispatchers.IO) { runCatching { inspectUrl(address) } } }.awaitAll()
        }
        val inspectedHosts = mutableSetOf<String>()
        for ((address, probe) in addresses.zip(probes)) {
            val certificate = probe.getOrElse { error ->
                if (error is CancellationException) throw error
                if (error is CertificateException || error is IllegalArgumentException) {
                    throw TlsEndpointSecurityException(address, error)
                }
                null
            } ?: continue
            if (!inspectedHosts.add(certificate.host)) continue

            val existingPin = pinFor(certificate.host)
            if (existingPin?.spkiSha256 == certificate.spkiSha256) continue
            if (existingPin == null && !certificate.isSelfSigned) continue
            return certificate
        }
        return null
    }

    private fun ServerInfo.endpointAddresses(): List<String> = listOf(
        identityEndpoint,
        usersEndpoint,
        filesEndpoint,
        // Отдельный файловый адрес — самостоятельный TLS-хост, его серт тоже надо
        // предъявить пользователю до первой загрузки/скачивания.
        filesMediaEndpoint,
        messagesEndpoint,
        updatesEndpoint,
        onlinerEndpoint,
        fastAuthEndpoint,
        callsEndpoint,
        livekitUrl
    ).filter { it.isNotBlank() }
}
