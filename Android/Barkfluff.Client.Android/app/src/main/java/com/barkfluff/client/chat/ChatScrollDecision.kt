package com.barkfluff.client.chat

/**
 * Чистое решение об автопрокрутке обычного чата при подаче нового списка строк.
 *
 * Вызывается до submitList: [committed] — закоммиченный снимок адаптера
 * (messageAdapter.currentList), [next] — новые строки состояния. Возвращает,
 * нужно ли прокручивать к разделителю непрочитанных и/или к низу списка.
 * Держит в одном месте инвариант: автопрокрутка вниз допустима только при
 * первом наполнении или реальном добавлении сообщений за прежним хвостом,
 * а не при любом росте числа строк.
 */
internal data class ChatScrollDecision(
    /** Разделитель непрочитанных появился в [next] относительно закоммиченного снимка. */
    val unreadAppeared: Boolean,
    /** Единственный случай прокрутки вниз: первое наполнение или новый хвост по правилу якоря. */
    val scrollToBottom: Boolean,
) {
    companion object {
        fun decide(
            committed: List<MessageItem>,
            next: List<MessageItem>,
            currentUserId: Long,
            wasAtBottom: Boolean,
        ): ChatScrollDecision = ChatScrollDecision(
            unreadAppeared = next.any { it.type == MessageType.UNREAD_SEPARATOR } &&
                committed.none { it.type == MessageType.UNREAD_SEPARATOR },
            scrollToBottom = shouldScrollToBottom(committed, next, currentUserId, wasAtBottom),
        )

        /**
         * Автопрокрутка вниз допустима только когда за прежним контентным хвостом
         * появились новые строки. Идентичность — тот же тип и совпадающий непустой
         * localId (подтверждение оптимистичного сообщения меняет серверный id,
         * сохраняя localId) или совпадающий messageId. Структурные строки
         * (даты, разделитель непрочитанных, FOOTER) хвостом не считаются.
         */
        private fun shouldScrollToBottom(
            committed: List<MessageItem>,
            next: List<MessageItem>,
            currentUserId: Long,
            wasAtBottom: Boolean,
        ): Boolean {
            val oldTailIndex = committed.indexOfLast(::isContentRow)
            val newTailIndex = next.indexOfLast(::isContentRow)
            // Первое наполнение пустого списка — единственный случай без прежнего хвоста.
            if (oldTailIndex == -1) return newTailIndex != -1
            if (newTailIndex == -1) return false

            val oldTail = committed[oldTailIndex]
            // Прежний хвост обязан остаться в новом списке: иначе это удаление или
            // замена, которые нельзя принимать за добавление в хвост.
            val tailMatchIndex = next.indexOfLast { sameLogicalMessage(it, oldTail) }
            if (tailMatchIndex !in 0 until next.lastIndex) return false

            // За прежним хвостом должна быть хотя бы одна новая контентная строка,
            // отсутствующая в закоммиченном списке; перестановки старых строк не считаются.
            val hasNewContentBeyondOldTail = (tailMatchIndex + 1..next.lastIndex).any { index ->
                val row = next[index]
                isContentRow(row) && committed.none { isContentRow(it) && sameLogicalMessage(it, row) }
            }
            if (!hasNewContentBeyondOldTail) return false

            val newTail = next[newTailIndex]
            val ownNewTail = newTail.type == MessageType.MESSAGE && newTail.senderId == currentUserId
            return ownNewTail || wasAtBottom
        }

        private fun isContentRow(row: MessageItem): Boolean =
            row.type == MessageType.MESSAGE || row.type == MessageType.SYSTEM

        private fun sameLogicalMessage(a: MessageItem, b: MessageItem): Boolean {
            if (a.type != b.type) return false
            val localA = a.localId
            val localB = b.localId
            if (localA != null && localA == localB) return true
            return a.messageId == b.messageId
        }
    }
}
