package com.barkfluff.client.search

import android.content.Context
import barkfluff.shared.Shared
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.CachedChatDisplay
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.gateway.ChatDirectoryGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.repository.SecretChatRepository
import dagger.hilt.android.qualifiers.ApplicationContext
import javax.inject.Inject

data class SearchChat(
    val chat: ChatSummary,
    val title: String = chat.title,
    val avatarFileId: String? = chat.picturePreviewFileId.ifBlank { chat.pictureFileId }.ifBlank { null },
    val otherUserId: Long = 0L,
)

data class SearchChatPage(val chats: List<SearchChat>, val totalCount: Int)

interface SearchChatsGateway {
    suspend fun cached(): List<SearchChat>
    suspend fun page(offset: Int, size: Int): Result<SearchChatPage>
    suspend fun save(chats: List<SearchChat>, totalCount: Int)
}

class GrpcSearchChatsGateway @Inject constructor(
    @ApplicationContext context: Context,
    private val chatDirectory: ChatDirectoryGateway,
    private val userProfiles: UserProfileGateway,
    private val cache: ChatCacheRepository,
    private val secretChats: SecretChatRepository,
) : SearchChatsGateway {
    private val globalParam = GlobalParam(context)
    private val scope = CacheScope.from(globalParam)
    private val userId = globalParam.userId
    private var displays = emptyMap<String, CachedChatDisplay>()

    override suspend fun cached(): List<SearchChat> {
        val cached = scope?.let { cache.readChatList(it) }
        displays = cached?.displays.orEmpty()
        val chats = cached?.chats.orEmpty().map { chat ->
            val display = displays[chat.id]
            SearchChat(chat, display?.title ?: chat.title, display?.avatarFileId ?: avatar(chat), peer(chat))
        }
        if (!globalParam.secretChatsEnabled) return chats
        return chats + secretChats.listChats().map { secret ->
            val profile = userProfiles.user(secret.peerUserId).getOrNull()
            val title = profile?.let { "${it.firstName} ${it.lastName}".trim().ifBlank { it.username } }.orEmpty()
            SearchChat(
                chat = ChatSummary(
                    id = secret.id, title = title, picture = "", isGroupChat = false,
                    lastMessage = null, memberIds = listOf(secret.peerUserId), countUnread = 0L,
                    firstUnreadMessageId = 0L, chatType = Shared.ChatType.CHAT_TYPE_SECRET,
                ),
                title = title,
                avatarFileId = profile?.profilePicturePreviewFileId?.ifBlank { profile.profilePictureFileId }?.ifBlank { null },
                otherUserId = secret.peerUserId,
            )
        }
    }

    override suspend fun page(offset: Int, size: Int): Result<SearchChatPage> =
        chatDirectory.chats(offset, size).map { page ->
            SearchChatPage(page.chats.map { chat ->
                val display = displays[chat.id]
                SearchChat(chat, chat.title.ifBlank { display?.title.orEmpty() }, avatar(chat), peer(chat))
            }, page.totalCount)
        }

    override suspend fun save(chats: List<SearchChat>, totalCount: Int) {
        val cacheScope = scope ?: return
        if (CacheScope.from(globalParam) != cacheScope) return
        cache.saveChatPage(cacheScope, chats.map { it.chat }, totalCount, replaceExisting = true)
    }

    private fun avatar(chat: ChatSummary): String? =
        chat.picturePreviewFileId.ifBlank { chat.pictureFileId }.ifBlank { null }

    private fun peer(chat: ChatSummary): Long = if (chat.isGroupChat) 0L else
        chat.memberIds.firstOrNull { it != userId } ?: chat.memberIds.firstOrNull() ?: displays[chat.id]?.otherUserId ?: 0L
}
