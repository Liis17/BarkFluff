package com.barkfluff.client.grpc

import barkfluff.shared.Shared
import barkfluff.updates.UpdatesApiOuterClass.NewMessageEvent
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class NewMessageHeartbeatTest {

    @Test
    fun emptyEventFromServerIsHeartbeat() {
        assertTrue(NewMessageEvent.getDefaultInstance().isHeartbeat())
    }

    @Test
    fun eventWithoutMessageIdIsHeartbeat() {
        val event = NewMessageEvent.newBuilder()
            .setChatId("7c9e6679-7425-40de-944b-e07fc1f90ae7")
            .setMessage(Shared.Message.getDefaultInstance())
            .build()

        assertTrue(event.isHeartbeat())
    }

    @Test
    fun realMessageIsNotHeartbeat() {
        val event = NewMessageEvent.newBuilder()
            .setChatId("7c9e6679-7425-40de-944b-e07fc1f90ae7")
            .setMessage(Shared.Message.newBuilder().setId(42).setSenderId(7))
            .build()

        assertFalse(event.isHeartbeat())
    }
}
