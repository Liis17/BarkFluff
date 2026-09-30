package com.barkfluff.client.chat

import barkfluff.shared.Shared
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.test.runCurrent
import org.junit.Test

class MessageNavigatorTest {
    @Test fun loadsContextAroundTheRequestedMessage() = runTest {
        var requestedId = 0L
        val navigator = MessageNavigator { id, before, after ->
            requestedId = id
            assertEquals(20, before)
            assertEquals(20, after)
            Result.success(listOf(Shared.Message.newBuilder().setId(id).build()))
        }
        val target = navigator.request(900L)
        val window = navigator.window(target)!!.getOrThrow()
        assertEquals(900L, requestedId)
        assertEquals(900L, window.single().id)
    }

    @Test fun waitsForCommittedTargetAndAcknowledgesOnlyThatRequest() = runTest {
        val navigator = MessageNavigator { id, _, _ -> Result.success(listOf(message(id))) }
        val first = navigator.request(9)
        navigator.window(first)
        assertNull(navigator.position(first, listOf(1, 2, 3)))
        assertEquals(first, navigator.pending)
        assertEquals(1, navigator.position(first, listOf(1, 9, 10)))
        assertTrue(navigator.acknowledge(first.requestId))
        assertNull(navigator.pending)

        val second = navigator.request(10)
        assertFalse(navigator.acknowledge(first.requestId))
        assertEquals(second, navigator.pending)
        assertNull(navigator.position(first, listOf(9, 10)))
    }

    @Test fun discardsLateContextFromPreviousNavigation() = runTest {
        val deferred = CompletableDeferred<Result<List<Shared.Message>>>()
        val navigator = MessageNavigator { _, _, _ -> deferred.await() }
        val first = navigator.request(9)
        val oldWindow = async { navigator.window(first) }
        runCurrent()
        val second = navigator.request(10)
        deferred.complete(Result.success(listOf(message(9))))
        assertNull(oldWindow.await())
        assertEquals(second, navigator.pending)
    }

    @Test fun missingMessageIsFailureEvenWhenServerReturnsNeighbours() = runTest {
        val navigator = MessageNavigator { _, _, _ -> Result.success(listOf(message(8), message(10))) }
        val target = navigator.request(9)
        assertTrue(navigator.window(target)!!.exceptionOrNull() is MessageTargetMissingException)
    }

    @Test fun sortsContextByTimestampThenId() = runTest {
        val navigator = MessageNavigator { _, _, _ -> Result.success(listOf(message(10), message(9), message(8))) }
        val target = navigator.request(9)
        assertEquals(listOf(8L, 9L, 10L), navigator.window(target)!!.getOrThrow().map { it.id })
    }

    private fun message(id: Long) = Shared.Message.newBuilder().setId(id).build()
}
