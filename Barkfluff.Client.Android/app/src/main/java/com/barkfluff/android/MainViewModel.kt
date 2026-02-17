package com.barkfluff.android

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.barkfluff.android.data.local.AppDataStore
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.util.UUID
import javax.inject.Inject

@HiltViewModel
class MainViewModel @Inject constructor(
    private val dataStore: AppDataStore,
) : ViewModel() {

    private val _isReady = MutableStateFlow(false)
    val isReady: StateFlow<Boolean> = _isReady.asStateFlow()

    // null = stay on Welcome, non-null = navigate to this fragment ID
    private val _startDest = MutableStateFlow<Int?>(null)
    val startDest: StateFlow<Int?> = _startDest.asStateFlow()

    init {
        viewModelScope.launch {
            ensureDeviceId()
            _startDest.value = when {
                !dataStore.hasServerConfig() -> null
                !dataStore.isLoggedIn() -> R.id.loginFragment
                else -> R.id.chatListFragment
            }
            _isReady.value = true
        }
    }

    private suspend fun ensureDeviceId() {
        val existing = dataStore.getDeviceId()
        if (existing.isEmpty()) {
            dataStore.saveDeviceId(UUID.randomUUID().toString())
        }
    }
}
