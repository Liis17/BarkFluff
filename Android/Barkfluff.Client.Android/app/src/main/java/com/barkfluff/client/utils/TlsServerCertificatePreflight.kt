package com.barkfluff.client.utils

import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.security.TlsCertificateInfo
import com.barkfluff.client.security.TlsCertificateProbe
import com.barkfluff.client.security.TlsTrustStore
import kotlinx.coroutines.CancellationException
import java.security.cert.CertificateException

class TlsEndpointSecurityException(address: String, cause: Throwable) :
    IllegalStateException("TLS certificate validation failed for $address", cause)

/**
 * Finds the first endpoint whose certificate needs an explicit user decision. Unreachable
 * endpoints are deliberately ignored: an unavailable service must not create a speculative pin.
 */
class TlsServerCertificatePreflight(
    private val trustStore: TlsTrustStore,
    private val certificateProbe: TlsCertificateProbe
) {
    fun approvalRequired(serverInfo: GrpcManager.ServerInfo): TlsCertificateInfo? {
        val inspectedHosts = mutableSetOf<String>()
        for (address in serverInfo.endpointAddresses()) {
            val certificate = try {
                certificateProbe.inspectUrl(address)
            } catch (error: Exception) {
                if (error is CancellationException) throw error
                if (error is CertificateException || error is IllegalArgumentException) {
                    throw TlsEndpointSecurityException(address, error)
                }
                continue
            }
            if (!inspectedHosts.add(certificate.host)) continue

            val existingPin = trustStore.pinFor(certificate.host)
            if (existingPin?.spkiSha256 == certificate.spkiSha256) continue
            if (existingPin == null && !certificate.isSelfSigned) continue
            return certificate
        }
        return null
    }

    private fun GrpcManager.ServerInfo.endpointAddresses(): List<String> = listOf(
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
