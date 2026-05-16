package com.barkfluff.client.share

import android.net.Uri

sealed class SharePayload {
    data class Text(val text: String) : SharePayload()
    data class SingleFile(val uri: Uri, val mime: String) : SharePayload()
    data class MultipleFiles(val items: List<Item>) : SharePayload() {
        data class Item(val uri: Uri, val mime: String)
    }
}
