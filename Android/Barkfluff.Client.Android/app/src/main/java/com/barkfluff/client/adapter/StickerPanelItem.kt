package com.barkfluff.client.adapter

import barkfluff.files.FilesApiOuterClass.StickerInfo

sealed class StickerPanelItem {
    data class PackHeader(
        val packId: String,
        val packName: String,
        val stickerCount: Int,
        val coverStickerId: String
    ) : StickerPanelItem()

    data class Sticker(
        val stickerInfo: StickerInfo,
        val packId: String
    ) : StickerPanelItem()

    data object Loading : StickerPanelItem()

    data object Empty : StickerPanelItem()
}
