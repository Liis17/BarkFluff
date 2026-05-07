package com.barkfluff.client.editor

import android.net.Uri

/**
 * In-memory кеш отредактированных версий картинок (crop/rotate/flip/draw).
 * Живёт пока открыт ImagePickerBottomSheet — clearAll() вызывается из его onDestroy.
 */
object MediaEditCache {

    data class EditedImage(
        val bytes: ByteArray,
        val wasCropped: Boolean = false,
        val wasRotated: Boolean = false,
        val wasFlipped: Boolean = false,
        val wasDrawn: Boolean = false
    ) {
        override fun equals(other: Any?): Boolean {
            if (this === other) return true
            if (other !is EditedImage) return false
            return bytes.contentEquals(other.bytes) &&
                    wasCropped == other.wasCropped &&
                    wasRotated == other.wasRotated &&
                    wasFlipped == other.wasFlipped &&
                    wasDrawn == other.wasDrawn
        }

        override fun hashCode(): Int {
            var result = bytes.contentHashCode()
            result = 31 * result + wasCropped.hashCode()
            result = 31 * result + wasRotated.hashCode()
            result = 31 * result + wasFlipped.hashCode()
            result = 31 * result + wasDrawn.hashCode()
            return result
        }
    }

    private val edits: MutableMap<Uri, EditedImage> = mutableMapOf()

    @Synchronized
    fun put(uri: Uri, edited: EditedImage) {
        edits[uri] = edited
    }

    @Synchronized
    fun get(uri: Uri): EditedImage? = edits[uri]

    @Synchronized
    fun has(uri: Uri): Boolean = edits.containsKey(uri)

    @Synchronized
    fun clearAll() {
        edits.clear()
    }

    @Synchronized
    fun snapshot(): Map<Uri, EditedImage> = edits.toMap()
}
