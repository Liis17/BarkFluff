package com.barkfluff.client.editor

import android.net.Uri

/**
 * Спецификация правок над видео из VideoEditorActivity.
 * Хранится в VideoEditCache на время жизни picker'а (передавать через Intent смысла нет).
 */
data class EditedVideoSpec(
    val uri: Uri,
    val trimStartMs: Long = 0L,
    /** -1 = до конца видео */
    val trimEndMs: Long = -1L,
    val compressTo480p: Boolean = true
)

/**
 * In-memory кеш правок видео (trim, compress flag) — поведение и время жизни как у MediaEditCache.
 */
object VideoEditCache {

    private val specs: MutableMap<Uri, EditedVideoSpec> = mutableMapOf()

    @Synchronized
    fun put(spec: EditedVideoSpec) {
        specs[spec.uri] = spec
    }

    @Synchronized
    fun get(uri: Uri): EditedVideoSpec? = specs[uri]

    @Synchronized
    fun has(uri: Uri): Boolean = specs.containsKey(uri)

    @Synchronized
    fun clearAll() {
        specs.clear()
    }
}
