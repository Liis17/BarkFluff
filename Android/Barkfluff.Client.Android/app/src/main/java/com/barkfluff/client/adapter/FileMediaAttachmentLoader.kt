package com.barkfluff.client.adapter

import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.utils.FileCache
import java.io.File

/** Production attachment loader. Network and cache ownership stays outside MessageAdapter. */
class FileMediaAttachmentLoader(
    private val mediaGateway: FileMediaGateway,
) : AttachmentLoader {
    override suspend fun url(fileId: String): String? = mediaGateway.downloadUrl(fileId).getOrNull()
    override suspend fun download(fileId: String, onProgress: (Int) -> Unit): File? =
        mediaGateway.download(fileId, onProgress)
    override fun cached(fileId: String): File? = FileCache.getFile(fileId)
    override fun hasCached(fileId: String): Boolean = FileCache.hasFile(fileId)
    override fun deleteCached(fileId: String) {
        FileCache.deleteFile(fileId)
    }
}
