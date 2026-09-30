package com.barkfluff.client.adapter

import java.io.File

/**
 * I/O boundary for message attachments. A row may ask for a URL or a cached copy, but it never
 * knows where the cache lives and never owns a network coroutine.
 */
interface AttachmentLoader {
    suspend fun url(fileId: String): String?
    suspend fun download(fileId: String, onProgress: (Int) -> Unit = {}): File?
    suspend fun downloadAuto(fileId: String, maxBytes: Long, onProgress: (Int) -> Unit = {}): File?
    fun cached(fileId: String): File?
    fun hasCached(fileId: String): Boolean
    fun deleteCached(fileId: String)
}

class CallbackAttachmentLoader(
    private val urlProvider: suspend (String) -> String?,
    private val downloadProvider: suspend (String, (Int) -> Unit) -> File?,
    private val cachedProvider: (String) -> File? = { null },
    private val deleteProvider: (String) -> Unit = {},
    private val autoDownloadProvider: suspend (String, Long, (Int) -> Unit) -> File? = { _, _, _ -> null },
) : AttachmentLoader {
    override suspend fun url(fileId: String): String? = urlProvider(fileId)
    override suspend fun download(fileId: String, onProgress: (Int) -> Unit): File? =
        downloadProvider(fileId, onProgress)
    override suspend fun downloadAuto(fileId: String, maxBytes: Long, onProgress: (Int) -> Unit): File? =
        autoDownloadProvider(fileId, maxBytes, onProgress)
    override fun cached(fileId: String): File? = cachedProvider(fileId)
    override fun hasCached(fileId: String): Boolean = cached(fileId) != null
    override fun deleteCached(fileId: String) = deleteProvider(fileId)
}

object EmptyAttachmentLoader : AttachmentLoader {
    override suspend fun url(fileId: String): String? = null
    override suspend fun download(fileId: String, onProgress: (Int) -> Unit): File? = null
    override suspend fun downloadAuto(fileId: String, maxBytes: Long, onProgress: (Int) -> Unit): File? = null
    override fun cached(fileId: String): File? = null
    override fun hasCached(fileId: String): Boolean = false
    override fun deleteCached(fileId: String) = Unit
}
