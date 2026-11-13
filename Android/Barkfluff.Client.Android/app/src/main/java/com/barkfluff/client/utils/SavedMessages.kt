package com.barkfluff.client.utils

import android.view.View
import android.widget.ImageView
import coil.load
import com.barkfluff.client.R
import com.barkfluff.client.domain.model.ChatSummary
import barkfluff.shared.Shared

fun isSelfChat(memberIds: List<Long>, currentUserId: Long): Boolean =
    currentUserId > 0 && memberIds.isNotEmpty() && memberIds.all { it == currentUserId }

fun ChatSummary.isSavedMessages(currentUserId: Long): Boolean =
    !isGroupChat && chatType == Shared.ChatType.CHAT_TYPE_REGULAR && isSelfChat(memberIds, currentUserId)

fun showSavedMessagesAvatar(image: ImageView, placeholder: View) {
    image.tag = "saved_messages"
    placeholder.tag = "saved_messages"
    placeholder.visibility = View.GONE
    image.visibility = View.VISIBLE
    image.load(R.drawable.avatar_saved_messages)
}
