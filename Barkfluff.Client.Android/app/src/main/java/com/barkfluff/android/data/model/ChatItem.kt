package com.barkfluff.android.data.model

import java.time.Instant

data class ChatItem(
    val id: String,
    val title: String,
    val picture: String?,
    val isGroupChat: Boolean,
    val lastMessageText: String?,
    val lastMessageTime: Instant?,
    val unreadCount: Long,
)
