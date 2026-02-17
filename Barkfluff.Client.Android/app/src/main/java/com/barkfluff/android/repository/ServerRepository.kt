package com.barkfluff.android.repository

import barkfluff.beacon.BeaconApiGrpcKt
import barkfluff.beacon.GetServerInfoRequest
import barkfluff.navigator.ListServersRequest
import barkfluff.navigator.NavigatorApiGrpcKt
import com.barkfluff.android.data.local.AppDataStore
import com.barkfluff.android.data.model.ServerConfig
import com.barkfluff.android.data.model.ServerInfo
import com.barkfluff.android.data.remote.GrpcChannelFactory
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class ServerRepository @Inject constructor(
    private val dataStore: AppDataStore,
    private val channelFactory: GrpcChannelFactory,
) {
    suspend fun listServers(): Result<List<ServerInfo>> = runCatching {
        val channel = channelFactory.getNavigatorChannel()
        val stub = NavigatorApiGrpcKt.NavigatorApiCoroutineStub(channel)
        val response = stub.listServers(ListServersRequest.getDefaultInstance())
        response.serversList.map { info ->
            ServerInfo(
                name = info.name,
                description = info.description,
                accountsCount = info.accountsCount,
                beaconHost = info.beaconUri.host,
                beaconPort = info.beaconUri.port,
                location = info.location,
                colorMain = info.color.mainHex,
                colorLite = info.color.liteHex,
                colorHard = info.color.hardHex,
            )
        }
    }

    suspend fun connectToServer(host: String, port: Int): Result<Unit> = runCatching {
        val channel = channelFactory.getChannel(host, port, false)
        val stub = BeaconApiGrpcKt.BeaconApiCoroutineStub(channel)
        val response = stub.getServerInfo(GetServerInfoRequest.getDefaultInstance())

        val config = ServerConfig(
            serverName = response.name,
            identityHost = response.identity.endpoint.host,
            identityPort = response.identity.endpoint.port,
            identityTls = response.identity.tlsEnabled,
            messagesHost = response.messages.endpoint.host,
            messagesPort = response.messages.endpoint.port,
            messagesTls = response.messages.tlsEnabled,
            colorMain = response.color.mainHex.ifEmpty { "#6200EE" },
            colorLite = response.color.liteHex.ifEmpty { "#BB86FC" },
            colorHard = response.color.hardHex.ifEmpty { "#3700B3" },
        )
        dataStore.saveServerConfig(config)
    }
}
