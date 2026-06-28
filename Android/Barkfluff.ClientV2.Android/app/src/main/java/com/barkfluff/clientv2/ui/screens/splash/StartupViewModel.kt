package com.barkfluff.clientv2.ui.screens.splash

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.clientv2.ui.navigation.Routes
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

/**
 * Стартовый роутинг (аналог V1 SplashActivity.checkDataAndNavigate):
 * нет сервера → Welcome; есть сервер, нет токенов → Login; есть токены → обновить/проверить → Chats.
 */
class StartupViewModel(
    private val appContext: Context,
    private val grpcManager: GrpcManager,
    private val globalParam: GlobalParam
) : ViewModel() {

    private val _destination = MutableStateFlow<String?>(null)
    val destination: StateFlow<String?> = _destination.asStateFlow()

    init {
        viewModelScope.launch { _destination.value = resolveDestination() }
    }

    private suspend fun resolveDestination(): String {
        val hasServer = globalParam.socketBeacon.isNotBlank() && globalParam.socketIdentity.isNotBlank()
        if (!hasServer) return Routes.WELCOME

        val refresh = globalParam.refreshToken
        if (refresh.isNullOrBlank()) return Routes.LOGIN

        grpcManager.createIdentityClient(globalParam.socketIdentity, appContext)

        val tokenBufferMs = 5 * 60 * 1000L
        val accessExpired = System.currentTimeMillis() + tokenBufferMs >= globalParam.accessTokenExpiration
        if (globalParam.accessToken.isNullOrBlank() || accessExpired) {
            val r = grpcManager.refreshAccessToken(refresh, globalParam.refreshTokenExpiration).getOrNull()
                ?: return Routes.LOGIN
            globalParam.accessToken = r.accessToken
            globalParam.accessTokenExpiration = r.accessTokenExpiration
            globalParam.refreshToken = r.refreshToken
            globalParam.refreshTokenExpiration = r.refreshTokenExpiration
        }

        grpcManager.initAllClients(appContext, globalParam)

        var user = grpcManager.getCurrentUserData().getOrNull()
        if (user == null) {
            if (!grpcManager.forceRefreshToken(appContext)) return Routes.LOGIN
            user = grpcManager.getCurrentUserData().getOrNull() ?: return Routes.LOGIN
        }
        saveUser(user)
        grpcManager.recreateAllClients(appContext, globalParam)
        return Routes.CHATS
    }

    private fun saveUser(user: GrpcManager.UserData) {
        globalParam.apply {
            userId = user.userId
            userName = user.username
            firstName = user.firstName
            lastName = user.lastName
            description = user.bio
            pictureFileId = user.profilePictureFileId
            picturePreviewFileId = user.profilePicturePreviewFileId
            pictureUrl = user.profilePictureUrl
            picturePreviewUrl = user.profilePicturePreviewUrl
        }
    }
}
