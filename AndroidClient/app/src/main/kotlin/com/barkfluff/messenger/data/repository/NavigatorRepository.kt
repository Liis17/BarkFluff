package com.barkfluff.messenger.data.repository

import com.barkfluff.messenger.domain.model.ServerColor
import com.barkfluff.messenger.domain.model.ServerInfo
import io.grpc.ManagedChannel
import io.grpc.ManagedChannelBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import javax.inject.Inject

data class ServerListItem(
    val name: String,
    val description: String,
    val accountsCount: Long,
    val beaconEndpoint: String
)

class NavigatorRepository @Inject constructor() {

    suspend fun listServers(navigatorEndpoint: String): Result<List<ServerListItem>> =
        withContext(Dispatchers.IO) {
            try {
                val channel = ManagedChannelBuilder.forTarget(navigatorEndpoint)
                    .usePlaintext()
                    .build()

                val stub = navigator_api.NavigatorApiGrpc.newBlockingStub(channel)

                val request = navigator_api.NavigatorApi.ListServersRequest.newBuilder().build()
                val response = stub.listServers(request)

                val servers = response.serversList.map { server ->
                    ServerListItem(
                        name = server.name,
                        description = server.description,
                        accountsCount = server.accountsCount,
                        beaconEndpoint = "${server.beaconUri.host}:${server.beaconUri.port}"
                    )
                }

                channel.shutdown()

                Result.success(servers)
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    suspend fun getServerInfo(beaconEndpoint: String): Result<ServerInfo> =
        withContext(Dispatchers.IO) {
            try {
                val channel = ManagedChannelBuilder.forTarget(beaconEndpoint)
                    .usePlaintext()
                    .build()

                val stub = beacon_api.BeaconApiGrpc.newBlockingStub(channel)

                val request = beacon_api.BeaconApi.GetServerInfoRequest.newBuilder().build()
                val response = stub.getServerInfo(request)

                val serverInfo = ServerInfo(
                    name = response.name,
                    description = response.description,
                    color = ServerColor(
                        liteHex = response.color.liteHex,
                        mainHex = response.color.mainHex,
                        hardHex = response.color.hardHex
                    ),
                    beaconEndpoint = beaconEndpoint,
                    identityEndpoint = "${response.identity.endpoint.host}:${response.identity.endpoint.port}",
                    usersEndpoint = "${response.users.endpoint.host}:${response.users.endpoint.port}",
                    filesEndpoint = "${response.files.endpoint.host}:${response.files.endpoint.port}",
                    messagesEndpoint = "${response.messages.endpoint.host}:${response.messages.endpoint.port}",
                    updatesEndpoint = "${response.updates.endpoint.host}:${response.updates.endpoint.port}"
                )

                channel.shutdown()

                Result.success(serverInfo)
            } catch (e: Exception) {
                Result.failure(e)
            }
        }
}
