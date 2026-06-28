package com.barkfluff.clientv2.ui.screens.profile

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

data class ProfileUiState(
    val loading: Boolean = false,
    val firstName: String = "",
    val lastName: String = "",
    val username: String = "",
    val bio: String = "",
    val avatarUrl: String = "",
    val registrationDate: Long = 0L,
) {
    val fullName: String get() = "$firstName $lastName".trim().ifBlank { username }
}

class ProfileViewModel(
    private val grpcManager: GrpcManager,
    private val globalParam: GlobalParam,
) : ViewModel() {

    private val _ui = MutableStateFlow(
        // Мгновенно показываем кэш из GlobalParam, затем обновляем с сервера.
        ProfileUiState(
            firstName = globalParam.firstName,
            lastName = globalParam.lastName,
            username = globalParam.userName,
            bio = globalParam.description,
            avatarUrl = globalParam.picturePreviewUrl,
            registrationDate = globalParam.registrationDate,
        )
    )
    val ui: StateFlow<ProfileUiState> = _ui.asStateFlow()

    fun refresh() {
        viewModelScope.launch {
            _ui.update { it.copy(loading = true) }
            grpcManager.getCurrentUserData()
                .onSuccess { u ->
                    globalParam.firstName = u.firstName
                    globalParam.lastName = u.lastName
                    globalParam.userName = u.username
                    globalParam.description = u.bio
                    globalParam.picturePreviewUrl = u.profilePicturePreviewUrl
                    globalParam.registrationDate = u.registrationDate
                    _ui.update {
                        it.copy(
                            loading = false,
                            firstName = u.firstName,
                            lastName = u.lastName,
                            username = u.username,
                            bio = u.bio,
                            avatarUrl = u.profilePicturePreviewUrl,
                            registrationDate = u.registrationDate,
                        )
                    }
                }
                .onFailure { _ui.update { it.copy(loading = false) } }
        }
    }
}
