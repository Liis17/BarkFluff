package com.barkfluff.client.di

import com.barkfluff.client.domain.gateway.MessageGateway
import com.barkfluff.client.send.OutgoingMessageQueue
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.components.SingletonComponent

/**
 * Narrow Hilt entry points for Android components that the framework constructs directly.
 * Keeping one method per component avoids resurrecting an application-level service locator.
 */
@EntryPoint
@InstallIn(SingletonComponent::class)
interface OutgoingQueueEntryPoint {
    fun outgoingMessageQueue(): OutgoingMessageQueue
}

@EntryPoint
@InstallIn(SingletonComponent::class)
interface MessageGatewayEntryPoint {
    fun messageGateway(): MessageGateway
}
