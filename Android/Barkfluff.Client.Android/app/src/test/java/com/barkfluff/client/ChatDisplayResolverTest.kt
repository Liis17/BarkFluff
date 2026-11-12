package com.barkfluff.client

import com.barkfluff.client.cache.CachedChatDisplay
import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.domain.model.UserProfile
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class ChatDisplayResolverTest {

    @Test
    fun `cached display hydrates a direct chat without loading the user`() = runTest {
        var userCalls = 0
        val resolver = ChatDisplayResolver(
            loadUser = { userCalls++; Result.success(profile()) },
            saveDisplay = { _, _ -> error("Cached display should not be rewritten") },
        )
        resolver.loadCachedDisplays(mapOf(CHAT_ID to CachedChatDisplay("Алиса", "avatar-2", 2L)), 1L)

        val item = resolver.resolveCachedChats(listOf(chat()), 1L, "Чат").single()

        assertEquals("Алиса", item.displayTitle)
        assertEquals("avatar-2", item.displayAvatarFileId)
        assertEquals(2L, item.otherUserId)
        assertEquals(0, userCalls)
    }

    @Test
    fun `ListChats display is cached without loading the user`() = runTest {
        var userCalls = 0
        val saved = linkedMapOf<String, CachedChatDisplay>()
        val resolver = ChatDisplayResolver(
            loadUser = { userCalls++; Result.success(profile()) },
            saveDisplay = { chatId, display -> saved[chatId] = display },
        )

        resolver.cacheChatList(listOf(chat(title = "Серверная Алиса", avatar = "server-avatar")), 1L)
        val fromList = resolver.resolveCachedChats(listOf(chat()), 1L, "Чат").single()
        assertEquals("Серверная Алиса", fromList.displayTitle)
        assertEquals("server-avatar", fromList.displayAvatarFileId)
        assertEquals(0, userCalls)
        assertEquals(CachedChatDisplay("Серверная Алиса", "server-avatar", 2L), saved[CHAT_ID])
    }

    @Test
    fun `resolved profile is saved and reused on the next display refresh`() = runTest {
        var userCalls = 0
        val saved = linkedMapOf<String, CachedChatDisplay>()
        val resolver = ChatDisplayResolver(
            loadUser = { userCalls++; Result.success(profile()) },
            saveDisplay = { chatId, display -> saved[chatId] = display },
        )

        resolver.resolveMissingProfiles(listOf(chat()), 1L)
        val firstResolution = resolver.resolveCachedChats(listOf(chat()), 1L, "Чат").single()
        resolver.resolveMissingProfiles(listOf(chat()), 1L)
        val repeatedResolution = resolver.resolveCachedChats(listOf(chat()), 1L, "Чат").single()

        assertEquals("Алиса Тест", firstResolution.displayTitle)
        assertEquals("preview-2", firstResolution.displayAvatarFileId)
        assertEquals("Алиса Тест", repeatedResolution.displayTitle)
        assertEquals(1, userCalls)
        assertEquals(CachedChatDisplay("Алиса Тест", "preview-2", 2L), saved[CHAT_ID])
    }

    @Test
    fun `direct chat never treats the current account as its peer`() = runTest {
        var requestedUserId: Long? = null
        val resolver = ChatDisplayResolver(
            loadUser = { requestedUserId = it; Result.success(profile()) },
            saveDisplay = { _, _ -> },
        )

        resolver.resolveMissingProfiles(listOf(chat(memberIds = listOf(1L))), 1L)
        val item = resolver.resolveCachedChats(listOf(chat(memberIds = listOf(1L))), 1L, "Чат").single()

        assertNull(requestedUserId)
        assertEquals(0L, item.otherUserId)
        assertNull(resolveOtherUserId(listOf(1L, 2L), 0L))
    }

    @Test
    fun `known name without avatar still resolves peer profile`() = runTest {
        var userCalls = 0
        val resolver = ChatDisplayResolver(
            loadUser = { userCalls++; Result.success(profile()) },
            saveDisplay = { _, _ -> },
        )
        val chat = chat(title = "Имя из списка")

        resolver.resolveMissingProfiles(listOf(chat), 1L)
        val item = resolver.resolveCachedChats(listOf(chat), 1L, "Чат").single()

        assertEquals("Имя из списка", item.displayTitle)
        assertEquals("preview-2", item.displayAvatarFileId)
        assertEquals(1, userCalls)
    }

    @Test
    fun `successful profile without avatar is negatively cached for the session`() = runTest {
        var userCalls = 0
        val resolver = ChatDisplayResolver(
            loadUser = {
                userCalls++
                Result.success(profile().copy(profilePicturePreviewFileId = "", profilePictureFileId = ""))
            },
            saveDisplay = { _, _ -> },
        )
        val chat = chat(title = "Имя из списка")

        repeat(3) { resolver.resolveMissingProfiles(listOf(chat), 1L) }

        assertEquals(1, userCalls)
    }

    @Test
    fun `failed profile request retries after the next list refresh`() = runTest {
        var userCalls = 0
        val resolver = ChatDisplayResolver(
            loadUser = {
                userCalls++
                if (userCalls == 1) Result.failure(IllegalStateException("offline")) else Result.success(profile())
            },
            saveDisplay = { _, _ -> },
        )
        val chat = chat(title = "Имя из списка")

        resolver.resolveMissingProfiles(listOf(chat), 1L)
        resolver.resolveMissingProfiles(listOf(chat), 1L)
        assertEquals(1, userCalls)

        resolver.beginChatListRefresh()
        resolver.resolveMissingProfiles(listOf(chat), 1L)

        assertEquals(2, userCalls)
        assertEquals("preview-2", resolver.resolveCachedChats(listOf(chat), 1L, "Чат").single().displayAvatarFileId)
    }

    private fun chat(
        title: String = "",
        avatar: String = "",
        memberIds: List<Long> = listOf(1L, 2L),
    ) = ChatSummary(
        id = CHAT_ID,
        title = title,
        picture = "",
        pictureFileId = avatar,
        picturePreviewFileId = avatar,
        isGroupChat = false,
        lastMessage = null,
        memberIds = memberIds,
        countUnread = 0L,
        firstUnreadMessageId = 0L,
    )

    private fun profile() = UserProfile(
        userId = 2L,
        username = "alice",
        firstName = "Алиса",
        lastName = "Тест",
        bio = "",
        profilePictureUrl = "",
        profilePicturePreviewUrl = "",
        profilePictureFileId = "full-2",
        profilePicturePreviewFileId = "preview-2",
        registrationDate = 0L,
    )

    companion object {
        private const val CHAT_ID = "chat-1"
    }
}
