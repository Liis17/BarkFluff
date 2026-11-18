package com.barkfluff.client.utils

import android.content.Context
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.bft.BftProxyManager
import com.barkfluff.client.bft.BftProxySessionPolicy
import com.barkfluff.client.domain.gateway.ServerDiscoveryGateway
import com.barkfluff.client.domain.model.ServerInfo
import com.barkfluff.client.security.TlsCertificateProbe
import com.barkfluff.client.security.TlsTrustStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

fun GlobalParam.applyServerInfo(serverInfo: ServerInfo) {
    serverName = serverInfo.name
    serverDescription = serverInfo.description
    socketIdentity = ensureHttpPrefix(serverInfo.identityEndpoint)
    socketUsers = ensureHttpPrefix(serverInfo.usersEndpoint)
    socketFiles = ensureHttpPrefix(serverInfo.filesEndpoint)
    // Отдельный файловый адрес ноды: пустой — качаем и грузим по ссылкам Files как есть.
    socketFilesMedia = if (serverInfo.filesMediaEndpoint.isBlank()) {
        ""
    } else {
        ensureHttpPrefix(serverInfo.filesMediaEndpoint)
    }
    socketMessages = ensureHttpPrefix(serverInfo.messagesEndpoint)
    socketUpdates = ensureHttpPrefix(serverInfo.updatesEndpoint)
    socketOnliner = ensureHttpPrefix(serverInfo.onlinerEndpoint)
    socketFastAuth = ensureHttpPrefix(serverInfo.fastAuthEndpoint)
    socketCalls = ensureHttpPrefix(serverInfo.callsEndpoint)
    livekitUrl = serverInfo.livekitUrl
    colors = serverInfo.color
}

sealed interface ServerInfoRefreshResult {
    data object Refreshed : ServerInfoRefreshResult
    data object Unavailable : ServerInfoRefreshResult
    data object CertificateApprovalRequired : ServerInfoRefreshResult
    data object CertificateRejected : ServerInfoRefreshResult
}

suspend fun refreshServerInfoFromBeacon(
    serverDiscoveryGateway: ServerDiscoveryGateway,
    globalParam: GlobalParam,
    context: Context
): ServerInfoRefreshResult {
    val proxyEnabled = BftProxyManager.get(context.applicationContext).isEnabled
    if (globalParam.socketBeacon.isBlank() && !proxyEnabled) {
        return ServerInfoRefreshResult.Unavailable
    }

    val createResult = serverDiscoveryGateway.createBeacon(globalParam.socketBeacon)
    if (createResult.isFailure) {
        return ServerInfoRefreshResult.Unavailable
    }

    val infoResult = serverDiscoveryGateway.serverInfo()
    val serverInfo = infoResult.getOrNull() ?: return ServerInfoRefreshResult.Unavailable
    val hasSession = BftProxySessionPolicy.hasSession(
        globalParam.accessToken,
        globalParam.refreshToken,
        globalParam.userId
    )
    if (!BftProxySessionPolicy.mayApplyServerInfo(proxyEnabled, hasSession)) {
        // The proxy manager verifies the tunnel's Beacon response before exposing it to callers.
        // Updating socket* here could silently move an authenticated cache to another node.
        return ServerInfoRefreshResult.Refreshed
    }
    val effectiveServerInfo = if (
        serverInfo.filesMediaEndpoint.isBlank() && globalParam.socketFilesMedia.isNotBlank()
    ) {
        // Старый Beacon может не вернуть поле, уже полученное из Navigator.
        serverInfo.copy(filesMediaEndpoint = globalParam.socketFilesMedia)
    } else {
        serverInfo
    }
    if (!proxyEnabled) {
        val approvalRequired = try {
            withContext(Dispatchers.IO) {
                TlsServerCertificatePreflight(
                    TlsTrustStore(context.applicationContext),
                    TlsCertificateProbe()
                ).approvalRequired(effectiveServerInfo)
            }
        } catch (_: TlsEndpointSecurityException) {
            return ServerInfoRefreshResult.CertificateRejected
        }
        if (approvalRequired != null) {
            return ServerInfoRefreshResult.CertificateApprovalRequired
        }
    }
    globalParam.applyServerInfo(effectiveServerInfo)
    return ServerInfoRefreshResult.Refreshed
}

private fun ensureHttpPrefix(url: String): String {
    if (url.isBlank()) {
        return url
    }

    return if (!url.startsWith("http://") && !url.startsWith("https://")) {
        "https://$url"
    } else {
        url
    }
}
