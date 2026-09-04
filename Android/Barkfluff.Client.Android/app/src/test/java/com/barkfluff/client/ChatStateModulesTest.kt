package com.barkfluff.client

import com.barkfluff.client.cache.ComposerAttachment
import com.barkfluff.client.domain.gateway.RealtimeGateway
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(kotlinx.coroutines.ExperimentalCoroutinesApi::class)
class ChatStateModulesTest {
    private val selection = SelectionReducer()
    private val composer = ChatComposer()

    @Test
    fun selectionToggleIsImmutableAndClosesWhenLastItemRemoved() {
        val initial = SelectionState()
        val selected = selection.toggle(initial, 42L)

        assertTrue(selected.isActive)
        assertEquals(setOf(42L), selected.selectedMessageIds)
        assertEquals(emptySet<Long>(), initial.selectedMessageIds)
        assertEquals(SelectionState(), selection.toggle(selected, 42L))
    }

    @Test
    fun missingMessagesAreDroppedFromRestoredSelection() {
        val state = SelectionState(setOf(1L, 2L, 3L), isActive = true)
        val restored = selection.removeMissing(state, setOf(2L, 4L))

        assertEquals(setOf(2L), restored.selectedMessageIds)
        assertTrue(restored.isActive)
        assertEquals(SelectionState(), selection.removeMissing(state, emptySet()))
    }

    @Test
    fun replyAndEditAreMutuallyExclusive() {
        val reply = PendingReply(1L, 2L, "Alice", "hello", emptyList())
        val edit = PendingEdit(3L, "edited", listOf("file"))

        val replying = composer.beginReply(ComposerState(), reply)
        assertEquals(reply, replying.pendingReply)
        assertFalse(replying.pendingEdit != null)

        val editing = composer.beginEdit(replying, edit)
        assertEquals(edit, editing.pendingEdit)
        assertEquals("edited", editing.text)
        assertFalse(editing.pendingReply != null)
    }

    @Test
    fun stagedAttachmentKeepsKindAlignedWithPath() {
        val initial = ComposerState()
        val staged = composer.stagedAttachment(initial, "/private/image.jpg", "RAW_IMAGE")
            .let { composer.stagedAttachment(it, "/private/file.pdf", "DOCUMENT") }

        assertEquals(
            listOf("/private/image.jpg", "/private/file.pdf"),
            staged.attachmentPaths,
        )
        assertEquals(listOf("RAW_IMAGE", "DOCUMENT"), staged.attachmentKinds)

        val removed = composer.removeAttachment(staged, "/private/image.jpg")
        assertEquals(listOf("/private/file.pdf"), removed.attachmentPaths)
        assertEquals(listOf("DOCUMENT"), removed.attachmentKinds)
    }

    @Test
    fun stagedAttachmentBackfillsKindsFromOlderState() {
        val olderState = ComposerState(attachmentPaths = listOf("/private/old.pdf"))
        val staged = composer.stagedAttachment(olderState, "/private/new.jpg", "RAW_IMAGE")

        assertEquals(listOf("DOCUMENT", "RAW_IMAGE"), staged.attachmentKinds)
    }

    @Test
    fun durableEnqueueKeepsNewerPreviewRecords() {
        val state = ComposerState(
            text = "sent",
            attachmentPaths = listOf("/private/old.jpg", "/private/new.pdf"),
            attachmentKinds = listOf("RAW_IMAGE", "DOCUMENT"),
        )
        val remaining = listOf(
            ComposerAttachment(
                attachmentIndex = 0,
                path = "/private/new.pdf",
                kind = "DOCUMENT",
                fileName = "new.pdf",
                mimeType = "application/pdf",
                generation = 2L,
                createdAtMillis = 2L,
            )
        )

        val next = composer.clearAfterDurableEnqueue(state, generation = 1L, remaining = remaining)

        assertEquals(listOf("/private/new.pdf"), next.attachmentPaths)
        assertEquals(listOf("DOCUMENT"), next.attachmentKinds)
        assertEquals(2L, next.draftGeneration)
    }

    @Test
    fun presenceIgnoresOwnUserAndOtherChatAndExpiresTyping() {
        val presence = ChatPresence(currentUserId = 10L)
        val initial = PresenceState()
        assertEquals(initial, presence.online(initial, 10L, true))

        val online = presence.online(initial, 11L, true)
        assertEquals(setOf(11L), online.onlineUserIds)
        val wrongChat = presence.typing(online, "chat-a", "chat-b", 11L, true, 100L)
        assertEquals(emptySet<Long>(), wrongChat.typingUserIds)

        val typing = presence.typing(online, "chat-a", "chat-a", 11L, true, 100L)
        assertEquals(setOf(11L), typing.typingUserIds)
        assertEquals(emptySet<Long>(), presence.expire(typing, 5_100L).typingUserIds)
    }

    @Test
    fun presenceSessionKeepsOneHeartbeatAndClearsSubscriptions() = runTest {
        val realtime = RecordingRealtimeGateway()
        var now = 100L
        val session = ChatPresenceSession(
            realtime = realtime,
            scope = this,
            heartbeatIntervalMillis = 1_000L,
            idleTimeoutMillis = 1_500L,
            nowMillis = { now },
        )

        session.start("chat-a", listOf(10L, 11L, 11L))
        assertEquals(listOf(listOf("chat-a")), realtime.typingSubscriptions)
        assertEquals(listOf(listOf(10L, 11L)), realtime.onlineSubscriptions)

        session.textChanged("h")
        runCurrent()
        assertEquals(listOf("chat-a:true"), realtime.typingStatuses)

        session.textChanged("he")
        now = 1_000L
        advanceTimeBy(1_000L)
        runCurrent()
        assertEquals(listOf("chat-a:true", "chat-a:true"), realtime.typingStatuses)

        session.stopTyping(sendCancel = true)
        assertEquals("chat-a:false", realtime.typingStatuses.last())
        session.stop()
        assertEquals(listOf(emptyList<String>()), realtime.typingSubscriptions.takeLast(1))
        assertEquals(listOf(emptyList<Long>()), realtime.onlineSubscriptions.takeLast(1))
    }

    @Test
    fun presenceSessionDoesNotSendCancelBeforeTypingWasAnnounced() = runTest {
        val realtime = RecordingRealtimeGateway()
        val session = ChatPresenceSession(realtime, this, heartbeatIntervalMillis = 1_000L)

        session.start("chat-a", emptyList())
        session.stopTyping(sendCancel = true)

        assertTrue(realtime.typingStatuses.isEmpty())
    }

    private class RecordingRealtimeGateway : RealtimeGateway {
        private val newMessageFlow = MutableSharedFlow<barkfluff.updates.UpdatesApiOuterClass.NewMessageEvent>()
        private val messagesReadFlow = MutableSharedFlow<barkfluff.updates.UpdatesApiOuterClass.MessageReadEvent>()
        private val messageEditedFlow = MutableSharedFlow<barkfluff.updates.UpdatesApiOuterClass.MessageEditedEvent>()
        private val messageDeletedFlow = MutableSharedFlow<barkfluff.updates.UpdatesApiOuterClass.MessageDeletedEvent>()
        private val messagePinnedFlow = MutableSharedFlow<barkfluff.updates.UpdatesApiOuterClass.MessagePinnedEvent>()
        private val messageUnpinnedFlow = MutableSharedFlow<barkfluff.updates.UpdatesApiOuterClass.MessageUnpinnedEvent>()
        private val allMessagesUnpinnedFlow = MutableSharedFlow<barkfluff.updates.UpdatesApiOuterClass.AllMessagesUnpinnedEvent>()
        private val onlineStatusesFlow = MutableSharedFlow<barkfluff.onliner.OnlinerApiOuterClass.UserOnlineStatus>()
        private val typingEventsFlow = MutableSharedFlow<barkfluff.onliner.OnlinerApiOuterClass.TypingEvent>()

        val typingSubscriptions = mutableListOf<List<String>>()
        val onlineSubscriptions = mutableListOf<List<Long>>()
        val typingStatuses = mutableListOf<String>()

        override val newMessages: Flow<barkfluff.updates.UpdatesApiOuterClass.NewMessageEvent> = newMessageFlow
        override val messagesRead: Flow<barkfluff.updates.UpdatesApiOuterClass.MessageReadEvent> = messagesReadFlow
        override val messageEdited: Flow<barkfluff.updates.UpdatesApiOuterClass.MessageEditedEvent> = messageEditedFlow
        override val messageDeleted: Flow<barkfluff.updates.UpdatesApiOuterClass.MessageDeletedEvent> = messageDeletedFlow
        override val messagePinned: Flow<barkfluff.updates.UpdatesApiOuterClass.MessagePinnedEvent> = messagePinnedFlow
        override val messageUnpinned: Flow<barkfluff.updates.UpdatesApiOuterClass.MessageUnpinnedEvent> = messageUnpinnedFlow
        override val allMessagesUnpinned: Flow<barkfluff.updates.UpdatesApiOuterClass.AllMessagesUnpinnedEvent> = allMessagesUnpinnedFlow
        override val onlineStatuses: Flow<barkfluff.onliner.OnlinerApiOuterClass.UserOnlineStatus> = onlineStatusesFlow
        override val typingEvents: Flow<barkfluff.onliner.OnlinerApiOuterClass.TypingEvent> = typingEventsFlow

        override fun resume() = Unit
        override fun pause() = Unit
        override fun shutdown() = Unit
        override fun changeOnlineSubscription(userIds: List<Long>) { onlineSubscriptions += userIds }
        override fun changeTypingSubscription(chatIds: List<String>) { typingSubscriptions += chatIds }
        override fun sendTypingStatus(chatId: String, typing: Boolean) {
            typingStatuses += "$chatId:$typing"
        }
    }
}
