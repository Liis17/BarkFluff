package com.barkfluff.client.notifications

import android.Manifest
import android.app.Notification
import android.app.NotificationManager
import android.content.Context
import android.content.Intent
import android.database.sqlite.SQLiteException
import android.os.Build
import android.os.Bundle
import android.view.View
import androidx.core.app.RemoteInput
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry
import androidx.test.runner.lifecycle.Stage
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.MainActivity
import com.barkfluff.client.R
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import java.lang.reflect.Proxy
import java.util.UUID
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
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

    @Before
    fun setUp() {
        global = GlobalParam(context)
        previousBeacon = global.socketBeacon
        previousUserId = global.userId
        previousEnabled = global.notificationsEnabled
        global.socketBeacon = "notification-test-${UUID.randomUUID()}"
        global.userId = Long.MAX_VALUE - 9
        global.notificationsEnabled = true
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
        showRegular(42)
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
        reply.actionIntent.send(context, 0, fillIn)
        await { application.chatCacheRepository.outgoingOperationIds(scope()).isNotEmpty() }
        val operationId = application.chatCacheRepository.outgoingOperationIds(scope()).single()
        val record = application.chatCacheRepository.outgoing(scope(), operationId)!!
        assertEquals("offline reply", record.text)
        assertEquals(0L, record.replyToMessageId)
        assertNull(record.draftGeneration)
        assertEquals(41L, application.chatCacheRepository.readyPendingReads(scope(), Long.MAX_VALUE).single().messageId)
        await { regular() == null }
        instrumentation.runOnMainSync {
            assertTrue(ActivityLifecycleMonitorRegistry.getInstance().getActivitiesInStage(Stage.RESUMED).isEmpty())
        }
    }

    @Test
    fun blankWrongAccountAndEncryptedRepliesNeverReachTheQueue() = runBlocking {
        var enqueued = 0
        val receiver = NotificationActionReceiver()
        receiver.acceptReply(context, replyIntent(" \n ")) { enqueued++ }
        val stale = replyIntent("reply")
        global.userId--
        receiver.acceptReply(context, stale) { enqueued++ }
        global.userId++
        val encrypted = MessageNotificationTarget(scope().id, NotificationHelper.KIND_PRIVATE, CHAT, "41")
        receiver.acceptReply(context, withReply(NotificationHelper.actionIntent(context, encrypted, NotificationHelper.ACTION_REPLY), "reply")) {
            enqueued++
        }
        assertEquals(0, enqueued)
        assertTrue(application.chatCacheRepository.outgoingOperationIds(scope()).isEmpty())
        assertTrue(application.chatCacheRepository.readyPendingReads(scope(), Long.MAX_VALUE).isEmpty())
    }

    @Test
    fun storageFailureLeavesNotificationAndShowsLocalizedError() = runBlocking {
        showRegular(41)
        await { regular() != null }
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
        NotificationActionReceiver().acceptReply(context, replyIntent("reply")) {
            showRegular(42)
            await { regular()?.extras?.getLong(NotificationHelper.EXTRA_MESSAGE_ID) == 42L }
        }
        assertEquals(42L, regular()!!.extras.getLong(NotificationHelper.EXTRA_MESSAGE_ID))
    }

    @Test
    fun notificationArrivingAfterReplyAcknowledgementSurvivesPendingCancellation() = runBlocking {
        showRegular(41)
        await { regular() != null }
        val acceptance = launch(start = CoroutineStart.UNDISPATCHED) {
            NotificationActionReceiver().acceptReply(context, replyIntent("reply")) { }
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
            unavailableGateway<FileMediaGateway>(), application.secretChatRepository)
        val data = mapOf("type" to "new_private_message", "private_chat_id" to CHAT,
            "event_id" to "41", "sender_user_id" to "7")
        assertTrue(dispatchEncryptedMarker(data, effects))
        await { encrypted(NotificationHelper.KIND_PRIVATE) != null }
        val first = encrypted(NotificationHelper.KIND_PRIVATE)!!
        val postTime = first.postTime
        effects.showPrivateMessageNotification(CHAT, 41, 7)
        delay(100)
        assertEquals(postTime, encrypted(NotificationHelper.KIND_PRIVATE)!!.postTime)
        effects.showSecretMessageNotification(UUID.randomUUID().toString(), 7, UUID.randomUUID().toString())
        await { encrypted(NotificationHelper.KIND_SECRET) != null }
        showRegular(41)
        await { regular() != null }
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
            await { encrypted(kind) == null }
        }
        assertNotNull(regular())
    }

    @Test
    fun openingChatDismissesEncryptedNotificationWithoutInMemoryTracking() = runBlocking {
        NotificationHelper.showEncryptedMessageNotification(context, NotificationHelper.KIND_PRIVATE, CHAT, "41")
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
                NotificationHelper.showMessageNotification(context, "Sender", "Message", null, chatId, 51L + index)
                NotificationHelper.showEncryptedMessageNotification(context, NotificationHelper.KIND_PRIVATE, chatId, (51L + index).toString())
                fun regularNotification() = manager.activeNotifications.firstOrNull {
                    it.tag == null && it.id == chatId.hashCode()
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
            unavailableGateway<FileMediaGateway>(), application.secretChatRepository)
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
            ActivityScenario.launch<MainActivity>(Intent(context, MainActivity::class.java)).use { scenario ->
                scenario.onActivity { activity ->
                    activity.findViewById<View>(R.id.profileNavButton).performClick()
                    assertEquals(View.GONE, activity.findViewById<View>(R.id.createChatFab).visibility)
                    MainActivity.pendingChatId = "must-not-open"
                    MainActivity.pendingChatIsPrivate = true
                }
                NotificationHelper.showEncryptedMessageNotification(context, NotificationHelper.KIND_PRIVATE, CHAT, "41")
                await { encrypted(NotificationHelper.KIND_PRIVATE) != null }
                encrypted(NotificationHelper.KIND_PRIVATE)!!.notification.contentIntent.send()
                await {
                    var showsChats = false
                    scenario.onActivity { showsChats = it.findViewById<View>(R.id.createChatFab).visibility == View.VISIBLE }
                    showsChats
                }
                assertNull(MainActivity.pendingChatId)
                assertFalse(MainActivity.pendingChatIsPrivate)
            }
        } finally {
            global.accessToken = previousAccessToken
            global.refreshToken = previousRefreshToken
            global.accessTokenExpiration = previousExpiration
            MainActivity.pendingChatId = null
            MainActivity.pendingChatIsPrivate = false
        }
    }

    private fun scope() = requireNotNull(CacheScope.from(global))
    private fun showRegular(id: Long) = NotificationHelper.showMessageNotification(context, "Sender", "Message", null, CHAT, id)
    private fun regular() = manager.activeNotifications.firstOrNull { it.tag == null && it.id == CHAT.hashCode() }?.notification
    private fun encrypted(kind: String) = manager.activeNotifications.firstOrNull {
        it.notification.extras.getString(NotificationHelper.EXTRA_KIND) == kind
    }
    private fun replyIntent(text: String) = withReply(NotificationHelper.actionIntent(context,
        MessageNotificationTarget(scope().id, NotificationHelper.KIND_REGULAR, CHAT, "41"), NotificationHelper.ACTION_REPLY), text)
    private fun withReply(intent: Intent, text: String): Intent {
        RemoteInput.addResultsToIntent(arrayOf(RemoteInput.Builder(NotificationHelper.REPLY_INPUT_KEY).build()), intent,
            Bundle().apply { putCharSequence(NotificationHelper.REPLY_INPUT_KEY, text) })
        return intent
    }
    private suspend fun await(condition: suspend () -> Boolean) = withTimeout(5_000) {
        while (!condition()) delay(25)
    }
    private inline fun <reified T> unavailableGateway(): T = Proxy.newProxyInstance(T::class.java.classLoader,
        arrayOf(T::class.java)) { _, _, _ -> error("Encrypted notifications must not resolve/decrypt contents") } as T

    companion object { private const val CHAT = "notification-chat" }
}
