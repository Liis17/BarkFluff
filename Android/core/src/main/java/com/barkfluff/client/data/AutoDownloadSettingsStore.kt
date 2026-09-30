package com.barkfluff.client.data

import android.content.SharedPreferences
import barkfluff.shared.Shared.MessageAttachmentType
import com.barkfluff.client.domain.media.AutoDownloadMode
import com.barkfluff.client.domain.media.AutoDownloadSettings
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** Process-wide observable view of the account's local media preferences. */
class AutoDownloadSettingsStore(private val globalParam: GlobalParam) {
    private val mutableSettings = MutableStateFlow(globalParam.autoDownloadSettings)
    val settings: StateFlow<AutoDownloadSettings> = mutableSettings.asStateFlow()

    private val listener = SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
        if (key == null || key.startsWith(GlobalParam.KEY_AUTO_DOWNLOAD_PREFIX)) {
            mutableSettings.value = globalParam.autoDownloadSettings
        }
    }

    init {
        globalParam.sharedPreferences.registerOnSharedPreferenceChangeListener(listener)
        mutableSettings.value = globalParam.autoDownloadSettings
    }

    fun setMode(type: MessageAttachmentType, mode: AutoDownloadMode) {
        require(type in AutoDownloadSettings.TYPES)
        val current = globalParam.autoDownloadSettings
        globalParam.autoDownloadSettings = current.copy(modes = current.modes + (type to mode))
    }

    fun setMaxSizeMb(value: Int) {
        globalParam.autoDownloadSettings = globalParam.autoDownloadSettings.copy(maxSizeMb = value)
    }
}
