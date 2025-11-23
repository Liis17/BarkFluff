package com.barkfluff.messenger.data.repository

import com.barkfluff.messenger.data.local.SessionManager
import com.barkfluff.messenger.domain.model.AuthToken
import io.grpc.ManagedChannel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.withContext
import java.time.Instant
import javax.inject.Inject
import javax.inject.Named

class FastAuthRepository @Inject constructor(
    @Named("fastAuthChannel") private val fastAuthChannel: ManagedChannel,
    @Named("identityChannel") private val identityChannel: ManagedChannel,
    private val sessionManager: SessionManager
) {

    // 1. Generate token for device connection (QR code or text)
    suspend fun generateConnectDeviceToken(
        format: TokenFormat = TokenFormat.QR
    ): Result<FastAuthTokenData> = withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val formatProto = when (format) {
                TokenFormat.QR -> fast_auth_api.FastAuthApi.TokenFormat.TOKEN_FORMAT_QR
                TokenFormat.TEXT -> fast_auth_api.FastAuthApi.TokenFormat.TOKEN_FORMAT_TEXT
            }

            val request = fast_auth_api.FastAuthApi.GenerateConnectDeviceTokenRequest.newBuilder()
                .setFormat(formatProto)
                .build()

            val response = stub.generateConnectDeviceToken(request)

            val tokenData = FastAuthTokenData(
                format = when (response.token.format) {
                    fast_auth_api.FastAuthApi.TokenFormat.TOKEN_FORMAT_QR -> TokenFormat.QR
                    fast_auth_api.FastAuthApi.TokenFormat.TOKEN_FORMAT_TEXT -> TokenFormat.TEXT
                    else -> TokenFormat.QR
                },
                value = response.token.value
            )

            Result.success(tokenData)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 2. Connect device using fast auth token
    suspend fun connectDevice(): Result<String> = withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.ConnectDeviceRequest.newBuilder().build()
            val response = stub.connectDevice(request)

            Result.success(response.connectId)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 3. Accept device connection request
    suspend fun acceptConnectDevice(connectId: String): Result<ConnectedDeviceInfo> =
        withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.AcceptConnectDeviceRequest.newBuilder()
                .setConnectId(connectId)
                .build()

            val response = stub.acceptConnectDevice(request)
            val device = response.device

            val deviceInfo = ConnectedDeviceInfo(
                id = device.id,
                name = device.name,
                ip = device.ip,
                connectedAt = Instant.ofEpochSecond(
                    device.connectedAt.seconds,
                    device.connectedAt.nanos.toLong()
                )
            )

            Result.success(deviceInfo)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 4. Subscribe to device connection status (streaming)
    fun subscribeConnectDeviceStatus(connectId: String): Flow<ConnectDeviceStatusUpdate> = flow {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.SubscribeConnectDeviceStatusRequest.newBuilder()
                .setConnectId(connectId)
                .build()

            val responseIterator = stub.subscribeConnectDeviceStatus(request)

            responseIterator.forEach { statusProto ->
                val status = when (statusProto.status) {
                    fast_auth_api.FastAuthApi.ConnectedDeviceStatus.CONNECTED_DEVICE_STATUS_ACCEPTED ->
                        ConnectedDeviceStatusType.ACCEPTED
                    fast_auth_api.FastAuthApi.ConnectedDeviceStatus.CONNECTED_DEVICE_STATUS_REJECTED ->
                        ConnectedDeviceStatusType.REJECTED
                    fast_auth_api.FastAuthApi.ConnectedDeviceStatus.CONNECTED_DEVICE_STATUS_PENDING ->
                        ConnectedDeviceStatusType.PENDING
                    else -> ConnectedDeviceStatusType.UNKNOWN
                }

                val device = if (statusProto.hasDevice()) {
                    ConnectedDeviceInfo(
                        id = statusProto.device.id,
                        name = statusProto.device.name,
                        ip = statusProto.device.ip,
                        connectedAt = Instant.ofEpochSecond(
                            statusProto.device.connectedAt.seconds,
                            statusProto.device.connectedAt.nanos.toLong()
                        )
                    )
                } else null

                emit(ConnectDeviceStatusUpdate(
                    status = status,
                    device = device,
                    fastToken = statusProto.fastToken.takeIf { it.isNotBlank() }
                ))
            }
        } catch (e: Exception) {
            // Flow will complete on error
        }
    }

    // 5. List all connected devices
    suspend fun listConnectedDevices(): Result<List<ConnectedDeviceInfo>> =
        withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.ListConnectedDevicesRequest.newBuilder().build()
            val response = stub.listConnectedDevices(request)

            val devices = response.connectedDevicesList.map { deviceProto ->
                ConnectedDeviceInfo(
                    id = deviceProto.id,
                    name = deviceProto.name,
                    ip = deviceProto.ip,
                    connectedAt = Instant.ofEpochSecond(
                        deviceProto.connectedAt.seconds,
                        deviceProto.connectedAt.nanos.toLong()
                    )
                )
            }

            Result.success(devices)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 6. Remove connected device
    suspend fun removeConnectedDevice(deviceId: String): Result<Unit> =
        withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.RemoveConnectedDeviceRequest.newBuilder()
                .setConnectedDeviceId(deviceId)
                .build()

            stub.removeConnectedDevice(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 7. Generate fast auth token for login (no authorization required)
    suspend fun generateFastAuthToken(
        format: TokenFormat = TokenFormat.QR
    ): Result<FastAuthTokenResponse> = withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val formatProto = when (format) {
                TokenFormat.QR -> fast_auth_api.FastAuthApi.TokenFormat.TOKEN_FORMAT_QR
                TokenFormat.TEXT -> fast_auth_api.FastAuthApi.TokenFormat.TOKEN_FORMAT_TEXT
            }

            val request = fast_auth_api.FastAuthApi.GenerateFastAuthTokenRequest.newBuilder()
                .setFormat(formatProto)
                .build()

            val response = stub.generateFastAuthToken(request)

            val tokenData = FastAuthTokenResponse(
                token = FastAuthTokenData(
                    format = when (response.token.format) {
                        fast_auth_api.FastAuthApi.TokenFormat.TOKEN_FORMAT_QR -> TokenFormat.QR
                        fast_auth_api.FastAuthApi.TokenFormat.TOKEN_FORMAT_TEXT -> TokenFormat.TEXT
                        else -> TokenFormat.QR
                    },
                    value = response.token.value
                ),
                fastAuthId = response.fastAuthId
            )

            Result.success(tokenData)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 8. Check if fast auth is available for user
    suspend fun checkFastAuth(
        username: String? = null,
        email: String? = null
    ): Result<FastAuthAvailability> = withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.CheckFastAuthRequest.newBuilder().apply {
                username?.let { setUsername(it) }
                email?.let { setEmail(it) }
            }.build()

            val response = stub.checkFastAuth(request)

            val devices = response.devicesList.map { deviceProto ->
                ConnectedDeviceInfo(
                    id = deviceProto.id,
                    name = deviceProto.name,
                    ip = deviceProto.ip,
                    connectedAt = Instant.ofEpochSecond(
                        deviceProto.connectedAt.seconds,
                        deviceProto.connectedAt.nanos.toLong()
                    )
                )
            }

            Result.success(FastAuthAvailability(
                isAvailable = response.isAvailable,
                devices = devices
            ))
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 9. Create fast auth request (initiate login from new device)
    suspend fun createFastAuth(
        username: String? = null,
        email: String? = null,
        deviceIds: List<String> = emptyList()
    ): Result<String> = withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.CreateFastAuthRequest.newBuilder().apply {
                username?.let { setUsername(it) }
                email?.let { setEmail(it) }
                addAllDeviceIds(deviceIds)
            }.build()

            val response = stub.createFastAuth(request)

            Result.success(response.fastAuthId)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 10. Accept fast auth request (approve login on trusted device)
    suspend fun acceptFastAuth(fastAuthId: String): Result<Unit> =
        withContext(Dispatchers.IO) {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.AcceptFastAuthRequest.newBuilder()
                .setFastAuthId(fastAuthId)
                .build()

            stub.acceptFastAuth(request)
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // 11. Subscribe to incoming fast auth requests (streaming)
    fun subscribeFastAuthRequests(): Flow<FastAuthRequestInfo> = flow {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.SubscribeFastAuthRequestsRequest.newBuilder().build()
            val responseIterator = stub.subscribeFastAuthRequests(request)

            responseIterator.forEach { requestProto ->
                emit(FastAuthRequestInfo(
                    fastAuthId = requestProto.fastAuthId,
                    deviceName = requestProto.deviceName,
                    osName = requestProto.osName,
                    ip = requestProto.ip,
                    dieAt = Instant.ofEpochSecond(
                        requestProto.dieAt.seconds,
                        requestProto.dieAt.nanos.toLong()
                    )
                ))
            }
        } catch (e: Exception) {
            // Flow will complete on error
        }
    }

    // 12. Subscribe to fast auth result (streaming)
    fun subscribeFastAuthResult(fastAuthId: String): Flow<FastAuthStatus> = flow {
        try {
            val stub = fast_auth_api.FastAuthApiGrpc.newBlockingStub(fastAuthChannel)

            val request = fast_auth_api.FastAuthApi.SubscribeFastAuthResultRequest.newBuilder()
                .setFastAuthId(fastAuthId)
                .build()

            val responseIterator = stub.subscribeFastAuthResult(request)

            responseIterator.forEach { resultProto ->
                val status = when (resultProto.status) {
                    fast_auth_api.FastAuthApi.FastAuthStatus.FAST_AUTH_STATUS_ACCEPTED ->
                        FastAuthStatus.ACCEPTED
                    fast_auth_api.FastAuthApi.FastAuthStatus.FAST_AUTH_STATUS_REJECTED ->
                        FastAuthStatus.REJECTED
                    fast_auth_api.FastAuthApi.FastAuthStatus.FAST_AUTH_STATUS_PENDING ->
                        FastAuthStatus.PENDING
                    else -> FastAuthStatus.UNKNOWN
                }
                emit(status)
            }
        } catch (e: Exception) {
            // Flow will complete on error
        }
    }

    // Fast auth login - use token to login
    suspend fun fastAuthLogin(fastAuthToken: String): Result<AuthToken> =
        withContext(Dispatchers.IO) {
        try {
            val stub = identity_api.IdentityApiGrpc.newBlockingStub(identityChannel)

            val request = identity_api.IdentityApi.FastAuthRequest.newBuilder()
                .setFastToken(fastAuthToken)
                .build()

            val response = stub.fastAuth(request)

            val token = AuthToken(
                accessToken = response.accessToken.value,
                refreshToken = response.refreshToken.value,
                accessTokenExpiration = Instant.ofEpochSecond(
                    response.accessToken.expirationDate.seconds,
                    response.accessToken.expirationDate.nanos.toLong()
                ),
                refreshTokenExpiration = Instant.ofEpochSecond(
                    response.refreshToken.expirationDate.seconds,
                    response.refreshToken.expirationDate.nanos.toLong()
                )
            )

            sessionManager.saveToken(token)
            sessionManager.saveUserId(response.userId)

            Result.success(token)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}

// Data classes

data class FastAuthTokenData(
    val format: TokenFormat,
    val value: String
)

data class FastAuthTokenResponse(
    val token: FastAuthTokenData,
    val fastAuthId: String
)

data class ConnectedDeviceInfo(
    val id: String,
    val name: String,
    val ip: String,
    val connectedAt: Instant
)

data class ConnectDeviceStatusUpdate(
    val status: ConnectedDeviceStatusType,
    val device: ConnectedDeviceInfo?,
    val fastToken: String?
)

data class FastAuthAvailability(
    val isAvailable: Boolean,
    val devices: List<ConnectedDeviceInfo>
)

data class FastAuthRequestInfo(
    val fastAuthId: String,
    val deviceName: String,
    val osName: String,
    val ip: String,
    val dieAt: Instant
)

enum class TokenFormat {
    QR,
    TEXT
}

enum class ConnectedDeviceStatusType {
    ACCEPTED,
    REJECTED,
    PENDING,
    UNKNOWN
}

enum class FastAuthStatus {
    ACCEPTED,
    REJECTED,
    PENDING,
    UNKNOWN
}
