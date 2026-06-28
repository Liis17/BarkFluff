package com.barkfluff.clientv2.ui.screens.server

import android.content.Context
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.ServerDataElement
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class SelectServerUiState(
    val loading: Boolean = false,
    val servers: List<ServerDataElement> = emptyList(),
    val connecting: Boolean = false,
    val error: String? = null,
)

class SelectServerViewModel(
    private val grpcManager: GrpcManager,
    private val globalParam: GlobalParam
) : ViewModel() {

    private val _ui = MutableStateFlow(SelectServerUiState())
    val ui: StateFlow<SelectServerUiState> = _ui.asStateFlow()

    init {
        loadServers()
    }

    fun loadServers() {
        viewModelScope.launch {
            _ui.update { it.copy(loading = true, error = null) }
            grpcManager.createNavigatorClient()
            grpcManager.getServerList()
                .onSuccess { list -> _ui.update { it.copy(loading = false, servers = list) } }
                .onFailure { e -> _ui.update { it.copy(loading = false, error = e.message ?: "Не удалось загрузить список серверов") } }
        }
    }

    fun connect(address: String, onConnected: () -> Unit) {
        val target = address.trim()
        if (target.isEmpty()) return
        viewModelScope.launch {
            _ui.update { it.copy(connecting = true, error = null) }
            globalParam.socketBeacon = target

            if (grpcManager.createOnlyBeaconClient(target).isFailure) {
                _ui.update { it.copy(connecting = false, error = "Не удалось подключиться к серверу") }
                return@launch
            }

            val info = grpcManager.getServerInfo().getOrElse { e ->
                _ui.update { it.copy(connecting = false, error = e.message ?: "Сервер недоступен") }
                return@launch
            }

            globalParam.apply {
                serverName = info.name
                serverDescription = info.description
                socketIdentity = ensureHttpPrefix(info.identityEndpoint)
                socketUsers = ensureHttpPrefix(info.usersEndpoint)
                socketFiles = ensureHttpPrefix(info.filesEndpoint)
                socketMessages = ensureHttpPrefix(info.messagesEndpoint)
                socketUpdates = ensureHttpPrefix(info.updatesEndpoint)
                socketOnliner = ensureHttpPrefix(info.onlinerEndpoint)
                socketFastAuth = ensureHttpPrefix(info.fastAuthEndpoint)
                colors = info.color
            }
            grpcManager.createIdentityClient(globalParam.socketIdentity)
            _ui.update { it.copy(connecting = false) }
            onConnected()
        }
    }

    private fun ensureHttpPrefix(url: String): String =
        if (!url.startsWith("http://") && !url.startsWith("https://")) "http://$url" else url
}
