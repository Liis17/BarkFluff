package com.barkfluff.client.domain.media

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** Updated by the application's existing default-network callback. */
class AutoDownloadNetworkState {
    private var currentNetwork: Any? = null
    private val mutableNetwork = MutableStateFlow(AutoDownloadNetwork.UNAVAILABLE)
    val network: StateFlow<AutoDownloadNetwork> = mutableNetwork.asStateFlow()

    @Synchronized
    fun available(network: Any) {
        currentNetwork = network
        mutableNetwork.value = AutoDownloadNetwork.UNAVAILABLE
    }

    @Synchronized
    fun capabilities(network: Any, wifi: Boolean, cellular: Boolean) {
        if (network != currentNetwork) return
        mutableNetwork.value = if (wifi && !cellular) AutoDownloadNetwork.WIFI else AutoDownloadNetwork.OTHER
    }

    @Synchronized
    fun lost(network: Any) {
        if (network != currentNetwork) return
        currentNetwork = null
        mutableNetwork.value = AutoDownloadNetwork.UNAVAILABLE
    }
}
