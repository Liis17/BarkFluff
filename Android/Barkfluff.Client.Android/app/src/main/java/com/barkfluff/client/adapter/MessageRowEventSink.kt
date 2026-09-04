package com.barkfluff.client.adapter

import java.io.File
import android.view.View

/** UI event boundary for a rendered row; the adapter never performs navigation or domain work. */
interface MessageRowEventSink {
    fun onMessageActionRequested(bubble: View, item: MessageItem) {}
    fun onReplyQuoteClick(originalMessageId: Long) {}
    fun onSelectionToggle(messageId: Long) {}
    fun senderInfo(senderId: Long): Pair<String?, String?>? = null
    fun onAttachmentAction(action: MessageAttachmentAction) {}
}

/**
 * Actions that need an Activity or a system-owned storage surface. Keeping these events outside
 * the adapter prevents row recycling from owning navigation, Toasts, FileProvider or MediaStore.
 */
sealed interface MessageAttachmentAction {
    data class OpenImage(
        val fileIds: List<String>,
        val previewUrls: List<String>,
        val clickedIndex: Int,
        val fileNames: List<String>,
        val sourceMessageIds: List<Long>,
    ) : MessageAttachmentAction

    data class OpenVideo(
        val fileId: String,
        val fileName: String,
        val cachedPath: String?,
    ) : MessageAttachmentAction

    data class OpenDocument(
        val fileId: String,
        val fileName: String,
        val cachedFile: File,
        val previewUrl: String,
    ) : MessageAttachmentAction

    data class Save(
        val fileName: String,
        val cachedFile: File,
    ) : MessageAttachmentAction

    data class ToastRes(val resId: Int, val formatArg: String? = null) : MessageAttachmentAction
}
