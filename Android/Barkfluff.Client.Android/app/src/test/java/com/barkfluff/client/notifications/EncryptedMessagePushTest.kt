package com.barkfluff.client.notifications

import barkfluff.updates.UpdatesApiOuterClass
import com.barkfluff.client.grpc.RealtimeSideEffects
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class EncryptedMessagePushTest {
    @Test
    fun privateMarkerDispatchesOnlyIdentifiers() = runTest {
        val effects = RecordingEffects()
        assertTrue(dispatchEncryptedMarker(mapOf("type" to "new_private_message", "private_chat_id" to "chat",
            "event_id" to "42", "sender_user_id" to "7", "message_text" to "must not display"), effects))
        assertEquals(listOf("private:chat:42:7"), effects.events)
    }

    @Test
    fun secretMarkerUsesSeparateKeysAndCanonicalDeviceId() = runTest {
        val effects = RecordingEffects()
        val eventId = "11111111-1111-1111-1111-111111111111"
        val deviceId = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"
        assertTrue(dispatchEncryptedMarker(mapOf("type" to "new_secret_message", "event_id" to eventId,
            "sender_user_id" to "7", "sender_device_id" to deviceId), effects))
        assertEquals(listOf("secret:$eventId:7:${deviceId.lowercase()}"), effects.events)
    }

    @Test
    fun incompleteUnknownAndLegacyPayloadsNeverDispatchEncryptedCallbacks() = runTest {
        val effects = RecordingEffects()
        for (data in listOf(
            mapOf("type" to "new_private_message", "chat_id" to "chat", "message_id" to "42", "sender_user_id" to "7"),
            mapOf("type" to "new_private_message", "private_chat_id" to "chat", "event_id" to "0", "sender_user_id" to "7"),
            mapOf("type" to "new_secret_message", "event_id" to "bad-id", "sender_user_id" to "7"),
            mapOf("type" to "future_type", "sender_user_id" to "7")
        )) assertFalse(dispatchEncryptedMarker(data, effects))
        assertTrue(effects.events.isEmpty())
    }

    private class RecordingEffects : RealtimeSideEffects {
        val events = mutableListOf<String>()
        override fun onChatChanged(chatId: String) {}
        override fun dismissChatNotifications(chatId: String, messageId: Long) {}
        override suspend fun showMessageNotification(event: UpdatesApiOuterClass.NewMessageEvent) {
            error("Encrypted marker reached ordinary message handling")
        }
        override suspend fun showPrivateMessageNotification(chatId: String, messageId: Long, senderUserId: Long, expectedScopeId: String?) {
            events.add("private:$chatId:$messageId:$senderUserId")
        }
        override suspend fun showSecretMessageNotification(messageId: String, senderUserId: Long, senderDeviceId: String, expectedScopeId: String?) {
            events.add("secret:$messageId:$senderUserId:$senderDeviceId")
        }
    }
}
