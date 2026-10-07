package com.barkfluff.client.domain.model

import barkfluff.shared.Shared

enum class SearchAttachmentPresence { Any, With, Without }

data class MessageSearchAuthor(val userId: Long = 0L, val userUuid: String = "", val name: String = "") {
    val key: String get() = if (userId > 0L) "id:$userId" else "uuid:$userUuid"
}

data class MessageSearchCursor(val sentAtSeconds: Long, val sentAtNanos: Int, val messageId: Long)

data class MessageSearchQuery(
    val text: String = "",
    val author: MessageSearchAuthor? = null,
    val sentFromSeconds: Long? = null,
    val sentBeforeSeconds: Long? = null,
    val attachmentPresence: SearchAttachmentPresence = SearchAttachmentPresence.Any,
    val attachmentTypes: Set<Shared.MessageAttachmentType> = emptySet(),
    val cursor: MessageSearchCursor? = null,
    val pageSize: Int = 30,
)

data class MessageSearchHit(
    val messageId: Long,
    val chatId: String,
    val chatTitle: String,
    val isGroupChat: Boolean,
    val chatPictureFileId: String,
    val otherUserId: Long,
    val author: MessageSearchAuthor,
    val sentAtMillis: Long,
    val text: String,
    val attachmentTypes: Set<Shared.MessageAttachmentType>,
)

data class MessageSearchPage(val hits: List<MessageSearchHit>, val nextCursor: MessageSearchCursor?)

class MessageSearchUnavailableException : Exception()
