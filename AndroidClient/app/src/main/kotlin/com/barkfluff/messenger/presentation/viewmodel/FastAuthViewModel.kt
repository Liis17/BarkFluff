package com.barkfluff.messenger.presentation.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.messenger.data.repository.*
import com.barkfluff.messenger.domain.model.AuthToken
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class FastAuthViewModel @Inject constructor(
    private val fastAuthRepository: FastAuthRepository
) : ViewModel() {

    private val _state = MutableStateFlow(FastAuthState())
    val state: StateFlow<FastAuthState> = _state.asStateFlow()

    private val _events = Channel<FastAuthEvent>(Channel.BUFFERED)
    val events: Flow<FastAuthEvent> = _events.receiveAsFlow()

    private val _connectedDevices = MutableStateFlow<List<ConnectedDeviceInfo>>(emptyList())
    val connectedDevices: StateFlow<List<ConnectedDeviceInfo>> = _connectedDevices.asStateFlow()

    private val _incomingRequests = MutableStateFlow<List<FastAuthRequestInfo>>(emptyList())
    val incomingRequests: StateFlow<List<FastAuthRequestInfo>> = _incomingRequests.asStateFlow()

    // Generate QR code for connecting a new device
    fun generateConnectDeviceQr() {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            fastAuthRepository.generateConnectDeviceToken(TokenFormat.QR)
                .onSuccess { tokenData ->
                    _state.value = _state.value.copy(
                        isLoading = false,
                        connectDeviceToken = tokenData.value
                    )
                    _events.send(FastAuthEvent.ConnectTokenGenerated(tokenData.value))
                }
                .onFailure { error ->
                    _state.value = _state.value.copy(isLoading = false)
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to generate QR"))
                }
        }
    }

    // Connect this device using scanned QR token
    fun connectDevice() {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            fastAuthRepository.connectDevice()
                .onSuccess { connectId ->
                    _state.value = _state.value.copy(
                        isLoading = false,
                        connectId = connectId
                    )
                    // Start monitoring connection status
                    monitorConnectionStatus(connectId)
                }
                .onFailure { error ->
                    _state.value = _state.value.copy(isLoading = false)
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to connect device"))
                }
        }
    }

    // Monitor device connection status (streaming)
    private fun monitorConnectionStatus(connectId: String) {
        viewModelScope.launch {
            fastAuthRepository.subscribeConnectDeviceStatus(connectId)
                .collect { statusUpdate ->
                    when (statusUpdate.status) {
                        ConnectedDeviceStatusType.ACCEPTED -> {
                            statusUpdate.device?.let { device ->
                                _events.send(FastAuthEvent.DeviceConnected(device))
                            }
                            statusUpdate.fastToken?.let { token ->
                                // Save fast token for future fast auth
                                _state.value = _state.value.copy(fastAuthToken = token)
                            }
                        }
                        ConnectedDeviceStatusType.REJECTED -> {
                            _events.send(FastAuthEvent.DeviceConnectionRejected)
                        }
                        ConnectedDeviceStatusType.PENDING -> {
                            _state.value = _state.value.copy(connectionStatus = "Pending approval...")
                        }
                        ConnectedDeviceStatusType.UNKNOWN -> {
                            _events.send(FastAuthEvent.Error("Unknown connection status"))
                        }
                    }
                }
        }
    }

    // Accept device connection request (on trusted device)
    fun acceptConnectDevice(connectId: String) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            fastAuthRepository.acceptConnectDevice(connectId)
                .onSuccess { device ->
                    _state.value = _state.value.copy(isLoading = false)
                    _events.send(FastAuthEvent.DeviceConnectionAccepted(device))
                    loadConnectedDevices() // Refresh device list
                }
                .onFailure { error ->
                    _state.value = _state.value.copy(isLoading = false)
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to accept device"))
                }
        }
    }

    // Load all connected devices
    fun loadConnectedDevices() {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            fastAuthRepository.listConnectedDevices()
                .onSuccess { devices ->
                    _connectedDevices.value = devices
                    _state.value = _state.value.copy(isLoading = false)
                }
                .onFailure { error ->
                    _state.value = _state.value.copy(isLoading = false)
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to load devices"))
                }
        }
    }

    // Remove a connected device
    fun removeConnectedDevice(deviceId: String) {
        viewModelScope.launch {
            fastAuthRepository.removeConnectedDevice(deviceId)
                .onSuccess {
                    _events.send(FastAuthEvent.DeviceRemoved)
                    loadConnectedDevices() // Refresh list
                }
                .onFailure { error ->
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to remove device"))
                }
        }
    }

    // Generate fast auth QR for login
    fun generateFastAuthQr() {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            fastAuthRepository.generateFastAuthToken(TokenFormat.QR)
                .onSuccess { response ->
                    _state.value = _state.value.copy(
                        isLoading = false,
                        fastAuthQrToken = response.token.value,
                        fastAuthId = response.fastAuthId
                    )
                    // Start monitoring for approval
                    monitorFastAuthResult(response.fastAuthId)
                }
                .onFailure { error ->
                    _state.value = _state.value.copy(isLoading = false)
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to generate QR"))
                }
        }
    }

    // Check if fast auth is available for user
    fun checkFastAuthAvailability(username: String? = null, email: String? = null) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            fastAuthRepository.checkFastAuth(username, email)
                .onSuccess { availability ->
                    _state.value = _state.value.copy(
                        isLoading = false,
                        fastAuthAvailable = availability.isAvailable,
                        availableDevices = availability.devices
                    )
                }
                .onFailure { error ->
                    _state.value = _state.value.copy(isLoading = false)
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to check availability"))
                }
        }
    }

    // Create fast auth request (initiate login from new device)
    fun createFastAuthRequest(username: String? = null, email: String? = null, deviceIds: List<String> = emptyList()) {
        viewModelScope.launch {
            _state.value = _state.value.copy(isLoading = true)

            fastAuthRepository.createFastAuth(username, email, deviceIds)
                .onSuccess { fastAuthId ->
                    _state.value = _state.value.copy(
                        isLoading = false,
                        fastAuthId = fastAuthId
                    )
                    // Start monitoring for result
                    monitorFastAuthResult(fastAuthId)
                }
                .onFailure { error ->
                    _state.value = _state.value.copy(isLoading = false)
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to create request"))
                }
        }
    }

    // Accept fast auth request (approve login on trusted device)
    fun acceptFastAuthRequest(fastAuthId: String) {
        viewModelScope.launch {
            fastAuthRepository.acceptFastAuth(fastAuthId)
                .onSuccess {
                    _events.send(FastAuthEvent.FastAuthAccepted)
                }
                .onFailure { error ->
                    _events.send(FastAuthEvent.Error(error.message ?: "Failed to accept request"))
                }
        }
    }

    // Subscribe to incoming fast auth requests (for trusted devices)
    fun subscribeToFastAuthRequests() {
        viewModelScope.launch {
            fastAuthRepository.subscribeFastAuthRequests()
                .collect { request ->
                    val currentRequests = _incomingRequests.value.toMutableList()
                    currentRequests.add(request)
                    _incomingRequests.value = currentRequests
                    _events.send(FastAuthEvent.NewFastAuthRequest(request))
                }
        }
    }

    // Monitor fast auth result (streaming)
    private fun monitorFastAuthResult(fastAuthId: String) {
        viewModelScope.launch {
            fastAuthRepository.subscribeFastAuthResult(fastAuthId)
                .collect { status ->
                    when (status) {
                        FastAuthStatus.ACCEPTED -> {
                            _events.send(FastAuthEvent.FastAuthApproved)
                            // Now perform the actual login
                            performFastAuthLogin()
                        }
                        FastAuthStatus.REJECTED -> {
                            _events.send(FastAuthEvent.FastAuthRejected)
                        }
                        FastAuthStatus.PENDING -> {
                            _state.value = _state.value.copy(
                                fastAuthStatus = "Waiting for approval..."
                            )
                        }
                        FastAuthStatus.UNKNOWN -> {
                            _events.send(FastAuthEvent.Error("Unknown status"))
                        }
                    }
                }
        }
    }

    // Perform login using fast auth token
    private fun performFastAuthLogin() {
        viewModelScope.launch {
            val token = _state.value.fastAuthToken
            if (token == null) {
                _events.send(FastAuthEvent.Error("No fast auth token available"))
                return@launch
            }

            fastAuthRepository.fastAuthLogin(token)
                .onSuccess { authToken ->
                    _events.send(FastAuthEvent.LoginSuccess(authToken))
                }
                .onFailure { error ->
                    _events.send(FastAuthEvent.Error(error.message ?: "Login failed"))
                }
        }
    }
}

data class FastAuthState(
    val isLoading: Boolean = false,
    val connectDeviceToken: String? = null,
    val connectId: String? = null,
    val connectionStatus: String? = null,
    val fastAuthToken: String? = null,
    val fastAuthQrToken: String? = null,
    val fastAuthId: String? = null,
    val fastAuthStatus: String? = null,
    val fastAuthAvailable: Boolean = false,
    val availableDevices: List<ConnectedDeviceInfo> = emptyList()
)

sealed class FastAuthEvent {
    data class ConnectTokenGenerated(val token: String) : FastAuthEvent()
    data class DeviceConnected(val device: ConnectedDeviceInfo) : FastAuthEvent()
    data class DeviceConnectionAccepted(val device: ConnectedDeviceInfo) : FastAuthEvent()
    object DeviceConnectionRejected : FastAuthEvent()
    object DeviceRemoved : FastAuthEvent()
    data class NewFastAuthRequest(val request: FastAuthRequestInfo) : FastAuthEvent()
    object FastAuthAccepted : FastAuthEvent()
    object FastAuthApproved : FastAuthEvent()
    object FastAuthRejected : FastAuthEvent()
    data class LoginSuccess(val token: AuthToken) : FastAuthEvent()
    data class Error(val message: String) : FastAuthEvent()
}
