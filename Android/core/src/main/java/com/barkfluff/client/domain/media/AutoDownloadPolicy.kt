package com.barkfluff.client.domain.media

import barkfluff.shared.Shared.MessageAttachmentType

enum class AutoDownloadMode { WIFI_ONLY, ANY_NETWORK, MANUAL }

enum class AutoDownloadNetwork { WIFI, OTHER, UNAVAILABLE }

data class AutoDownloadSettings(
    val modes: Map<MessageAttachmentType, AutoDownloadMode> = emptyMap(),
    val maxSizeMb: Int = DEFAULT_MAX_SIZE_MB,
) {
    init {
        require(maxSizeMb in 1..512)
    }

    val maxBytes: Long get() = maxSizeMb * 1_048_576L

    fun mode(type: MessageAttachmentType): AutoDownloadMode = modes[type] ?: when (type) {
        MessageAttachmentType.IMAGE -> AutoDownloadMode.ANY_NETWORK
        in TYPES -> AutoDownloadMode.WIFI_ONLY
        else -> AutoDownloadMode.MANUAL
    }

    companion object {
        const val DEFAULT_MAX_SIZE_MB = 2
        val TYPES = listOf(
            MessageAttachmentType.IMAGE,
            MessageAttachmentType.GIF,
            MessageAttachmentType.VIDEO,
            MessageAttachmentType.AUDIO,
            MessageAttachmentType.VOICE,
            MessageAttachmentType.DOCUMENT,
        )
    }
}

object AutoDownloadPolicy {
    fun mayStart(
        settings: AutoDownloadSettings,
        type: MessageAttachmentType,
        sizeBytes: Long,
        network: AutoDownloadNetwork,
    ): Boolean {
        if (type !in AutoDownloadSettings.TYPES || sizeBytes <= 0 || sizeBytes > settings.maxBytes) return false
        return when (settings.mode(type)) {
            AutoDownloadMode.MANUAL -> false
            AutoDownloadMode.WIFI_ONLY -> network == AutoDownloadNetwork.WIFI
            AutoDownloadMode.ANY_NETWORK -> network != AutoDownloadNetwork.UNAVAILABLE
        }
    }
}
