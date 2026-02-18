package com.barkfluff.client.grpc

import android.util.Log
import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.ServerDataElement
import io.grpc.ManagedChannel
import io.grpc.ManagedChannelBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.net.URL

/**
 * Менеджер для работы с gRPC
 */
class SimpleGrpcManager {
    
    companion object {
        private const val TAG = "SimpleGrpcManager"
        const val DEFAULT_NAVIGATOR_URL = "navigator.barkfluff.com:64646"
    }
    
    private var navigatorChannel: ManagedChannel? = null
    private var beaconChannel: ManagedChannel? = null
    private var navigatorStub: NavigatorApiStub? = null
    private var beaconStub: BeaconApiStub? = null
    
    /**
     * Получает список серверов из навигатора
     */
    suspend fun getServerList(navigatorUrl: String = DEFAULT_NAVIGATOR_URL): Result<List<ServerDataElement>> = withContext(Dispatchers.IO) {
        try {
            if (navigatorChannel == null) {
                navigatorChannel = createChannel(navigatorUrl)
                navigatorStub = NavigatorApiStub(navigatorChannel!!)
            }
            
            val response = navigatorStub!!.listServers()
            
            val serverList = response.servers.map { server ->
                ServerDataElement(
                    ip = "${server.beaconUri.host}:${server.beaconUri.port}",
                    title = server.name,
                    description = server.description,
                    userCount = server.accountsCount.toString(),
                    publicName = server.serverPublicName,
                    location = server.location,
                    hexColor = server.color?.mainHex ?: ""
                )
            }
            
            Log.d(TAG, "Получено ${serverList.size} серверов")
            Result.success(serverList)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения списка серверов", e)
            Result.failure(Exception("Ошибка получения списка серверов: ${e.message}"))
        }
    }
    
    /**
     * Получает информацию о сервере через Beacon API
     */
    suspend fun getServerInfo(beaconAddress: String): Result<ServerInfo> = withContext(Dispatchers.IO) {
        try {
            if (beaconChannel == null) {
                beaconChannel = createChannel(beaconAddress)
                beaconStub = BeaconApiStub(beaconChannel!!)
            }
            
            val response = beaconStub!!.getServerInfo()
            
            val serverInfo = ServerInfo(
                name = response.name,
                description = response.description,
                color = ClientColors(
                    response.color.liteHex,
                    response.color.mainHex,
                    response.color.hardHex
                ),
                identityEndpoint = "${response.identity.endpoint.host}:${response.identity.endpoint.port}",
                usersEndpoint = "${response.users.endpoint.host}:${response.users.endpoint.port}",
                filesEndpoint = "${response.files.endpoint.host}:${response.files.endpoint.port}",
                messagesEndpoint = "${response.messages.endpoint.host}:${response.messages.endpoint.port}",
                updatesEndpoint = "${response.updates.endpoint.host}:${response.updates.endpoint.port}",
                onlinerEndpoint = "${response.onliner.endpoint.host}:${response.onliner.endpoint.port}"
            )
            
            Log.d(TAG, "Получена информация о сервере: ${serverInfo.name}")
            Result.success(serverInfo)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка получения информации о сервере", e)
            Result.failure(Exception("Ошибка получения информации о сервере: ${e.message}"))
        }
    }
    
    private fun createChannel(address: String): ManagedChannel {
        val url = ensureHttpPrefix(address)
        val hostPort = url.removePrefix("http://").removePrefix("https://")
        val parts = hostPort.split(":")
        val host = parts[0]
        val port = parts[1].toInt()
        
        return ManagedChannelBuilder.forAddress(host, port)
            .usePlaintext()
            .build()
    }
    
    private fun ensureHttpPrefix(url: String): String {
        return if (!url.startsWith("http://") && !url.startsWith("https://")) {
            "http://$url"
        } else {
            url
        }
    }
    
    fun shutdown() {
        navigatorChannel?.shutdown()
        beaconChannel?.shutdown()
    }
    
    data class ServerInfo(
        val name: String,
        val description: String,
        val color: ClientColors,
        val identityEndpoint: String,
        val usersEndpoint: String,
        val filesEndpoint: String,
        val messagesEndpoint: String,
        val updatesEndpoint: String,
        val onlinerEndpoint: String
    )
}

/**
 * Заглушка для Navigator API stub
 * В реальной реализации здесь будет gRPC вызов
 */
class NavigatorApiStub(private val channel: ManagedChannel) {
    
    suspend fun listServers(): NavigatorListServersResponse {
        // TODO: Реализовать реальный gRPC вызов
        // Это временная реализация с тестовыми данными
        return NavigatorListServersResponse(
            servers = listOf(
                NavigatorServerInfo(
                    name = "BarkFluff Public Server 1",
                    description = "Публичный сервер для тестирования",
                    accountsCount = 125,
                    beaconUri = ServiceEndpoint("test1.barkfluff.com", 64646),
                    serverPublicName = "barkfluff-public-1",
                    location = "Москва, RU",
                    color = ServerColor("#FFDAD0", "#FF6B35", "#4A2000")
                ),
                NavigatorServerInfo(
                    name = "BarkFluff Public Server 2",
                    description = "Второй публичный сервер",
                    accountsCount = 89,
                    beaconUri = ServiceEndpoint("test2.barkfluff.com", 64646),
                    serverPublicName = "barkfluff-public-2",
                    location = "Санкт-Петербург, RU",
                    color = ServerColor("#D0E4FF", "#2196F3", "#001F3A")
                )
            )
        )
    }
}

/**
 * Заглушка для Beacon API stub
 * В реальной реализации здесь будет gRPC вызов
 */
class BeaconApiStub(private val channel: ManagedChannel) {
    
    suspend fun getServerInfo(): BeaconGetServerInfoResponse {
        // TODO: Реализовать реальный gRPC вызов
        // Это временная реализация с тестовыми данными
        return BeaconGetServerInfoResponse(
            name = "Test Server",
            description = "Test Description",
            color = BeaconServerColor("#FFDAD0", "#FF6B35", "#4A2000"),
            identity = BeaconService("identity", ServiceEndpoint("test.com", 64646)),
            users = BeaconService("users", ServiceEndpoint("test.com", 64646)),
            files = BeaconService("files", ServiceEndpoint("test.com", 64646)),
            messages = BeaconService("messages", ServiceEndpoint("test.com", 64646)),
            updates = BeaconService("updates", ServiceEndpoint("test.com", 64646)),
            onliner = BeaconService("onliner", ServiceEndpoint("test.com", 64646))
        )
    }
}

// Data классы для Navigator API
data class NavigatorListServersResponse(
    val servers: List<NavigatorServerInfo>
)

data class NavigatorServerInfo(
    val name: String,
    val description: String,
    val accountsCount: Long,
    val beaconUri: ServiceEndpoint,
    val serverPublicName: String,
    val location: String,
    val color: ServerColor?
)

data class ServiceEndpoint(
    val host: String,
    val port: Int
)

data class ServerColor(
    val liteHex: String,
    val mainHex: String,
    val hardHex: String
)

// Data классы для Beacon API
data class BeaconGetServerInfoResponse(
    val name: String,
    val description: String,
    val color: BeaconServerColor,
    val identity: BeaconService,
    val users: BeaconService,
    val files: BeaconService,
    val messages: BeaconService,
    val updates: BeaconService,
    val onliner: BeaconService
)

data class BeaconServerColor(
    val liteHex: String,
    val mainHex: String,
    val hardHex: String
)

data class BeaconService(
    val name: String,
    val endpoint: ServiceEndpoint,
    val tlsEnabled: Boolean = false,
    val status: String = "Unknown"
)
