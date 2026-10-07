package com.barkfluff.client.notifications

import android.Manifest
import android.app.NotificationManager
import android.content.Context
import android.os.Build
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.OutgoingMessageState
import com.barkfluff.client.data.GlobalParam
import java.io.IOException
import java.util.UUID
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith

/** Multi-process fixture: run each step separately with -e cold_start_step <step>.
 * Kill the app between steps; SystemUI sends the saved Reply/Hide PendingIntents while it is dead.
 */
@RunWith(AndroidJUnit4::class)
class ColdNotificationScenarioTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private val application get() = context.applicationContext as BarkFluffApplication
    private val global get() = GlobalParam(context)
    private val manager get() = context.getSystemService(NotificationManager::class.java)
    private val fixture get() = context.getSharedPreferences("cold_notification_test", Context.MODE_PRIVATE)
    private fun scope() = requireNotNull(CacheScope.from(global))
    private fun step(name: String) = assumeTrue(InstrumentationRegistry.getArguments().getString("cold_start_step") == name)

    @Test
    fun language() {
        step("language")
        global.appLanguage = InstrumentationRegistry.getArguments().getString("language") ?: "system"
        assertTrue(global.sharedPreferences.edit().commit())
    }

    @Test
    fun seed() = runBlocking {
        step("seed")
        fixture.edit().putString("beacon", global.socketBeacon).putLong("user", global.userId)
            .putBoolean("enabled", global.notificationsEnabled).commit()
        global.socketBeacon = "cold-notification-test"
        global.userId = Long.MAX_VALUE - 11
        global.notificationsEnabled = true
        assertTrue(global.sharedPreferences.edit().commit())
        application.outgoingMessageQueue.cancelAllForCurrentScope()
        if (Build.VERSION.SDK_INT >= 33) {
            instrumentation.uiAutomation.grantRuntimePermission(context.packageName, Manifest.permission.POST_NOTIFICATIONS)
        }
        manager.cancelAll()
        // Fresh ids also make repeated local runs independent of the persistent dedup cache.
        context.getSharedPreferences("message_notification_events", Context.MODE_PRIVATE).edit().clear().commit()
        NotificationHelper.showMessageNotification(context, "Cold reply", "Reply while the process is stopped", null, CHAT, 1001)
        NotificationHelper.showEncryptedMessageNotification(context, NotificationHelper.KIND_PRIVATE, "cold-private", "1001")
        NotificationHelper.showEncryptedMessageNotification(context, NotificationHelper.KIND_SECRET,
            "7:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", UUID.randomUUID().toString())
        withTimeout(5_000) { while (manager.activeNotifications.size != 3) delay(25) }
    }

    @Test
    fun verifyReply() = runBlocking {
        step("verifyReply")
        val operations = application.chatCacheRepository.outgoingOperationIds(scope())
        assertEquals(1, operations.size)
        val record = application.chatCacheRepository.outgoing(scope(), operations.single())!!
        assertEquals("cold_reply", record.text)
        assertEquals(OutgoingMessageState.QUEUED, record.state)
        assertEquals(0L, record.replyToMessageId)
        assertNull(record.draftGeneration)
        assertEquals(1001L, application.chatCacheRepository.readyPendingReads(scope(), Long.MAX_VALUE).single().messageId)
        assertFalse(manager.activeNotifications.any { it.tag == null && it.id == CHAT.hashCode() })
    }

    @Test
    fun verifyHide() {
        step("verifyHide")
        assertFalse(manager.activeNotifications.any {
            it.notification.extras.getString(NotificationHelper.EXTRA_KIND) in
                listOf(NotificationHelper.KIND_PRIVATE, NotificationHelper.KIND_SECRET)
        })
    }

    @Test
    fun verifyIndependentReadRetry() = runBlocking {
        step("verifyIndependentReadRetry")
        val operationId = application.chatCacheRepository.outgoingOperationIds(scope()).single()
        val before = application.chatCacheRepository.outgoing(scope(), operationId)
        application.outgoingMessageQueue.drainPendingReads(scope(), Long.MAX_VALUE) {
            Result.failure(IOException("offline"))
        }
        assertTrue(application.chatCacheRepository.readyPendingReads(scope(), Long.MAX_VALUE).single().attemptCount > 0)
        application.outgoingMessageQueue.drainPendingReads(scope(), Long.MAX_VALUE) { ids ->
            assertEquals(listOf(1001L), ids)
            Result.success(Unit)
        }
        assertTrue(application.chatCacheRepository.readyPendingReads(scope(), Long.MAX_VALUE).isEmpty())
        assertEquals(before, application.chatCacheRepository.outgoing(scope(), operationId))
        assertEquals(setOf(operationId), application.chatCacheRepository.outgoingOperationIds(scope()).toSet())
    }

    @Test
    fun cleanup() = runBlocking {
        step("cleanup")
        application.outgoingMessageQueue.cancelAllForCurrentScope()
        manager.cancelAll()
        global.socketBeacon = fixture.getString("beacon", "").orEmpty()
        global.userId = fixture.getLong("user", 0)
        global.notificationsEnabled = fixture.getBoolean("enabled", true)
        assertTrue(global.sharedPreferences.edit().commit())
        fixture.edit().clear().commit()
        Unit
    }

    companion object { private const val CHAT = "cold-notification-chat" }
}
