package com.barkfluff.client.perf

import android.os.Trace
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

object PerfTrace {
    const val APP_TO_CHATS = "BF:appToChats"
    const val APP_TO_CHATS_COOKIE = 0
    const val APPLICATION_ON_CREATE = "BF:Application.onCreate"
    const val CHAT_OPEN = "BF:chatOpen"
    const val CHAT_OPEN_COOKIE = 1
    const val ONLINE_PING = "BF:onlinePing"
    const val EXTRA_CHAT_OPEN = "com.barkfluff.client.perf.CHAT_OPEN"

    private val appToChatsReported = AtomicBoolean()
    private val onlinePingCookie = AtomicInteger()

    fun beginAppToChats() {
        Trace.beginAsyncSection(APP_TO_CHATS, APP_TO_CHATS_COOKIE)
    }

    fun markFirstChatDisplayed(): Boolean = appToChatsReported.compareAndSet(false, true)

    fun endAppToChats() {
        Trace.endAsyncSection(APP_TO_CHATS, APP_TO_CHATS_COOKIE)
    }

    fun beginChatOpen() {
        Trace.beginAsyncSection(CHAT_OPEN, CHAT_OPEN_COOKIE)
    }

    fun endChatOpen() {
        Trace.endAsyncSection(CHAT_OPEN, CHAT_OPEN_COOKIE)
    }

    fun beginOnlinePing(): Int {
        val cookie = onlinePingCookie.incrementAndGet()
        Trace.beginAsyncSection(ONLINE_PING, cookie)
        return cookie
    }

    fun endOnlinePing(cookie: Int) {
        Trace.endAsyncSection(ONLINE_PING, cookie)
    }
}
