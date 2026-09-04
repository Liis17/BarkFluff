package com.barkfluff.client.adapter

import android.content.Context
import barkfluff.shared.Shared
import com.barkfluff.client.utils.FileMediaUrl
import java.io.File

/**
 * Small attachment projection seam shared by media/document renderers. I/O remains behind
 * [AttachmentLoader]; this class only resolves presentation inputs for a row.
 */
class MessageAttachmentRenderer(private val loader: AttachmentLoader) {
    fun previewUrl(context: Context, attachment: Shared.MessageAttachment): String =
        FileMediaUrl.rewrite(context, attachment.previewUrl)

    fun cachedPath(fileId: String): String? = loader.cached(fileId)?.absolutePath

    fun cachedFile(fileId: String): File? = loader.cached(fileId)
}
