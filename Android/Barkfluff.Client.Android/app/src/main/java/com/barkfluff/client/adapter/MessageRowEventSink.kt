package com.barkfluff.client.adapter

import android.view.View

/** UI event boundary for a rendered row; the adapter never performs navigation or domain work. */
interface MessageRowEventSink {
    fun onMessageActionRequested(bubble: View, item: MessageItem) {}
    fun onReplyQuoteClick(originalMessageId: Long) {}
    fun onSelectionToggle(messageId: Long) {}
}
