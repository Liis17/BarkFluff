package com.barkfluff.client.notifications

import android.Manifest
import android.app.Notification
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.database.sqlite.SQLiteException
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.drawable.BitmapDrawable
import android.os.Build
import android.os.Bundle
import android.os.SystemClock
import android.view.KeyEvent
import android.view.View
import androidx.core.app.RemoteInput
import androidx.core.app.Person
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry
import androidx.test.runner.lifecycle.Stage
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.MainActivity
import com.barkfluff.client.R
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.ChatActivity
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.domain.gateway.ChatDirectoryGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.model.ChatSummary
import com.barkfluff.client.domain.model.UserProfile
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.cache.OutgoingMessageRecord
import com.barkfluff.client.cache.OutgoingMessageState
import barkfluff.shared.Shared
import barkfluff.updates.UpdatesApiOuterClass
import com.google.protobuf.Timestamp
import java.io.IOException
import java.lang.reflect.Proxy
import java.util.concurrent.ConcurrentHashMap
import java.util.UUID
import java.util.concurrent.atomic.AtomicInteger
import io.grpc.Metadata
import io.grpc.Status
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.awaitCancellation
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.withTimeoutOrNull
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MessageNotificationTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val application get() = context.applicationContext as BarkFluffApplication
    private val manager get() = context.getSystemService(NotificationManager::class.java)
    private lateinit var global: GlobalParam
    private lateinit var previousBeacon: String
    private var previousUserId = 0L
    private var previousEnabled = true
    private var previousSecretChatsEnabled = false

    @Before
    fun setUp() {
        global = GlobalParam(context)
        previousBeacon = global.socketBeacon
        previousUserId = global.userId
        previousEnabled = global.notificationsEnabled
        previousSecretChatsEnabled = global.secretChatsEnabled
        global.socketBeacon = "notification-test-${UUID.randomUUID()}"
        global.userId = Long.MAX_VALUE - 9
        global.notificationsEnabled = true
        global.secretChatsEnabled = true
        OpenChatManager.closeChat()
        if (Build.VERSION.SDK_INT >= 33) {
            instrumentation.uiAutomation.grantRuntimePermission(context.packageName, Manifest.permission.POST_NOTIFICATIONS)
        }
        manager.cancelAll()
        NotificationHelper.createChannels(context)
    }

    @After
    fun tearDown() = runBlocking {
        application.outgoingMessageQueue.cancelAllForCurrentScope()
        manager.cancelAll()
        global.socketBeacon = previousBeacon
        global.userId = previousUserId
        global.notificationsEnabled = previousEnabled
        global.secretChatsEnabled = previousSecretChatsEnabled
    }

    @Test
    fun replyPendingIntentKeepsOriginalMessageAndIsMutableWhileReadIsImmutable() = runBlocking {
        showRegular(41)
        await { regular() != null }
        val first = regular()!!
        val reply = first.actions.first { it.semanticAction == Notification.Action.SEMANTIC_ACTION_REPLY }
        assertFalse(reply.actionIntent.isImmutable)
        assertEquals(NotificationHelper.REPLY_INPUT_KEY, reply.remoteInputs.single().resultKey)
        assertTrue(first.actions.last().actionIntent.isImmutable)
        assertEquals(scope().id, first.extras.getString(NotificationHelper.EXTRA_SCOPE_ID))
        postRegular(42)
        NotificationHelper.dismissForChat(context, CHAT, 41)
        await { regular()?.extras?.getLong(NotificationHelper.EXTRA_MESSAGE_ID) == 42L }
        assertNotEquals(reply.actionIntent, regular()!!.actions.first().actionIntent)
        NotificationHelper.activeNotificationChats.clear()
        NotificationHelper.dismissForChat(context, CHAT, 41)
        assertEquals(42L, regular()!!.extras.getLong(NotificationHelper.EXTRA_MESSAGE_ID))
        NotificationHelper.dismissForChat(context, CHAT, 42)
        await { regular() == null }
    }

    @Test
    fun replyThroughSystemPendingIntentPersistsBothJournalsWithoutActivity() = runBlocking {
        showRegular(41)
        await { regular() != null }
        val reply = regular()!!.actions.first()
        val fillIn = Intent()
        android.app.RemoteInput.addResultsToIntent(reply.remoteInputs, fillIn,
            Bundle().apply { putCharSequence(NotificationHelper.REPLY_INPUT_KEY, "offline reply") })
        AndroidNotificationPostBudget.awaitNextSlot()
        reply.actionIntent.send(context, 0, fillIn)
        await { application.chatCacheRepository.outgoingOperationIds(scope()).isNotEmpty() }
        val operationId = application.chatCacheRepository.outgoingOperationIds(scope()).single()
        val record = application.chatCacheRepository.outgoing(scope(), operationId)!!
        assertEquals("offline reply", record.text)
        assertEquals(0L, record.replyToMessageId)
        assertNull(record.draftGeneration)
        assertEquals(41L, application.chatCacheRepository
            .readyPendingChatReadRanges(scope(), Long.MAX_VALUE).single().upToMessageId)
        await {
            val style = regular()?.let(androidx.core.app.NotificationCompat.MessagingStyle::extractMessagingStyleFromNotification)
            style?.messages?.any { it.extras.getString(NotificationHelper.EXTRA_REPLY_BODY) == "offline reply" } == true
        }
        val retained = regular()!!
        val ownReply = androidx.core.app.NotificationCompat.MessagingStyle
            .extractMessagingStyleFromNotification(retained)!!.messages
            .single { it.extras.getString(NotificationHelper.EXTRA_REPLY_BODY) == "offline reply" }
        assertTrue(ownReply.extras.getString(NotificationHelper.EXTRA_REPLY_STATUS) in setOf("queued", "sent", "failed"))
        assertTrue(retained.actions.any { it.remoteInputs?.any { remote -> remote.resultKey == NotificationHelper.REPLY_INPUT_KEY } == true })
        instrumentation.runOnMainSync {
            assertTrue(ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(Stage.RESUMED).isEmpty())
        }
    }

    @Test
    fun blankWrongAccountAndEncryptedRepliesNeverReachTheQueue() = runBlocking {
        var enqueued = 0
        val receiver = NotificationActionReceiver()
        receiver.acceptReply(context, replyIntent(" \n ")) { enqueued++; emptyList() }
        val stale = replyIntent("reply")
        global.userId--
        receiver.acceptReply(context, stale) { enqueued++; emptyList() }
        global.userId++
        val encrypted = MessageNotificationTarget(scope().id, NotificationHelper.KIND_PRIVATE, CHAT, "41")
        receiver.acceptReply(context, withReply(NotificationHelper.actionIntent(context, encrypted, NotificationHelper.ACTION_REPLY), "reply")) {
            enqueued++
            emptyList()
        }
        assertEquals(0, enqueued)
        assertTrue(application.chatCacheRepository.outgoingOperationIds(scope()).isEmpty())
        assertTrue(application.chatCacheRepository.readyPendingReads(scope(), Long.MAX_VALUE).isEmpty())
    }

    @Test
    fun storageFailureLeavesNotificationAndShowsLocalizedError() = runBlocking {
        showRegular(41)
        await { regular() != null }
        AndroidNotificationPostBudget.awaitNextSlot()
        NotificationActionReceiver().acceptReply(context, replyIntent("reply")) { throw SQLiteException("disk full") }
        await { regular()?.extras?.getCharSequence(Notification.EXTRA_SUB_TEXT) != null }
        assertEquals(context.getString(R.string.notification_reply_failed),
            regular()!!.extras.getCharSequence(Notification.EXTRA_SUB_TEXT).toString())
        assertTrue(application.chatCacheRepository.outgoingOperationIds(scope()).isEmpty())
        assertTrue(application.chatCacheRepository.readyPendingReads(scope(), Long.MAX_VALUE).isEmpty())
    }

    @Test
    fun acceptingOldReplyPreservesNewNotification() = runBlocking {
        showRegular(41)
        await { regular() != null }
        AndroidNotificationPostBudget.awaitNextSlot()
        NotificationActionReceiver().acceptReply(context, replyIntent("reply")) {
            showRegular(42)
            await { regular()?.extras?.getLong(NotificationHelper.EXTRA_MESSAGE_ID) == 42L }
            emptyList()
        }
        assertEquals(42L, regular()!!.extras.getLong(NotificationHelper.EXTRA_MESSAGE_ID))
    }

    @Test
    fun replyKeepsActionCreationCutoffWhenNewIncomingMessageArrivesBeforeAcceptance() = runBlocking {
        showRegular(41)
        await { regular() != null }
        val action = replyIntent("reply")
        showRegular(42)
        await { regular()?.extras?.getLong(NotificationHelper.EXTRA_RECEIVED_CUTOFF) == 42L }

        AndroidNotificationPostBudget.awaitNextSlot()
        NotificationActionReceiver().acceptReply(context, action)

        val range = application.chatCacheRepository.readyPendingChatReadRanges(scope(), Long.MAX_VALUE).single()
        assertEquals(41L, range.upToMessageId)
        val notification = regular()!!
        assertEquals(42L, notification.extras.getLong(NotificationHelper.EXTRA_RECEIVED_CUTOFF))
        val messages = androidx.core.app.NotificationCompat.MessagingStyle
            .extractMessagingStyleFromNotification(notification)!!.messages
        assertTrue(messages.any { it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID) == 42L })
        assertTrue(messages.any { it.extras.getString(NotificationHelper.EXTRA_REPLY_BODY) == "reply" })
    }

    @Test
    fun markReadPersistsCutoffBeforeRemovingOnlyOlderEntries() = runBlocking {
        showRegular(41)
        awaitFor("initial regular card") { regular() != null }
        val action = readIntent(41)
        postWithNotificationBudget {
            NotificationHelper.addNotificationReply(
                context,
                MessageNotificationTarget(scope().id, NotificationHelper.KIND_REGULAR, CHAT, "41"),
                "reply-operation",
                "reply",
                "queued",
            )
        }
        awaitFor("queued reply update is acknowledged") {
            NotificationHelper.messagingHistoryFromNotification(regular() ?: return@awaitFor false)
                .any { it.extras.getString(NotificationHelper.EXTRA_OPERATION_ID) == "reply-operation" }
        }
        showRegular(42)
        awaitFor("newer incoming message") {
            regular()?.extras?.getLong(NotificationHelper.EXTRA_RECEIVED_CUTOFF) == 42L
        }

        AndroidNotificationPostBudget.awaitNextSlot()
        MarkAsReadReceiver().acceptMarkAsRead(context, action) { target ->
            application.chatCacheRepository.enqueueChatReadRange(
                scope(), target.chatId, target.eventId.toLong(),
            )
        }

        var remaining: List<androidx.core.app.NotificationCompat.MessagingStyle.Message>? = null
        var stableSnapshots = 0
        awaitFor("read cutoff removes only older history") {
            val current = regular() ?: return@awaitFor false
            val history = NotificationHelper.messagingHistoryFromNotification(current)
            val filtered = history.any { it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID) == 42L } &&
                history.any { it.extras.getString(NotificationHelper.EXTRA_REPLY_BODY) == "reply" } &&
                history.none { it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID) == 41L }
            if (filtered) {
                remaining = history
                stableSnapshots++
            } else {
                stableSnapshots = 0
            }
            stableSnapshots >= 2
        }
        assertEquals(41L, application.chatCacheRepository
            .readyPendingChatReadRanges(scope(), Long.MAX_VALUE).single().upToMessageId)
        val remainingHistory = requireNotNull(remaining)
        assertTrue(remainingHistory.any { it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID) == 42L })
        assertTrue(remainingHistory.any { it.extras.getString(NotificationHelper.EXTRA_REPLY_BODY) == "reply" })
        assertFalse(remainingHistory.any { it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID) == 41L })
    }

    @Test
    fun markReadPersistenceFailureRetainsCardAndShowsLocalizedError() = runBlocking {
        showRegular(41)
        await { regular() != null }

        AndroidNotificationPostBudget.awaitNextSlot()
        MarkAsReadReceiver().acceptMarkAsRead(context, readIntent(41)) {
            throw SQLiteException("disk full")
        }

        await { regular()?.extras?.getCharSequence(Notification.EXTRA_SUB_TEXT) != null }
        assertEquals(41L, regular()!!.extras.getLong(NotificationHelper.EXTRA_MESSAGE_ID))
        assertEquals(context.getString(R.string.notification_mark_read_failed),
            regular()!!.extras.getCharSequence(Notification.EXTRA_SUB_TEXT).toString())
        assertTrue(application.chatCacheRepository
            .readyPendingChatReadRanges(scope(), Long.MAX_VALUE).isEmpty())
    }

    @Test
    fun scopedCardsMergeLateMessagesAndKeepCollidingChatIdsSeparate() = runBlocking {
        val firstChat = "Aa"
        val collidingChat = "BB"
        assertEquals(firstChat.hashCode(), collidingChat.hashCode())
        val scopeId = scope().id
        fun input(chatId: String, id: Long, timestamp: Long, body: String) = RegularNotificationInput(
            scopeId = scopeId,
            chatId = chatId,
            messageId = id,
            senderId = id,
            senderName = "Sender $id",
            chatTitle = "Room $chatId",
            isGroupChat = true,
            timestampMillis = timestamp,
            text = body,
        )

        postRegular(input(firstChat, 41, 1_000, "one"))
        awaitFor("first scoped message") { historyIds(scopeId, firstChat) == listOf(41L) }
        postRegular(input(firstChat, 43, 3_000, "three"))
        awaitFor("newer scoped message") { historyIds(scopeId, firstChat) == listOf(41L, 43L) }
        postRegular(input(firstChat, 42, 2_000, "late"))
        awaitFor("late scoped message merge") { historyIds(scopeId, firstChat) == listOf(41L, 42L, 43L) }
        postRegular(input(firstChat, 41, 1_000, "duplicate"))
        postRegular(input(collidingChat, 41, 1_000, "other"))
        awaitFor("colliding scoped chat") { historyIds(scopeId, collidingChat) == listOf(41L) }

        await {
            val first = manager.activeNotifications.firstOrNull { it.tag == "regular:$scopeId:$firstChat" }?.notification
                ?: return@await false
            val second = manager.activeNotifications.firstOrNull { it.tag == "regular:$scopeId:$collidingChat" }?.notification
                ?: return@await false
            NotificationHelper.messagingHistoryFromNotification(first).map {
                it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID)
            } == listOf(41L, 42L, 43L) &&
                NotificationHelper.messagingHistoryFromNotification(second).singleOrNull()?.text?.toString() == "other"
        }
        val firstActive = manager.activeNotifications.single { it.tag == "regular:$scopeId:$firstChat" }
        val secondActive = manager.activeNotifications.single { it.tag == "regular:$scopeId:$collidingChat" }
        val first = firstActive.notification
        val second = secondActive.notification
        val style = requireNotNull(androidx.core.app.NotificationCompat.MessagingStyle
            .extractMessagingStyleFromNotification(first))
        val history = NotificationHelper.messagingHistoryFromNotification(first)
        assertTrue(first.extras.containsKey(NotificationHelper.EXTRA_MESSAGING_HISTORY))
        assertEquals(listOf(41L, 42L, 43L), history.map {
            it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID)
        })
        assertEquals("Room $firstChat", style.conversationTitle)
        assertEquals("Sender 42", history[1].person?.name)
        assertEquals(43L, first.extras.getLong(NotificationHelper.EXTRA_RECEIVED_CUTOFF))
        assertEquals("other", NotificationHelper.messagingHistoryFromNotification(second).single().text.toString())
        assertNotEquals(firstActive.tag, secondActive.tag)
        assertNotEquals(first.contentIntent, second.contentIntent)
    }

    @Test
    fun dismissingChatDoesNotCancelLegacyHashCollisionOrScopedCall() = runBlocking {
        val chatId = "Aa"
        val collidingId = "BB"
        assertEquals(chatId.hashCode(), collidingId.hashCode())
        val scopeId = scope().id
        val legacyOtherChat = androidx.core.app.NotificationCompat.Builder(context, NotificationHelper.CHANNEL_CHAT_MESSAGES)
            .setSmallIcon(R.drawable.ic_notification)
            .addExtras(Bundle().apply {
                putString(NotificationHelper.EXTRA_SCOPE_ID, scopeId)
                putString(NotificationHelper.EXTRA_KIND, NotificationHelper.KIND_REGULAR)
                putString(NotificationHelper.EXTRA_CHAT_ID, collidingId)
            })
            .build()
        val scopedCall = androidx.core.app.NotificationCompat.Builder(context, NotificationHelper.CHANNEL_CHAT_MESSAGES)
            .setSmallIcon(R.drawable.ic_notification)
            .addExtras(Bundle().apply {
                putString(NotificationHelper.EXTRA_SCOPE_ID, scopeId)
                putString(NotificationHelper.EXTRA_KIND, "call")
                putString(NotificationHelper.EXTRA_CHAT_ID, collidingId)
            })
            .build()
        postWithNotificationBudget { manager.notify(chatId.hashCode(), legacyOtherChat) }
        postWithNotificationBudget { manager.notify("call:$scopeId:$collidingId", chatId.hashCode(), scopedCall) }
        awaitFor("both notifications sharing an integer id") {
            manager.activeNotifications.any { it.tag == null && it.id == chatId.hashCode() } &&
                manager.activeNotifications.any { it.tag == "call:$scopeId:$collidingId" }
        }

        NotificationHelper.dismissForChat(context, chatId)

        assertTrue(manager.activeNotifications.any { it.tag == null && it.id == chatId.hashCode() })
        assertTrue(manager.activeNotifications.any { it.tag == "call:$scopeId:$collidingId" })
    }

    @Test
    fun messagingStyleBoundsTwentyFiveSendersAndOwnReplyDoesNotAdvanceCutoff() = runBlocking {
        val scopeId = scope().id
        val chatId = "history-${UUID.randomUUID()}"
        val tag = "regular:$scopeId:$chatId"
        val seedMessages = (1L..25L).map { messageId ->
            val person = Person.Builder().setName("Sender $messageId").setKey(messageId.toString()).apply {
                if (messageId == 25L) {
                    setIcon(androidx.core.graphics.drawable.IconCompat.createWithBitmap(
                        Bitmap.createBitmap(48, 48, Bitmap.Config.ARGB_8888).apply { eraseColor(Color.BLUE) }
                    ))
                }
            }.build()
            androidx.core.app.NotificationCompat.MessagingStyle.Message(
                "message $messageId", messageId, person,
            ).apply {
                extras.putLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID, messageId)
                extras.putLong(NotificationHelper.EXTRA_SENDER_ID, messageId)
            }
        }
        val seedStyle = androidx.core.app.NotificationCompat.MessagingStyle(
            Person.Builder().setName(context.getString(R.string.notification_me)).build(),
        ).setGroupConversation(true).setConversationTitle("Group")
        seedMessages.forEach(seedStyle::addMessage)
        val replyIntent = NotificationHelper.actionIntent(context,
            MessageNotificationTarget(scopeId, NotificationHelper.KIND_REGULAR, chatId, "25"),
            NotificationHelper.ACTION_REPLY)
        val replyPendingIntent = PendingIntent.getBroadcast(context, tag.hashCode(), replyIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE)
        val replyAction = androidx.core.app.NotificationCompat.Action.Builder(
            0, context.getString(R.string.notification_reply), replyPendingIntent,
        ).addRemoteInput(androidx.core.app.RemoteInput.Builder(NotificationHelper.REPLY_INPUT_KEY)
            .setLabel(context.getString(R.string.notification_reply_hint)).build())
            .setSemanticAction(androidx.core.app.NotificationCompat.Action.SEMANTIC_ACTION_REPLY)
            .build()
        val seed = androidx.core.app.NotificationCompat.Builder(context, NotificationHelper.CHANNEL_CHAT_MESSAGES)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle("Group")
            .setStyle(seedStyle)
            .addAction(replyAction)
            .addExtras(Bundle().apply {
                putString(NotificationHelper.EXTRA_SCOPE_ID, scopeId)
                putString(NotificationHelper.EXTRA_KIND, NotificationHelper.KIND_REGULAR)
                putString(NotificationHelper.EXTRA_CHAT_ID, chatId)
                putString(NotificationHelper.EXTRA_THREAD_ID, chatId)
                putString(NotificationHelper.EXTRA_EVENT_ID, "25")
                putLong(NotificationHelper.EXTRA_MESSAGE_ID, 25)
                putLong(NotificationHelper.EXTRA_RECEIVED_CUTOFF, 25)
                putString(NotificationHelper.EXTRA_CHAT_TITLE, "Group")
                putBoolean(NotificationHelper.EXTRA_GROUP_CHAT, true)
                putBoolean(NotificationHelper.EXTRA_CHAT_TITLE_FALLBACK, false)
                putParcelableArrayList(NotificationHelper.EXTRA_MESSAGING_HISTORY,
                    NotificationHelper.serializedMessagingHistory(seedMessages))
            })
            .build()
        postWithNotificationBudget { manager.notify(tag, 0, seed) }
        await { manager.activeNotifications.any { it.tag == tag } }

        val rehydrated = manager.activeNotifications.single { it.tag == tag }.notification
        val rehydratedHistory = NotificationHelper.messagingHistoryFromNotification(rehydrated)
        assertEquals(25, rehydratedHistory.size)
        assertEquals((1L..25L).toList(), rehydratedHistory.map {
            it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID)
        })
        assertEquals("Sender 25", rehydratedHistory.last().person?.name)
        assertNotNull(rehydratedHistory.last().person?.icon)
        assertTrue(rehydratedHistory.dropLast(1).all { it.person?.icon == null })

        postWithNotificationBudget {
            NotificationHelper.updateNotificationReplyStatus(context, scopeId, chatId, "reply-op", "queued reply", "queued")
        }
        await {
            val current = manager.activeNotifications.firstOrNull { it.tag == tag }?.notification
                ?: return@await false
            NotificationHelper.messagingHistoryFromNotification(current).any {
                it.extras.getString(NotificationHelper.EXTRA_OPERATION_ID) == "reply-op"
            }
        }
        val updated = manager.activeNotifications.single { it.tag == "regular:$scopeId:$chatId" }.notification
        val updatedStyle = requireNotNull(androidx.core.app.NotificationCompat.MessagingStyle
            .extractMessagingStyleFromNotification(updated))
        val updatedHistory = NotificationHelper.messagingHistoryFromNotification(updated)
        val reply = updatedHistory.single { it.extras.getString(NotificationHelper.EXTRA_OPERATION_ID) == "reply-op" }
        assertEquals("queued", reply.extras.getString(NotificationHelper.EXTRA_REPLY_STATUS))
        assertEquals(25L, updated.extras.getLong(NotificationHelper.EXTRA_RECEIVED_CUTOFF))
        assertEquals(25, updatedHistory.size)
        assertTrue(updatedStyle.messages.isNotEmpty())
        assertTrue(updated.actions.any { it.remoteInputs?.any { remote -> remote.resultKey == NotificationHelper.REPLY_INPUT_KEY } == true })
    }

    @Test
    fun duplicateMessageSilentlyEnrichesSenderAvatarAndKeepsServerId() = runBlocking {
        val scopeId = scope().id
        val chatId = "avatar-${UUID.randomUUID()}"
        val input = RegularNotificationInput(
            scopeId = scopeId,
            chatId = chatId,
            messageId = 701,
            senderId = 7,
            senderName = "Sender",
            chatTitle = "Group",
            isGroupChat = true,
            timestampMillis = 7_000,
            text = "hello",
        )
        postRegular(input)
        await { manager.activeNotifications.any { it.tag == "regular:$scopeId:$chatId" } }
        val avatar = Bitmap.createBitmap(64, 64, Bitmap.Config.ARGB_8888).apply { eraseColor(Color.GREEN) }
        postRegular(input, senderAvatarBitmap = avatar)

        await {
            val active = manager.activeNotifications.firstOrNull { it.tag == "regular:$scopeId:$chatId" }?.notification
                ?: return@await false
            val message = NotificationHelper.messagingHistoryFromNotification(active).singleOrNull() ?: return@await false
            val icon = message.person?.icon?.loadDrawable(context) as? BitmapDrawable ?: return@await false
            icon.bitmap.getPixel(icon.bitmap.width / 2, icon.bitmap.height / 2) == Color.GREEN
        }

        val notification = manager.activeNotifications.single { it.tag == "regular:$scopeId:$chatId" }.notification
        val message = NotificationHelper.messagingHistoryFromNotification(notification).single()
        assertEquals(701L, message.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID))
        val icon = requireNotNull(message.person?.icon).loadDrawable(context) as BitmapDrawable
        assertEquals(Color.GREEN, icon.bitmap.getPixel(icon.bitmap.width / 2, icon.bitmap.height / 2))
    }

    @Test
    fun duplicateRealtimeMessageUpgradesFallbackNameAndShortPreviewWithOrWithoutAvatar() = runBlocking {
        val scopeId = scope().id
        listOf(false, true).forEachIndexed { index, withAvatar ->
            val chatId = "enrichment-$index-${UUID.randomUUID()}"
            val messageId = 801L + index
            val fallback = RegularNotificationInput(
                scopeId = scopeId,
                chatId = chatId,
                messageId = messageId,
                senderId = 7,
                senderName = "Unknown #7",
                chatTitle = "Unknown room",
                isGroupChat = false,
                timestampMillis = 8_000L + index,
                text = "short FCM preview",
                senderNameIsFallback = true,
                chatTitleIsFallback = true,
                contentIsComplete = false,
            )
            postRegular(fallback)
            await { manager.activeNotifications.any { it.tag == "regular:$scopeId:$chatId" } }

            val actual = fallback.copy(
                senderName = "Ada Sender",
                chatTitle = "Ada Sender",
                text = "Full **realtime** message body",
                senderNameIsFallback = false,
                chatTitleIsFallback = false,
                contentIsComplete = true,
            )
            val avatar = if (withAvatar) Bitmap.createBitmap(48, 48, Bitmap.Config.ARGB_8888)
                .apply { eraseColor(Color.RED) } else null
            postRegular(actual, senderAvatarBitmap = avatar)

            await {
                val active = manager.activeNotifications.firstOrNull { it.tag == "regular:$scopeId:$chatId" }?.notification
                    ?: return@await false
                val message = NotificationHelper.messagingHistoryFromNotification(active).singleOrNull()
                    ?: return@await false
                message.person?.name == "Ada Sender" && message.text.toString() == "Full realtime message body"
            }

            val notification = manager.activeNotifications.single { it.tag == "regular:$scopeId:$chatId" }.notification
            val messages = NotificationHelper.messagingHistoryFromNotification(notification)
            assertEquals(1, messages.size)
            assertEquals(messageId, messages.single().extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID))
            assertEquals("Ada Sender", messages.single().person?.name)
            assertEquals("Full realtime message body", messages.single().text.toString())
            assertEquals(messageId, notification.extras.getLong(NotificationHelper.EXTRA_RECEIVED_CUTOFF))
            val icon = requireNotNull(messages.single().person?.icon).loadDrawable(context) as BitmapDrawable
            if (withAvatar) {
                assertEquals(Color.RED, icon.bitmap.getPixel(icon.bitmap.width / 2, icon.bitmap.height / 2))
            } else {
                assertNotNull(icon.bitmap)
            }
        }
    }

    @Test
    fun avatarEnrichmentDoesNotRecreateDismissedCard() = runBlocking {
        val scopeId = scope().id
        val chatId = "dismissed-${UUID.randomUUID()}"
        val input = RegularNotificationInput(
            scopeId = scopeId,
            chatId = chatId,
            messageId = 702,
            senderId = 7,
            senderName = "Sender",
            chatTitle = "Chat",
            isGroupChat = false,
            timestampMillis = 7_020,
            text = "hello",
        )
        postRegular(input)
        val tag = "regular:$scopeId:$chatId"
        await { manager.activeNotifications.any { it.tag == tag } }
        val deleteIntent = requireNotNull(manager.activeNotifications.single { it.tag == tag }.notification.deleteIntent)
        manager.cancel(tag, 0)
        AndroidNotificationPostBudget.awaitNextSlot()
        deleteIntent.send()
        await { manager.activeNotifications.none { it.tag == tag } }

        postRegular(input,
            senderAvatarBitmap = Bitmap.createBitmap(48, 48, Bitmap.Config.ARGB_8888).apply { eraseColor(Color.BLUE) })

        assertNull(manager.activeNotifications.singleOrNull { it.tag == tag })
    }

    @Test
    fun swipedHistoryIsNotRestoredByTheNextMessageAndLateDismissalKeepsNewerMessage() = runBlocking {
        val scopeId = scope().id
        val chatId = "dismiss-history-${UUID.randomUUID()}"
        val tag = "regular:$scopeId:$chatId"
        fun input(id: Long) = RegularNotificationInput(
            scopeId = scopeId,
            chatId = chatId,
            messageId = id,
            senderId = 7,
            senderName = "Sender",
            chatTitle = "Chat",
            isGroupChat = false,
            timestampMillis = id * 1_000,
            text = "message $id",
        )

        postRegular(input(702))
        awaitFor("message 702 card") { historyIds(scopeId, chatId) == listOf(702L) }
        val swipedDelete = requireNotNull(manager.activeNotifications.single { it.tag == tag }.notification.deleteIntent)
        manager.cancel(tag, 0)
        AndroidNotificationPostBudget.awaitNextSlot()
        swipedDelete.send()
        awaitFor("swiped card removed") { manager.activeNotifications.none { it.tag == tag } }

        postRegular(input(703))
        awaitFor("new message after swipe") { historyIds(scopeId, chatId) == listOf(703L) }
        assertEquals(listOf(703L), historyIds(scopeId, chatId))

        val oldGenerationDelete = requireNotNull(manager.activeNotifications.single { it.tag == tag }.notification.deleteIntent)
        postRegular(input(704))
        AndroidNotificationPostBudget.awaitNextSlot()
        oldGenerationDelete.send()
        awaitFor("late dismissal removes only the old message") { historyIds(scopeId, chatId) == listOf(704L) }
        assertTrue(manager.activeNotifications.any { it.tag == tag })

        postWithNotificationBudget {
            NotificationHelper.addNotificationReply(
                context,
                MessageNotificationTarget(scopeId, NotificationHelper.KIND_REGULAR, chatId, "704"),
                "late-reply-operation",
                "queued reply",
                "queued",
            )
        }
        awaitFor("reply is in the card before the next incoming message") {
            NotificationHelper.messagingHistoryFromNotification(
                manager.activeNotifications.singleOrNull { it.tag == tag }?.notification ?: return@awaitFor false,
            ).any { it.extras.getString(NotificationHelper.EXTRA_OPERATION_ID) == "late-reply-operation" }
        }
        val delayedDismiss = requireNotNull(manager.activeNotifications.single { it.tag == tag }.notification.deleteIntent)
        postRegular(input(705))
        AndroidNotificationPostBudget.awaitNextSlot()
        NotificationHelper.dismissReadMessages(
            context, MessageNotificationTarget(scopeId, NotificationHelper.KIND_REGULAR, chatId, "705"),
        )
        awaitFor("read leaves only the older own reply") {
            NotificationHelper.messagingHistoryFromNotification(
                manager.activeNotifications.singleOrNull { it.tag == tag }?.notification ?: return@awaitFor false,
            ).singleOrNull()?.extras?.getString(NotificationHelper.EXTRA_OPERATION_ID) == "late-reply-operation"
        }
        AndroidNotificationPostBudget.awaitNextSlot()
        delayedDismiss.send()
        awaitFor("late deletion cancels an empty card") { manager.activeNotifications.none { it.tag == tag } }
        postRegular(input(706))
        awaitFor("next incoming message does not restore the removed reply") {
            historyIds(scopeId, chatId) == listOf(706L)
        }
    }

    @Test
    fun cancelPendingOutgoingRemovesQueuedReplyButKeepsReadRangeAndNewerIncoming() = runBlocking {
        val scope = scope()
        val operationId = UUID.randomUUID().toString()
        application.chatCacheRepository.saveOutgoing(scope, OutgoingMessageRecord(
            operationId = operationId,
            batchId = null,
            chatId = CHAT,
            chatTitle = "Notification chat",
            text = "queued reply",
            replyToMessageId = 0,
            draftGeneration = null,
            sendAsFile = false,
            existingFileIds = emptyList(),
            createdAtMillis = System.currentTimeMillis(),
            state = OutgoingMessageState.QUEUED,
            progress = 0,
            attemptCount = 0,
            nextAttemptAtMillis = 0,
            failureCategory = null,
            failureDetail = null,
            leaseOwner = null,
            leaseExpiresAtMillis = 0,
            serverMessageId = 0,
            serverMessagePayload = null,
            attachments = emptyList(),
            notificationReadMessageId = 702,
        ))
        showRegular(702)
        postWithNotificationBudget {
            NotificationHelper.addNotificationReply(
            context,
            MessageNotificationTarget(scope.id, NotificationHelper.KIND_REGULAR, CHAT, "702"),
            operationId,
            "queued reply",
            "queued",
            )
        }
        showRegular(703)

        AndroidNotificationPostBudget.awaitNextSlot()
        application.outgoingMessageQueue.cancelPendingOutgoing()

        awaitFor("cancelled reply removed from card") {
            val current = regular() ?: return@awaitFor false
            val history = NotificationHelper.messagingHistoryFromNotification(current)
            history.none { it.extras.getString(NotificationHelper.EXTRA_OPERATION_ID) == operationId } &&
                history.any { it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID) == 703L }
        }
        assertNull(application.chatCacheRepository.outgoing(scope, operationId))
        assertEquals(702L, application.chatCacheRepository
            .readyPendingChatReadRanges(scope, Long.MAX_VALUE).single().upToMessageId)
    }

    @Test
    fun imageNotificationUsesCaptionSummaryWithoutMediaPreview() = runBlocking {
        val scopeId = scope().id
        val chatId = "photo-${UUID.randomUUID()}"
        postRegular(RegularNotificationInput(
            scopeId = scopeId,
            chatId = chatId,
            messageId = 501,
            senderId = 5,
            senderName = "Photo sender",
            chatTitle = "One to one",
            isGroupChat = false,
            timestampMillis = 5_000,
            text = "**caption**",
            attachmentKind = "image",
        ))
        awaitFor("photo chat notification") { historyIds(scopeId, chatId) == listOf(501L) }
        val notification = manager.activeNotifications.single { it.tag == "regular:$scopeId:$chatId" }.notification
        val message = requireNotNull(androidx.core.app.NotificationCompat.MessagingStyle
            .extractMessagingStyleFromNotification(notification)).messages.single()
        assertEquals(context.getString(R.string.notification_photo_caption, "caption"), message.text.toString())
        assertNull(notification.extras.getParcelable(Notification.EXTRA_PICTURE))
        assertNotNull(notification.actions.first().remoteInputs.single())
    }

    @Test
    fun notificationArrivingAfterReplyAcknowledgementSurvivesPendingCancellation() = runBlocking {
        showRegular(41)
        await { regular() != null }
        val acceptance = launch(start = CoroutineStart.UNDISPATCHED) {
            AndroidNotificationPostBudget.awaitNextSlot()
            NotificationActionReceiver().acceptReply(context, replyIntent("reply")) { emptyList() }
        }
        showRegular(42)
        acceptance.join()
        await { regular()?.extras?.getLong(NotificationHelper.EXTRA_MESSAGE_ID) == 42L }
        delay(200)
        assertEquals(42L, regular()!!.extras.getLong(NotificationHelper.EXTRA_MESSAGE_ID))
    }

    @Test
    fun encryptedMarkersHaveOnlyHideAndDeduplicateAcrossFcmAndRealtime() = runBlocking {
        val effects = RealtimeSideEffectsImpl(context, unavailableGateway<UserProfileGateway>(),
            unavailableGateway<FileMediaGateway>(), application.secretChatRepository,
            application.chatRepository, application.chatCacheRepository)
        val data = mapOf("type" to "new_private_message", "private_chat_id" to CHAT,
            "event_id" to "41", "sender_user_id" to "7")
        AndroidNotificationPostBudget.awaitNextSlot()
        assertTrue(dispatchEncryptedMarker(data, effects))
        awaitFor("private encrypted marker") { encrypted(NotificationHelper.KIND_PRIVATE) != null }
        val first = encrypted(NotificationHelper.KIND_PRIVATE)!!
        val postTime = first.postTime
        effects.showPrivateMessageNotification(CHAT, 41, 7)
        delay(100)
        assertEquals(postTime, encrypted(NotificationHelper.KIND_PRIVATE)!!.postTime)
        AndroidNotificationPostBudget.awaitNextSlot()
        effects.showSecretMessageNotification(UUID.randomUUID().toString(), 7, UUID.randomUUID().toString())
        awaitFor("secret encrypted marker") { encrypted(NotificationHelper.KIND_SECRET) != null }
        showRegular(41)
        awaitFor("regular notification alongside encrypted markers") { regular() != null }
        assertEquals(3, manager.activeNotifications.count { it.notification.extras.getString(NotificationHelper.EXTRA_SCOPE_ID) == scope().id })
        for (kind in listOf(NotificationHelper.KIND_PRIVATE, NotificationHelper.KIND_SECRET)) {
            val notification = encrypted(kind)!!.notification
            assertEquals(context.getString(R.string.notification_encrypted_message), notification.extras.getCharSequence(Notification.EXTRA_TEXT).toString())
            assertEquals(1, notification.actions.size)
            val hide = notification.actions.single()
            assertEquals(context.getString(R.string.notification_hide), hide.title.toString())
            assertTrue(hide.actionIntent.isImmutable)
            assertTrue(hide.remoteInputs.isNullOrEmpty())
            assertFalse(hide.allowGeneratedReplies)
            assertFalse(notification.allowSystemGeneratedContextualActions)
            hide.actionIntent.send()
            awaitFor("hide $kind marker") { encrypted(kind) == null }
        }
        assertNotNull(regular())
    }

    @Test
    fun realtimeOrdinaryMessagePostsFallbackBeforeTransientChatLookupCompletes() = runBlocking {
        val lookupStarted = CompletableDeferred<Unit>()
        val lookupResult = CompletableDeferred<Result<ChatRepository.ChatInfo>>()
        val enrichmentCompleted = CompletableDeferred<Unit>()
        val profileStarted = CompletableDeferred<Unit>()
        val message = Shared.Message.newBuilder()
            .setId(901)
            .setSenderId(7)
            .setSentAt(Timestamp.newBuilder().setSeconds(System.currentTimeMillis() / 1_000).build())
            .setContent(Shared.MessageContent.newBuilder().setText("ordinary realtime body").build())
            .build()
        val event = UpdatesApiOuterClass.NewMessageEvent.newBuilder()
            .setChatId(CHAT)
            .setMessage(message)
            .build()
        val effects = RealtimeSideEffectsImpl(
            context,
            object : UserProfileGateway by unavailableGateway() {
                override suspend fun user(userId: Long): Result<UserProfile> {
                    AndroidNotificationPostBudget.awaitNextSlot()
                    profileStarted.complete(Unit)
                    return Result.success(UserProfile(
                        userId = userId,
                        username = "sender$userId",
                        firstName = "Sender",
                        lastName = userId.toString(),
                        bio = "",
                        profilePictureUrl = "",
                        profilePicturePreviewUrl = "",
                        profilePictureFileId = "realtime-avatar-$userId",
                        registrationDate = 0,
                    ))
                }
            },
            object : FileMediaGateway by unavailableGateway() {
                override suspend fun downloadUrl(fileId: String): Result<String> {
                    AndroidNotificationPostBudget.awaitNextSlot()
                    return Result.failure(IOException("avatar unavailable"))
                }
            },
            application.secretChatRepository,
            application.chatRepository,
            application.chatCacheRepository,
            chatInfoLookup = {
                lookupStarted.complete(Unit)
                lookupResult.await()
            },
        ).apply {
            notificationEnrichmentFinished = { _, _ -> enrichmentCompleted.complete(Unit) }
        }

        AndroidNotificationPostBudget.awaitNextSlot()
        effects.showMessageNotification(event)

        await { regular() != null }
        val notification = requireNotNull(regular())
        lookupResult.complete(Result.failure(IOException("offline")))
        await { lookupStarted.isCompleted }
        withTimeout(15_000) {
            profileStarted.await()
            enrichmentCompleted.await()
        }
        val messageEntry = requireNotNull(androidx.core.app.NotificationCompat.MessagingStyle
            .extractMessagingStyleFromNotification(notification)).messages.single()
        assertEquals("ordinary realtime body", messageEntry.text.toString())
        assertTrue(messageEntry.person?.name?.toString().orEmpty().contains("7"))
        assertEquals(NotificationHelper.KIND_REGULAR,
            notification.extras.getString(NotificationHelper.EXTRA_KIND))
    }

    @Test
    fun queuedRealtimeEnrichmentRechecksScopeBeforeStartingMetadataNetworkCall() = runBlocking {
        val originalUserId = global.userId
        val shiftedUserId = originalUserId - 1
        val requests = ConcurrentHashMap.newKeySet<Long>()
        val finishedChats = ConcurrentHashMap.newKeySet<String>()
        val profileGates = (601L..603L).associateWith { CompletableDeferred<Unit>() }
        val chatIds = (601L..603L).associateWith { "scope-recheck-$it-${UUID.randomUUID()}" }
        val profiles = object : UserProfileGateway by unavailableGateway() {
            override suspend fun user(userId: Long): Result<UserProfile> {
                requests += userId
                profileGates.getValue(userId).await()
                return Result.success(UserProfile(
                    userId = userId,
                    username = "sender$userId",
                    firstName = "Sender",
                    lastName = userId.toString(),
                    bio = "",
                    profilePictureUrl = "",
                    profilePicturePreviewUrl = "",
                    profilePictureFileId = "",
                    registrationDate = 0,
                ))
            }
        }
        val effects = RealtimeSideEffectsImpl(
            context,
            profiles,
            unavailableGateway<FileMediaGateway>(),
            application.secretChatRepository,
            application.chatRepository,
            application.chatCacheRepository,
            chatInfoLookup = { chatId ->
                Result.success(ChatRepository.ChatInfo(chatId, "Chat", "", false, 1, 0, 0, emptyList()))
            },
        ).apply {
            notificationEnrichmentFinished = { _, chatId -> finishedChats += chatId }
        }
        val postedChats = ConcurrentHashMap.newKeySet<String>()

        try {
            for ((senderId, chatId) in chatIds) {
                val message = Shared.Message.newBuilder()
                    .setId(senderId)
                    .setSenderId(senderId)
                    .setSentAt(Timestamp.newBuilder().setSeconds(System.currentTimeMillis() / 1_000).build())
                    .setContent(Shared.MessageContent.newBuilder().setText("scope check $senderId").build())
                    .build()
                AndroidNotificationPostBudget.awaitNextSlot()
                effects.showMessageNotification(UpdatesApiOuterClass.NewMessageEvent.newBuilder()
                    .setChatId(chatId).setMessage(message).build())
                postedChats += chatId
                awaitFor("initial scoped notification for sender $senderId") {
                    manager.activeNotifications.any { it.tag == "regular:${scope().id}:$chatId" }
                }
                if (senderId < 603L) {
                    awaitFor("metadata request for sender $senderId") { requests.contains(senderId) }
                }
            }

            assertFalse(requests.contains(603L))
            global.userId = shiftedUserId
            profileGates.getValue(601L).complete(Unit)
            profileGates.getValue(602L).complete(Unit)
            withTimeout(15_000) {
                while (!finishedChats.containsAll(chatIds.values)) delay(25)
            }
            assertEquals(setOf(601L, 602L), requests)
        } finally {
            global.userId = shiftedUserId
            profileGates.values.forEach { it.complete(Unit) }
            withTimeoutOrNull(15_000) {
                while (!finishedChats.containsAll(postedChats)) delay(25)
            }
            global.userId = originalUserId
        }
    }

    @Test
    fun realtimeEnrichmentQueuesThirdChatAndPublishesNamesBeforeAvatarTimeout() = runBlocking {
        val profileRequests = ConcurrentHashMap.newKeySet<Long>()
        val avatarRequests = ConcurrentHashMap<String, AtomicInteger>()
        val thirdAvatarStarted = CompletableDeferred<Unit>()
        val thirdAvatarResult = CompletableDeferred<Result<String>>()
        val thirdEnrichmentCompleted = CompletableDeferred<Unit>()
        val avatarIds = (501L..503L).associateWith { "avatar-${scope().id}-$it" }
        val profileGates = (501L..503L).associateWith { CompletableDeferred<Unit>() }
        val chatIds = (501L..503L).associateWith { "realtime-$it-${UUID.randomUUID()}" }
        val profiles = object : UserProfileGateway by unavailableGateway() {
            override suspend fun user(userId: Long): Result<UserProfile> {
                profileRequests += userId
                profileGates.getValue(userId).await()
                return Result.success(UserProfile(
                    userId = userId,
                    username = "sender$userId",
                    firstName = "Sender",
                    lastName = userId.toString(),
                    bio = "",
                    profilePictureUrl = "",
                    profilePicturePreviewUrl = "",
                    profilePictureFileId = avatarIds.getValue(userId),
                    registrationDate = 0,
                ))
            }
        }
        val media = object : FileMediaGateway by unavailableGateway() {
            override suspend fun downloadUrl(fileId: String): Result<String> {
                val count = avatarRequests.computeIfAbsent(fileId) { AtomicInteger() }.incrementAndGet()
                when (fileId) {
                    avatarIds.getValue(501L), avatarIds.getValue(502L) -> awaitCancellation()
                    avatarIds.getValue(503L) -> {
                        if (count == 1) thirdAvatarStarted.complete(Unit)
                        if (count == 2) AndroidNotificationPostBudget.awaitNextSlot()
                        return thirdAvatarResult.await()
                    }
                }
                return Result.failure(IOException("avatar unavailable"))
            }
        }
        val effects = RealtimeSideEffectsImpl(
            context,
            profiles,
            media,
            application.secretChatRepository,
            application.chatRepository,
            application.chatCacheRepository,
            chatInfoLookup = { chatId ->
                Result.success(ChatRepository.ChatInfo(chatId, "Chat", "", false, 1, 0, 0, emptyList()))
            },
        ).apply {
            notificationEnrichmentFinished = { _, chatId ->
                if (chatId == chatIds.getValue(503L)) thirdEnrichmentCompleted.complete(Unit)
            }
        }
        for ((senderId, chatId) in chatIds) {
            val message = Shared.Message.newBuilder()
                .setId(senderId)
                .setSenderId(senderId)
                .setSentAt(Timestamp.newBuilder().setSeconds(System.currentTimeMillis() / 1_000).build())
                .setContent(Shared.MessageContent.newBuilder().setText("ordinary $senderId").build())
                .build()
            AndroidNotificationPostBudget.awaitNextSlot()
            effects.showMessageNotification(UpdatesApiOuterClass.NewMessageEvent.newBuilder()
                .setChatId(chatId).setMessage(message).build())
            awaitFor("immediate fallback for sender $senderId") {
                val notification = manager.activeNotifications.firstOrNull {
                    it.tag == "regular:${scope().id}:$chatId"
                }?.notification ?: return@awaitFor false
                NotificationHelper.messagingHistoryFromNotification(notification).singleOrNull()
                    ?.person?.name?.toString() == context.getString(R.string.notification_unknown_sender_id, senderId)
            }
            if (senderId < 503L) {
                awaitFor("profile request for sender $senderId") { profileRequests.contains(senderId) }
            }
        }
        for (senderId in 501L..502L) {
            AndroidNotificationPostBudget.awaitNextSlot()
            profileGates.getValue(senderId).complete(Unit)
            val chatId = chatIds.getValue(senderId)
            awaitFor("sender $senderId name before avatar") {
                val notification = manager.activeNotifications.firstOrNull {
                    it.tag == "regular:${scope().id}:$chatId"
                }?.notification ?: return@awaitFor false
                NotificationHelper.messagingHistoryFromNotification(notification).singleOrNull()
                    ?.person?.name?.toString() == "Sender $senderId"
            }
            awaitFor("sender $senderId avatar request holds its permit") {
                avatarRequests[avatarIds.getValue(senderId)]?.get() == 1
            }
        }
        assertFalse(profileRequests.contains(503L))

        awaitFor("third profile starts after both avatar timeouts") { profileRequests.contains(503L) }
        AndroidNotificationPostBudget.awaitNextSlot()
        profileGates.getValue(503L).complete(Unit)
        awaitFor("third queued profile is enriched after bounded avatar waits") {
            val chatId = chatIds.getValue(503L)
            val notification = manager.activeNotifications.firstOrNull {
                it.tag == "regular:${scope().id}:$chatId"
            }?.notification ?: return@awaitFor false
            NotificationHelper.messagingHistoryFromNotification(notification).singleOrNull()
                ?.person?.name?.toString() == "Sender 503"
        }
        thirdAvatarStarted.await()
        val thirdTag = "regular:${scope().id}:${chatIds.getValue(503L)}"
        val deleteIntent = requireNotNull(manager.activeNotifications.single { it.tag == thirdTag }.notification.deleteIntent)
        manager.cancel(thirdTag, 0)
        AndroidNotificationPostBudget.awaitNextSlot()
        AndroidNotificationPostBudget.awaitNextSlot()
        deleteIntent.send()
        awaitFor("swiped third realtime card") { manager.activeNotifications.none { it.tag == thirdTag } }
        AndroidNotificationPostBudget.awaitNextSlot()
        thirdAvatarResult.complete(Result.failure(IOException("avatar unavailable")))
        awaitFor("second third-avatar lookup begins") { avatarRequests[avatarIds.getValue(503L)]?.get() == 2 }
        withTimeout(15_000) { thirdEnrichmentCompleted.await() }
        assertTrue(manager.activeNotifications.none { it.tag == thirdTag })
    }

    @Test
    fun openingChatDismissesEncryptedNotificationWithoutInMemoryTracking() = runBlocking {
        postWithNotificationBudget {
            NotificationHelper.showEncryptedMessageNotification(context, NotificationHelper.KIND_PRIVATE, CHAT, "41")
        }
        await { encrypted(NotificationHelper.KIND_PRIVATE) != null }
        NotificationHelper.activeNotificationChats.clear()
        NotificationHelper.dismissForChat(context, CHAT)
        await { encrypted(NotificationHelper.KIND_PRIVATE) == null }
    }

    @Test
    fun notificationsRespectSelectedLanguageWithoutActivity() = runBlocking {
        val previousLanguage = global.appLanguage
        val translations = mapOf(
            "ru" to listOf("Ответить", "Сообщение", "Скрыть", "Новое зашифрованное сообщение"),
            "en" to listOf("Reply", "Message", "Hide", "New encrypted message"),
            "de" to listOf("Antworten", "Nachricht", "Ausblenden", "Neue verschlüsselte Nachricht"),
            "es" to listOf("Responder", "Mensaje", "Ocultar", "Nuevo mensaje cifrado"),
            "zh-CN" to listOf("回复", "消息", "隐藏", "新的加密消息")
        )
        try {
            for ((index, entry) in translations.entries.withIndex()) {
                val (language, expected) = entry
                global.appLanguage = language
                val chatId = "locale-$language"
                postWithNotificationBudget {
                    NotificationHelper.showMessageNotification(context, "Sender", "Message", null, chatId, 51L + index)
                }
                postWithNotificationBudget {
                    NotificationHelper.showEncryptedMessageNotification(context, NotificationHelper.KIND_PRIVATE, chatId, (51L + index).toString())
                }
                fun regularNotification() = manager.activeNotifications.firstOrNull {
                    it.tag == "regular:${scope().id}:$chatId" && it.id == 0
                }?.notification
                fun privateNotification() = manager.activeNotifications.firstOrNull {
                    it.notification.extras.getString(NotificationHelper.EXTRA_KIND) == NotificationHelper.KIND_PRIVATE &&
                        it.notification.extras.getString(NotificationHelper.EXTRA_CHAT_ID) == chatId
                }?.notification
                await { regularNotification() != null && privateNotification() != null }
                assertEquals(expected[0], regularNotification()!!.actions.first().title.toString())
                assertEquals(expected[1], regularNotification()!!.actions.first().remoteInputs.single().label.toString())
                assertEquals(expected[2], privateNotification()!!.actions.single().title.toString())
                assertEquals(expected[3], privateNotification()!!.extras.getCharSequence(Notification.EXTRA_TEXT).toString())
            }
            instrumentation.runOnMainSync {
                assertTrue(ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(Stage.RESUMED).isEmpty())
            }
        } finally {
            global.appLanguage = previousLanguage
        }
    }

    @Test
    fun delayedEncryptedMarkerFromPreviousAccountIsIgnored() = runBlocking {
        val effects = RealtimeSideEffectsImpl(context, unavailableGateway<UserProfileGateway>(),
            unavailableGateway<FileMediaGateway>(), application.secretChatRepository,
            application.chatRepository, application.chatCacheRepository)
        val oldScopeId = scope().id
        global.userId--
        dispatchEncryptedMarker(mapOf("type" to "new_private_message", "private_chat_id" to CHAT,
            "event_id" to "41", "sender_user_id" to "7"), effects, oldScopeId)
        effects.showSecretMessageNotification(UUID.randomUUID().toString(), 7, UUID.randomUUID().toString(), oldScopeId)
        assertTrue(manager.activeNotifications.isEmpty())
        global.userId++
        Unit
    }

    @Test
    fun encryptedTapSelectsChatListInWarmActivityAndClearsPendingChatNavigation() = runBlocking {
        val previousAccessToken = global.accessToken
        val previousRefreshToken = global.refreshToken
        val previousExpiration = global.accessTokenExpiration
        global.accessToken = "notification-test-access"
        global.refreshToken = "notification-test-refresh"
        global.accessTokenExpiration = System.currentTimeMillis() + 60 * 60 * 1_000
        if (Build.VERSION.SDK_INT >= 34) {
            instrumentation.uiAutomation.executeShellCommand("appops set ${context.packageName} USE_FULL_SCREEN_INTENT allow").close()
        }
        try {
            val activity = startAuthenticatedMain()
            instrumentation.runOnMainSync {
                activity.findViewById<View>(R.id.profileNavButton).performClick()
                assertEquals(View.GONE, activity.findViewById<View>(R.id.createChatFab).visibility)
            }
            postWithNotificationBudget {
                NotificationHelper.showEncryptedMessageNotification(context, NotificationHelper.KIND_PRIVATE, CHAT, "41")
            }
            await { encrypted(NotificationHelper.KIND_PRIVATE) != null }
            encrypted(NotificationHelper.KIND_PRIVATE)!!.notification.contentIntent.send()
            await {
                (resumedActivity(MainActivity::class.java) as? MainActivity)
                    ?.findViewById<View>(R.id.createChatFab)?.visibility == View.VISIBLE
            }
        } finally {
            finishResumedActivity(ChatActivity::class.java)
            finishResumedActivity(MainActivity::class.java)
            global.accessToken = previousAccessToken
            global.refreshToken = previousRefreshToken
            global.accessTokenExpiration = previousExpiration
        }
    }

    @Test
    fun regularNotificationOpensResolvedChatAndBackReturnsToChatList() = runBlocking {
        val previousAccessToken = global.accessToken
        val previousRefreshToken = global.refreshToken
        val previousExpiration = global.accessTokenExpiration
        try {
            val activity = startAuthenticatedMain()
            activity.chatDirectoryGateway = notificationChatDirectoryGateway()
            activity.handleChatIntent(notificationOpenIntent())
            withTimeout(15_000) {
                while (resumedActivity(ChatActivity::class.java) == null) delay(25)
            }
            val opened = resumedActivity(ChatActivity::class.java) as ChatActivity
            assertEquals(41L, opened.intent.getLongExtra("first_unread_message_id", 0L))

            instrumentation.sendKeyDownUpSync(KeyEvent.KEYCODE_BACK)
            withTimeout(15_000) {
                while (resumedActivity(MainActivity::class.java)?.findViewById<View>(R.id.createChatFab)?.visibility != View.VISIBLE) {
                    delay(25)
                }
            }
        } finally {
            global.accessToken = previousAccessToken
            global.refreshToken = previousRefreshToken
            global.accessTokenExpiration = previousExpiration
            finishResumedActivity(ChatActivity::class.java)
            finishResumedActivity(MainActivity::class.java)
        }
    }

    @Test
    fun transientChatLookupOpensCachedRegularChatWithItsFirstUnreadAnchor() = runBlocking {
        val previousAccessToken = global.accessToken
        val previousRefreshToken = global.refreshToken
        val previousExpiration = global.accessTokenExpiration
        val cached = ChatSummary(
            id = CHAT,
            title = "Cached chat",
            picture = "",
            isGroupChat = false,
            lastMessage = null,
            memberIds = listOf(global.userId, 7L),
            countUnread = 2,
            firstUnreadMessageId = 41,
        )
        application.chatCacheRepository.saveChatPage(scope(), listOf(cached), 1, replaceExisting = true)
        try {
            val calls = AtomicInteger()
            val activity = startAuthenticatedMain()
            activity.chatDirectoryGateway = notificationChatDirectoryGateway(
                failure = IOException("offline"), calls = calls,
            )
            activity.handleChatIntent(notificationOpenIntent())
            withTimeout(15_000) {
                while (resumedActivity(ChatActivity::class.java) == null) delay(25)
            }
            val opened = resumedActivity(ChatActivity::class.java) as ChatActivity
            assertEquals("Cached chat", opened.intent.getStringExtra("chat_title"))
            assertEquals(41L, opened.intent.getLongExtra("first_unread_message_id", 0L))
            assertEquals(1, calls.get())
            assertEquals(scope().id, opened.intent.getStringExtra(NotificationHelper.EXTRA_SCOPE_ID))
        } finally {
            finishResumedActivity(ChatActivity::class.java)
            finishResumedActivity(MainActivity::class.java)
            global.accessToken = previousAccessToken
            global.refreshToken = previousRefreshToken
            global.accessTokenExpiration = previousExpiration
        }
    }

    @Test
    fun permanentNoAccessFailureDoesNotOpenEvenWhenOrdinaryChatIsCached() = runBlocking {
        val previousAccessToken = global.accessToken
        val previousRefreshToken = global.refreshToken
        val previousExpiration = global.accessTokenExpiration
        application.chatCacheRepository.saveChatPage(scope(), listOf(ChatSummary(
            id = CHAT,
            title = "Cached chat",
            picture = "",
            isGroupChat = false,
            lastMessage = null,
            memberIds = listOf(global.userId, 7L),
            countUnread = 2,
            firstUnreadMessageId = 41,
        )), 1, replaceExisting = true)
        val errorCode = Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER)
        val noAccess = Metadata().apply { put(errorCode, "604DD334-0484-4C6B-8113-354B9D2FDF2A") }
        try {
            val calls = AtomicInteger()
            val activity = startAuthenticatedMain()
            activity.chatDirectoryGateway = notificationChatDirectoryGateway(
                failure = Status.FAILED_PRECONDITION.asRuntimeException(noAccess), calls = calls,
            )
            val routeIntent = notificationOpenIntent()
            assertNotNull("Route should match current activity scope ${CacheScope.from(GlobalParam(activity))?.id}",
                NotificationHelper.currentTarget(activity, routeIntent))
            activity.handleChatIntent(routeIntent)
            await { calls.get() == 1 }
            instrumentation.waitForIdleSync()
            assertEquals(1, calls.get())
            assertNull(resumedActivity(ChatActivity::class.java))
            assertEquals(View.VISIBLE, (resumedActivity(MainActivity::class.java) as MainActivity)
                .findViewById<View>(R.id.createChatFab).visibility)
        } finally {
            finishResumedActivity(ChatActivity::class.java)
            finishResumedActivity(MainActivity::class.java)
            global.accessToken = previousAccessToken
            global.refreshToken = previousRefreshToken
            global.accessTokenExpiration = previousExpiration
        }
    }

    @Test
    fun consumedNotificationIntentDoesNotReplayOnMainActivityRecreation() = runBlocking {
        val previousAccessToken = global.accessToken
        val previousRefreshToken = global.refreshToken
        val previousExpiration = global.accessTokenExpiration
        global.accessToken = "notification-test-access"
        global.refreshToken = "notification-test-refresh"
        global.accessTokenExpiration = System.currentTimeMillis() + 60 * 60 * 1_000
        try {
            finishResumedActivity(ChatActivity::class.java)
            finishResumedActivity(MainActivity::class.java)
            val calls = AtomicInteger()
            context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            await { resumedActivity(MainActivity::class.java) != null }
            val main = resumedActivity(MainActivity::class.java) as MainActivity
            instrumentation.runOnMainSync {
                application.realtimeService.pause()
                application.callEventsService.pause()
                authenticate(main)
                main.chatDirectoryGateway = notificationChatDirectoryGateway(calls = calls)
                main.handleChatIntent(notificationOpenIntent())
            }
            withTimeout(15_000) {
                while (resumedActivity(ChatActivity::class.java) == null) delay(25)
            }
            instrumentation.sendKeyDownUpSync(KeyEvent.KEYCODE_BACK)
            withTimeout(15_000) {
                while (resumedActivity(MainActivity::class.java) == null) delay(25)
            }
            val beforeRecreate = resumedActivity(MainActivity::class.java) as MainActivity
            instrumentation.runOnMainSync { beforeRecreate.recreate() }
            withTimeout(15_000) {
                while (resumedActivity(MainActivity::class.java) == null) delay(25)
            }
            val recreated = resumedActivity(MainActivity::class.java) as MainActivity
            assertEquals(View.VISIBLE, recreated.findViewById<View>(R.id.createChatFab).visibility)
            assertNull(recreated.intent.data)
            assertNull(recreated.intent.action)
            assertNull(resumedActivity(ChatActivity::class.java))
            assertEquals(1, calls.get())
        } finally {
            finishResumedActivity(ChatActivity::class.java)
            finishResumedActivity(MainActivity::class.java)
            global.accessToken = previousAccessToken
            global.refreshToken = previousRefreshToken
            global.accessTokenExpiration = previousExpiration
        }
    }

    @Test
    fun coldClickFromPreviousAccountIsRejectedBeforeOpeningChat() = runBlocking {
        val previousAccessToken = global.accessToken
        val previousRefreshToken = global.refreshToken
        val previousExpiration = global.accessTokenExpiration
        val previousUserId = global.userId
        global.accessToken = "notification-test-access"
        global.refreshToken = "notification-test-refresh"
        global.accessTokenExpiration = System.currentTimeMillis() + 60 * 60 * 1_000
        try {
            showRegular(42)
            await { regular() != null }
            val foreignIntent = requireNotNull(regular()).contentIntent
            global.userId--
            foreignIntent.send()
            await { resumedActivity(MainActivity::class.java) != null }
            delay(250)
            assertNull(resumedActivity(ChatActivity::class.java))
            assertTrue(resumedActivity(MainActivity::class.java) != null)
        } finally {
            finishResumedActivity(ChatActivity::class.java)
            finishResumedActivity(MainActivity::class.java)
            global.userId = previousUserId
            global.accessToken = previousAccessToken
            global.refreshToken = previousRefreshToken
            global.accessTokenExpiration = previousExpiration
        }
    }

    @Test
    fun coldLegacyChatIdWithoutScopeIsRejected() {
        runBlocking {
            val previousAccessToken = global.accessToken
            val previousRefreshToken = global.refreshToken
            val previousExpiration = global.accessTokenExpiration
            global.accessToken = "notification-test-access"
            global.refreshToken = "notification-test-refresh"
            global.accessTokenExpiration = System.currentTimeMillis() + 60 * 60 * 1_000
            val intent = Intent(context, MainActivity::class.java).apply {
                action = Intent.ACTION_VIEW
                putExtra("chat_id", CHAT)
                flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
            }
            try {
                finishResumedActivity(ChatActivity::class.java)
                finishResumedActivity(MainActivity::class.java)
                context.startActivity(intent)
                withTimeout(15_000) {
                    while (resumedActivity(MainActivity::class.java) == null) delay(25)
                }
                assertNull(resumedActivity(ChatActivity::class.java))
                assertEquals(View.VISIBLE, (resumedActivity(MainActivity::class.java) as MainActivity)
                    .findViewById<View>(R.id.createChatFab).visibility)
            } finally {
                finishResumedActivity(ChatActivity::class.java)
                finishResumedActivity(MainActivity::class.java)
                global.accessToken = previousAccessToken
                global.refreshToken = previousRefreshToken
                global.accessTokenExpiration = previousExpiration
            }
        }
    }

    private fun scope() = requireNotNull(CacheScope.from(global))
    private suspend fun showRegular(id: Long) {
        postRegular(id)
        awaitFor("regular message $id") {
            NotificationHelper.messagingHistoryFromNotification(regular() ?: return@awaitFor false)
                .any { it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID) == id }
        }
    }
    private fun postRegular(id: Long) = postWithNotificationBudget {
        NotificationHelper.showMessageNotification(
            context, "Sender", "Message", null, CHAT, messageId = id, expectedScopeId = scope().id,
        )
    }
    private fun postRegular(
        input: RegularNotificationInput,
        senderAvatarBitmap: Bitmap? = null,
        chatAvatarBitmap: Bitmap? = null,
    ) = postWithNotificationBudget {
        NotificationHelper.showRegularMessageNotification(context, input, senderAvatarBitmap, chatAvatarBitmap)
    }
    private inline fun postWithNotificationBudget(action: () -> Unit) {
        AndroidNotificationPostBudget.awaitNextSlot()
        action()
    }
    private fun regular() = manager.activeNotifications.firstOrNull {
        it.tag == "regular:${scope().id}:$CHAT" && it.id == 0
    }?.notification
    private fun encrypted(kind: String) = manager.activeNotifications.firstOrNull {
        it.notification.extras.getString(NotificationHelper.EXTRA_KIND) == kind
    }
    private fun historyIds(scopeId: String, chatId: String) = manager.activeNotifications
        .firstOrNull { it.tag == "regular:$scopeId:$chatId" }
        ?.let { NotificationHelper.messagingHistoryFromNotification(it.notification) }
        ?.map { it.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID) }
    private fun replyIntent(text: String) = withReply(NotificationHelper.actionIntent(context,
        MessageNotificationTarget(scope().id, NotificationHelper.KIND_REGULAR, CHAT, "41"), NotificationHelper.ACTION_REPLY), text)
    private fun readIntent(cutoff: Long) = NotificationHelper.actionIntent(context,
        MessageNotificationTarget(scope().id, NotificationHelper.KIND_REGULAR, CHAT, cutoff.toString()),
        NotificationHelper.ACTION_MARK_AS_READ)
    private fun withReply(intent: Intent, text: String): Intent {
        RemoteInput.addResultsToIntent(arrayOf(RemoteInput.Builder(NotificationHelper.REPLY_INPUT_KEY).build()), intent,
            Bundle().apply { putCharSequence(NotificationHelper.REPLY_INPUT_KEY, text) })
        return intent
    }
    private suspend fun await(condition: suspend () -> Boolean) = withTimeout(15_000) {
        while (!condition()) delay(25)
    }
    private suspend fun awaitFor(description: String, condition: suspend () -> Boolean) {
        val reached = withTimeoutOrNull(15_000) {
            while (!condition()) delay(25)
            true
        }
        val active = manager.activeNotifications.joinToString { notification ->
            val extras = notification.notification.extras
            "${notification.tag}/${notification.id} scope=${extras.getString(NotificationHelper.EXTRA_SCOPE_ID)} " +
                "chat=${extras.getString(NotificationHelper.EXTRA_CHAT_ID)} " +
                "kind=${extras.getString(NotificationHelper.EXTRA_KIND)} " +
                "cutoff=${extras.getLong(NotificationHelper.EXTRA_RECEIVED_CUTOFF, -1)} history=" +
                NotificationHelper.messagingHistoryFromNotification(notification.notification).joinToString { message ->
                    "id=${message.extras.getLong(NotificationHelper.EXTRA_SERVER_MESSAGE_ID, 0)} " +
                        "reply=${message.extras.getString(NotificationHelper.EXTRA_REPLY_BODY)}"
                }
        }
        assertTrue("Timed out waiting for $description (scope=${scope().id}; active=$active)", reached == true)
    }
    private fun resumedActivity(type: Class<out android.app.Activity>): android.app.Activity? {
        var activity: android.app.Activity? = null
        instrumentation.runOnMainSync {
            activity = ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(Stage.RESUMED)
                .firstOrNull(type::isInstance)
        }
        return activity
    }
    private fun finishResumedActivity(type: Class<out android.app.Activity>) {
        instrumentation.runOnMainSync {
            ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(Stage.RESUMED)
                .filter(type::isInstance).forEach(android.app.Activity::finish)
        }
    }
    private fun notificationOpenIntent() = Intent(context, MainActivity::class.java).apply {
        action = Intent.ACTION_VIEW
        data = android.net.Uri.Builder().scheme("barkfluff").authority("message-notification")
            .appendPath(scope().id).appendPath(NotificationHelper.KIND_REGULAR)
            .appendPath(CHAT).appendPath("41").appendPath(Intent.ACTION_VIEW).build()
        putExtra(NotificationHelper.EXTRA_SCOPE_ID, scope().id)
        putExtra(NotificationHelper.EXTRA_KIND, NotificationHelper.KIND_REGULAR)
        putExtra(NotificationHelper.EXTRA_CHAT_ID, CHAT)
        putExtra(NotificationHelper.EXTRA_THREAD_ID, CHAT)
        putExtra(NotificationHelper.EXTRA_EVENT_ID, "41")
    }

    private fun notificationChatDirectoryGateway(
        failure: Throwable? = null,
        calls: AtomicInteger = AtomicInteger(),
    ): ChatDirectoryGateway = object : ChatDirectoryGateway {
        override suspend fun chats(offset: Int, size: Int) = error("Unexpected chats call")
        override suspend fun chat(chatId: String): Result<ChatSummary> {
            calls.incrementAndGet()
            if (failure != null) return Result.failure(failure)
            return Result.success(ChatSummary(
                id = chatId,
                title = "Notification chat",
                picture = "",
                isGroupChat = false,
                lastMessage = null,
                memberIds = listOf(global.userId, 7L),
                countUnread = 2,
                firstUnreadMessageId = 41,
            ))
        }
        override suspend fun members(chatId: String) = error("Unexpected members call")
        override suspend fun addMember(chatId: String, userId: Long) = error("Unexpected addMember call")
        override suspend fun removeMember(chatId: String, userId: Long) = error("Unexpected removeMember call")
        override suspend fun createGroup(userIds: List<Long>, title: String, pictureFileId: String?) =
            error("Unexpected createGroup call")
        override suspend fun updateGroup(chatId: String, title: String?, pictureFileId: String?) =
            error("Unexpected updateGroup call")
    }
    private fun authenticate(activity: MainActivity) {
        GlobalParam(activity).apply {
            accessToken = "notification-test-access"
            refreshToken = "notification-test-refresh"
            accessTokenExpiration = System.currentTimeMillis() + 60 * 60 * 1_000
        }
    }

    private suspend fun startAuthenticatedMain(): MainActivity {
        finishResumedActivity(ChatActivity::class.java)
        finishResumedActivity(MainActivity::class.java)
        global.accessToken = "notification-test-access"
        global.refreshToken = "notification-test-refresh"
        global.accessTokenExpiration = System.currentTimeMillis() + 60 * 60 * 1_000
        context.startActivity(Intent(context, MainActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK
        })
        withTimeout(15_000) {
            while (resumedActivity(MainActivity::class.java) == null) delay(25)
        }
        val activity = resumedActivity(MainActivity::class.java) as MainActivity
        instrumentation.runOnMainSync {
            application.realtimeService.pause()
            application.callEventsService.pause()
            authenticate(activity)
        }
        return activity
    }

    private inline fun <reified T> unavailableGateway(): T = Proxy.newProxyInstance(T::class.java.classLoader,
        arrayOf(T::class.java)) { _, _, _ -> error("Encrypted notifications must not resolve/decrypt contents") } as T

    companion object { private const val CHAT = "notification-chat" }
}

private object AndroidNotificationPostBudget {
    // NMS shed this package near 5 posts/s; keep instrumentation-triggered posts at 2/s.
    private const val MINIMUM_POST_INTERVAL_MILLIS = 500L
    private var lastReservedPostElapsedRealtime = 0L

    @Synchronized
    fun awaitNextSlot() {
        val previous = lastReservedPostElapsedRealtime
        if (previous > 0L) {
            val remaining = MINIMUM_POST_INTERVAL_MILLIS - (SystemClock.elapsedRealtime() - previous)
            if (remaining > 0L) SystemClock.sleep(remaining)
        }
        lastReservedPostElapsedRealtime = SystemClock.elapsedRealtime()
    }
}
