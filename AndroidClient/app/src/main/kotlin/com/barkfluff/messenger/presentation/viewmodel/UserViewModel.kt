package com.barkfluff.messenger.presentation.viewmodel

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.messenger.data.repository.UserRepository
import com.barkfluff.messenger.domain.model.User
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class UserViewModel @Inject constructor(
    private val userRepository: UserRepository
) : ViewModel() {

    private val _searchResults = MutableStateFlow<List<User>>(emptyList())
    val searchResults: StateFlow<List<User>> = _searchResults.asStateFlow()

    private val _currentUser = MutableStateFlow<User?>(null)
    val currentUser: StateFlow<User?> = _currentUser.asStateFlow()

    fun searchUsers(query: String) {
        viewModelScope.launch {
            userRepository.searchUsers(query)
                .onSuccess { users ->
                    _searchResults.value = users
                }
                .onFailure {
                    _searchResults.value = emptyList()
                }
        }
    }

    fun loadUser(userId: Long) {
        viewModelScope.launch {
            userRepository.getUser(userId)
                .onSuccess { user ->
                    _currentUser.value = user
                }
        }
    }

    fun updateProfile(
        firstName: String? = null,
        lastName: String? = null,
        username: String? = null,
        bio: String? = null
    ) {
        viewModelScope.launch {
            userRepository.updateProfile(firstName, lastName, username, bio)
                .onSuccess {
                    // Reload current user
                    _currentUser.value?.let { loadUser(it.id) }
                }
        }
    }
}
