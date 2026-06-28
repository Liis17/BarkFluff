package com.barkfluff.client.utils

import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager

fun GlobalParam.applyServerInfo(serverInfo: GrpcManager.ServerInfo) {
    serverName = serverInfo.name
    serverDescription = serverInfo.description
    socketIdentity = ensureHttpPrefix(serverInfo.identityEndpoint)
    socketUsers = ensureHttpPrefix(serverInfo.usersEndpoint)
    socketFiles = ensureHttpPrefix(serverInfo.filesEndpoint)
    socketMessages = ensureHttpPrefix(serverInfo.messagesEndpoint)
    socketUpdates = ensureHttpPrefix(serverInfo.updatesEndpoint)
    socketOnliner = ensureHttpPrefix(serverInfo.onlinerEndpoint)
    socketFastAuth = ensureHttpPrefix(serverInfo.fastAuthEndpoint)
    socketCalls = ensureHttpPrefix(serverInfo.callsEndpoint)
    livekitUrl = serverInfo.livekitUrl
    colors = serverInfo.color
}

suspend fun refreshServerInfoFromBeacon(grpcManager: GrpcManager, globalParam: GlobalParam): Boolean {
    if (globalParam.socketBeacon.isBlank()) {
        return false
    }

    val createResult = grpcManager.createOnlyBeaconClient(globalParam.socketBeacon)
    if (createResult.isFailure) {
        return false
    }

    val infoResult = grpcManager.getServerInfo()
    val serverInfo = infoResult.getOrNull() ?: return false
    globalParam.applyServerInfo(serverInfo)
    return true
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