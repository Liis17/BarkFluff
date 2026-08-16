package com.barkfluff.client.utils

import android.content.Context
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.security.TlsCertificateProbe
import com.barkfluff.client.security.TlsTrustStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

fun GlobalParam.applyServerInfo(serverInfo: GrpcManager.ServerInfo) {
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
    grpcManager: GrpcManager,
    globalParam: GlobalParam,
    context: Context
): ServerInfoRefreshResult {
    if (globalParam.socketBeacon.isBlank()) {
        return ServerInfoRefreshResult.Unavailable
    }

    val createResult = grpcManager.createOnlyBeaconClient(globalParam.socketBeacon)
    if (createResult.isFailure) {
        return ServerInfoRefreshResult.Unavailable
    }

    val infoResult = grpcManager.getServerInfo()
    val serverInfo = infoResult.getOrNull() ?: return ServerInfoRefreshResult.Unavailable
    val approvalRequired = try {
        withContext(Dispatchers.IO) {
            TlsServerCertificatePreflight(
                TlsTrustStore(context.applicationContext),
                TlsCertificateProbe()
            ).approvalRequired(serverInfo)
        }
    } catch (_: TlsEndpointSecurityException) {
        return ServerInfoRefreshResult.CertificateRejected
    }
    if (approvalRequired != null) {
        return ServerInfoRefreshResult.CertificateApprovalRequired
    }
    globalParam.applyServerInfo(serverInfo)
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
