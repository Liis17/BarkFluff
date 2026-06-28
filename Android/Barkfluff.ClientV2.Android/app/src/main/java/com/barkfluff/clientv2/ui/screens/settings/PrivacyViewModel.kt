package com.barkfluff.clientv2.ui.screens.settings

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import barkfluff.users.UsersApiOuterClass.PrivacySettings
import barkfluff.users.UsersApiOuterClass.ProfileFieldVisibility
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class PrivacyUiState(
    val loading: Boolean = true,
    val settings: PrivacySettings = PrivacySettings.getDefaultInstance(),
    val error: String? = null,
)

class PrivacyViewModel(private val grpcManager: GrpcManager) : ViewModel() {

    private val _ui = MutableStateFlow(PrivacyUiState())
    val ui: StateFlow<PrivacyUiState> = _ui.asStateFlow()

    init {
        viewModelScope.launch {
            grpcManager.getPrivacySettings()
                .onSuccess { s -> _ui.update { it.copy(loading = false, settings = s) } }
                .onFailure { e -> _ui.update { it.copy(loading = false, error = e.message) } }
        }
    }

    /** Локально применяет изменение и отправляет полный набор настроек на сервер. */
    private fun mutate(transform: PrivacySettings.Builder.() -> Unit) {
        val updated = _ui.value.settings.toBuilder().apply(transform).build()
        _ui.update { it.copy(settings = updated) }
        viewModelScope.launch { grpcManager.updatePrivacySettings(updated) }
    }

    fun setProfileVisibleOnSite(value: Boolean) = mutate { setProfileVisibleOnSite(value) }
    fun setSearchVisible(value: Boolean) = mutate { setSearchVisible(value) }
    fun setAvatarVisibility(value: ProfileFieldVisibility) = mutate { setAvatarVisibility(value) }
    fun setBioVisibility(value: ProfileFieldVisibility) = mutate { setBioVisibility(value) }
    fun setEmailVisibility(value: ProfileFieldVisibility) = mutate { setEmailVisibility(value) }
    fun setOnlineVisibility(value: ProfileFieldVisibility) = mutate { setOnlineVisibility(value) }
}
