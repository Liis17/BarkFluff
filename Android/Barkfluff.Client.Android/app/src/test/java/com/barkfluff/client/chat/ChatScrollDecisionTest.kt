package com.barkfluff.client.chat

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * Регрессия прыжка к новым сообщениям при быстрой прокрутке вверх: решение должно
 * сравнивать логические идентичности и порядок контента с закоммиченным снимком
 * адаптера, а не рост числа строк. Смотрите [ChatScrollDecision].
 */
class ChatScrollDecisionTest {
    private val ownUserId = 7L
    private val peerId = 3L

    @Test fun delayedHistoryPrependWithOwnTailDoesNotScrollToBottom() {
        val committed = listOf(
            dateSeparator(),
            message(100, peerId),
            message(101, peerId),
            message(102, ownUserId),
        )
        val nextWithPrependedHistory = listOf(
            dateSeparator("older"),
            message(90, peerId),
            message(91, peerId),
            dateSeparator(),
            message(100, peerId),
            message(101, peerId),
            message(102, ownUserId),
        )
        val decision = decide(committed, nextWithPrependedHistory, wasAtBottom = false)

        assertFalse(decision.scrollToBottom)
        assertFalse(decision.unreadAppeared)
    }

    @Test fun delayedHistoryPrependWithForeignTailDoesNotScrollEvenAtBottom() {
        val committed = listOf(dateSeparator(), message(100, ownUserId), message(101, peerId))
        val nextWithPrependedHistory = listOf(
            dateSeparator("older"),
            message(95, peerId),
            dateSeparator(),
            message(100, ownUserId),
            message(101, peerId),
        )

        assertFalse(decide(committed, nextWithPrependedHistory, wasAtBottom = true).scrollToBottom)
    }

    @Test fun repeatedPageOrDuplicatesDoNotScrollToBottom() {
        val committed = listOf(dateSeparator(), message(100, peerId), message(101, ownUserId))

        val sameContent = decide(committed, committed, wasAtBottom = false)
        assertFalse(sameContent.scrollToBottom)

        val withDuplicateRows = committed + message(101, ownUserId)
        assertFalse(decide(committed, withDuplicateRows, wasAtBottom = false).scrollToBottom)
    }

    @Test fun structuralRowGrowthAloneDoesNotScrollToBottom() {
        val committed = listOf(dateSeparator(), message(100, peerId), message(101, ownUserId))

        val withUnreadSeparator = committed + MessageItem.createUnreadSeparator("unread")
        val unreadDecision = decide(committed, withUnreadSeparator, wasAtBottom = false)
        assertFalse(unreadDecision.scrollToBottom)
        assertTrue(unreadDecision.unreadAppeared)

        val withDateSeparator = listOf(dateSeparator("older")) + committed
        assertFalse(decide(committed, withDateSeparator, wasAtBottom = false).scrollToBottom)

        val withFooter = committed + MessageItem.createFooter()
        assertFalse(decide(committed, withFooter, wasAtBottom = false).scrollToBottom)
    }

    @Test fun initialContentLoadScrollsToBottom() {
        val content = listOf(dateSeparator(), message(100, peerId), message(101, ownUserId))

        assertTrue(decide(emptyList(), content, wasAtBottom = true).scrollToBottom)
        // Пустое состояние адаптера — это projected-список из одного FOOTER.
        assertTrue(decide(listOf(MessageItem.createFooter()), content, wasAtBottom = true).scrollToBottom)
        assertFalse(decide(emptyList(), emptyList(), wasAtBottom = true).scrollToBottom)
    }

    @Test fun genuineOwnAppendScrollsToBottomEvenAwayFromBottom() {
        val committed = listOf(dateSeparator(), message(100, peerId))
        val appended = listOf(dateSeparator(), message(100, peerId), message(101, ownUserId))

        assertTrue(decide(committed, appended, wasAtBottom = false).scrollToBottom)
    }

    @Test fun foreignAppendScrollsOnlyWhenAtBottom() {
        val committed = listOf(dateSeparator(), message(100, ownUserId))
        val appended = listOf(dateSeparator(), message(100, ownUserId), message(101, peerId))

        assertTrue(decide(committed, appended, wasAtBottom = true).scrollToBottom)
        assertFalse(decide(committed, appended, wasAtBottom = false).scrollToBottom)
    }

    @Test fun confirmationAloneDoesNotScrollToBottom() {
        val committed = listOf(
            message(100, peerId),
            message(-10_000_000_000L, ownUserId, localId = "op-1"),
        )
        // Подтверждение меняет серверный id, сохраняя localId оптимистичной строки.
        val confirmed = listOf(
            message(100, peerId),
            message(500, ownUserId, localId = "op-1"),
        )

        assertFalse(decide(committed, confirmed, wasAtBottom = false).scrollToBottom)

        // Правка того же сообщения (статус/текст) — та же идентичность, без прокрутки.
        val edited = listOf(
            message(100, peerId),
            message(-10_000_000_000L, ownUserId, localId = "op-1", text = "edited"),
        )
        assertFalse(decide(committed, edited, wasAtBottom = false).scrollToBottom)
    }

    @Test fun confirmationCoalescedWithGenuineAppendStillScrollsToBottom() {
        val committed = listOf(
            message(100, peerId),
            message(-10_000_000_000L, ownUserId, localId = "op-1"),
        )
        val coalescedOwnAppend = listOf(
            message(100, peerId),
            message(500, ownUserId, localId = "op-1"),
            message(600, ownUserId),
        )
        assertTrue(decide(committed, coalescedOwnAppend, wasAtBottom = false).scrollToBottom)

        val coalescedForeignAppend = listOf(
            message(100, peerId),
            message(500, ownUserId, localId = "op-1"),
            message(600, peerId),
        )
        assertTrue(decide(committed, coalescedForeignAppend, wasAtBottom = true).scrollToBottom)
        assertFalse(decide(committed, coalescedForeignAppend, wasAtBottom = false).scrollToBottom)
    }

    @Test fun supersededSubmissionCoalescingKeepsUnreadNavigationAndTailRule() {
        // A (введённая между снапшотами) с разделителем и новым хвостом superseded-ится
        // обновлением B; commit приходит только для B, поэтому решение обязано
        // покрывать переход committed -> B одним шагом.
        val committed = listOf(message(100, peerId), message(101, ownUserId))
        val coalescedB = listOf(
            message(100, peerId),
            message(101, ownUserId),
            MessageItem.createUnreadSeparator("unread"),
            message(102, peerId),
        )
        val away = decide(committed, coalescedB, wasAtBottom = false)
        assertTrue(away.unreadAppeared)
        assertFalse(away.scrollToBottom)

        val coalescedOwnTailB = listOf(
            message(100, peerId),
            message(101, ownUserId),
            MessageItem.createUnreadSeparator("unread"),
            message(102, ownUserId),
        )
        val own = decide(committed, coalescedOwnTailB, wasAtBottom = false)
        assertTrue(own.unreadAppeared)
        assertTrue(own.scrollToBottom)
    }

    @Test fun tailDeletionOrReplacementIsNotTreatedAsAppend() {
        val committed = listOf(message(100, peerId), message(101, ownUserId), message(102, peerId))

        val tailDeleted = listOf(message(100, peerId), message(101, ownUserId))
        assertFalse(decide(committed, tailDeleted, wasAtBottom = true).scrollToBottom)

        val tailReplaced = listOf(message(100, peerId), message(101, ownUserId), message(999, peerId))
        assertFalse(decide(committed, tailReplaced, wasAtBottom = true).scrollToBottom)

        val allContentGone = listOf<MessageItem>()
        assertFalse(decide(committed, allContentGone, wasAtBottom = true).scrollToBottom)
    }

    @Test fun systemRowCountsAsContentTail() {
        val committed = listOf(message(100, peerId), system(900))
        val appended = listOf(message(100, peerId), system(900), message(101, peerId))

        assertTrue(decide(committed, appended, wasAtBottom = true).scrollToBottom)
        assertFalse(decide(committed, appended, wasAtBottom = false).scrollToBottom)
    }

    private fun decide(
        committed: List<MessageItem>,
        next: List<MessageItem>,
        wasAtBottom: Boolean,
    ) = ChatScrollDecision.decide(
        committed = committed,
        next = next,
        currentUserId = ownUserId,
        wasAtBottom = wasAtBottom,
    )

    private fun message(
        id: Long,
        senderId: Long,
        localId: String? = null,
        text: String = "m$id",
    ) = MessageItem(
        messageId = id,
        senderId = senderId,
        text = text,
        timestamp = id,
        attachments = emptyList(),
        localId = localId,
    )

    private fun system(id: Long) = MessageItem(
        messageId = id,
        senderId = 0,
        text = "sys$id",
        timestamp = id,
        attachments = emptyList(),
        type = MessageType.SYSTEM,
    )

    private fun dateSeparator(label: String = "today") = MessageItem.createDateSeparator(label)
}
