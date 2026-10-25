package com.barkfluff.client.cache

import com.barkfluff.client.domain.model.ChatSummary
import org.junit.Assert.assertEquals
import org.junit.Test

class ChatCacheDisplayTest {

    @Test
    fun `minimal mirrored row keeps existing display and member data`() {
        val cached = chat(
            title = "Алиса",
            picture = "https://files/avatar",
            pictureFileId = "avatar-file",
            picturePreviewFileId = "avatar-preview",
            memberIds = listOf(1L, 2L),
        )
        val mirrored = chat(title = "", picture = "", memberIds = emptyList())

        val merged = mergeCachedChatDisplay(cached, mirrored)

        assertEquals("Алиса", merged.title)
        assertEquals("https://files/avatar", merged.picture)
        assertEquals("avatar-file", merged.pictureFileId)
        assertEquals("avatar-preview", merged.picturePreviewFileId)
        assertEquals(listOf(1L, 2L), merged.memberIds)
    }

    @Test
    fun `new non-empty display fields replace cached values`() {
        val cached = chat(title = "Старое имя", picture = "old", memberIds = listOf(1L, 2L))
        val remote = chat(title = "Новое имя", picture = "new", memberIds = listOf(1L, 3L))

        val merged = mergeCachedChatDisplay(cached, remote)

        assertEquals("Новое имя", merged.title)
        assertEquals("new", merged.picture)
        assertEquals(listOf(1L, 3L), merged.memberIds)
    }

    private fun chat(
        title: String,
        picture: String,
        pictureFileId: String = "",
        picturePreviewFileId: String = "",
        memberIds: List<Long>,
    ) = ChatSummary(
        id = "chat-1",
        title = title,
        picture = picture,
        pictureFileId = pictureFileId,
        picturePreviewFileId = picturePreviewFileId,
        isGroupChat = false,
        lastMessage = null,
        memberIds = memberIds,
        countUnread = 0L,
        firstUnreadMessageId = 0L,
    )
}
