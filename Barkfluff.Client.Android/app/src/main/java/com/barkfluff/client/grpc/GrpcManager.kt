package com.barkfluff.client.grpc

import android.util.Log
import barkfluff.navigator.NavigatorApiOuterClass
import barkfluff.navigator.NavigatorApiGrpcKt
import barkfluff.beacon.BeaconApiOuterClass
import barkfluff.beacon.BeaconApiGrpcKt
import com.barkfluff.client.data.ClientColors
import com.barkfluff.client.data.ServerDataElement
import io.grpc.ManagedChannel
import io.grpc.ManagedChannelBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Менеджер для работы с gRPC
 * Аналог WebApiClientManager из WPF клиента
 */
class GrpcManager {
    
    companion object {
        private const val TAG = "GrpcManager"
        const val DEFAULT_NAVIGATOR_URL = "navigator.barkfluff.com:64646"
    }
    
    // gRPC каналы
    var navigatorChannel: ManagedChannel? = null
        private set
    var beaconChannel: ManagedChannel? = null
        private set
    
    // gRPC клиенты
    var navigatorClient: NavigatorApiGrpcKt.NavigatorApiCoroutineStub? = null
        private set
    var beaconClient: BeaconApiGrpcKt.BeaconApiCoroutineStub? = null
        private set
    
    /**
     * Создает только Beacon клиент для работы с Beacon API на сервере
     * Аналог CreateOnlyBeaconAC в WPF
     */
    fun createOnlyBeaconClient(beaconAddress: String): Result<Unit> {
        if (beaconAddress.isBlank()) {
            return Result.failure(IllegalArgumentException("Адрес Beacon сервера не указан"))
        }
        
        return try {
            val address = ensureHttpPrefix(beaconAddress)
            beaconChannel = createChannel(address)
            beaconClient = BeaconApiGrpcKt.BeaconApiCoroutineStub(beaconChannel!!)
            Log.d(TAG, "Beacon клиент создан: $address")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Beacon клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу: ${e.message}"))
        }
    }
    
    /**
     * Создает Navigator клиент для работы с навигатором
     * Аналог CreateNavigatorAC в WPF
     */
    fun createNavigatorClient(navigatorUrl: String = DEFAULT_NAVIGATOR_URL): Result<Unit> {
        if (navigatorUrl.isBlank()) {
            return Result.failure(IllegalArgumentException("URL навигатора не может быть пустым"))
        }
        
        return try {
            val address = ensureHttpPrefix(navigatorUrl)
            navigatorChannel = createChannel(address)
            navigatorClient = NavigatorApiGrpcKt.NavigatorApiCoroutineStub(navigatorChannel!!)
            Log.d(TAG, "Navigator клиент создан: $address")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Ошибка создания Navigator клиента", e)
            Result.failure(Exception("Ошибка подключения к серверу: ${e.message}"))
        }
    }
    
    /**
     * Получает список серверов из навигатора
     * Аналог GetServerList в WebApiServerManager
     */
    suspend fun getServerList(): Result<List<ServerDataElement>> = withContext(Dispatchers.IO) {
        try {
            if (navigatorClient == null) {
                return@withContext Result.failure(IllegalStateException("Navigator клиент не создан"))
            }
            
            val request = NavigatorApiOuterClass.ListServersRequest.newBuilder().build()
            val response = navigatorClient!!.listServers(request)
            
            val serverList = response.serversList.map { server ->
                ServerDataElement(
                    ip = "${server.beaconUri.host}:${server.beaconUri.port}",
                    title = server.name,
                    description = server.description,
                    userCount = server.accountsCount.toString(),
                    publicName = server.serverPublicName,
                    location = server.location,
                    hexColor = server.color.mainHex
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
     * Аналог GetServerInfo в WebApiServerManager
     */
    suspend fun getServerInfo(): Result<ServerInfo> = withContext(Dispatchers.IO) {
        try {
            if (beaconClient == null) {
                return@withContext Result.failure(IllegalStateException("Beacon клиент не создан"))
            }
            
            val request = BeaconApiOuterClass.GetServerInfoRequest.newBuilder().build()
            val response = beaconClient!!.getServerInfo(request)
            
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
            .usePlaintext() // Для отладки, в продакшене использовать TLS
            .build()
    }
    
    private fun ensureHttpPrefix(url: String): String {
        return if (!url.startsWith("http://") && !url.startsWith("https://")) {
            "http://$url"
        } else {
            url
        }
    }
    
    /**
     * Закрывает все gRPC каналы
     * Аналог Dispose в WPF
     */
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
