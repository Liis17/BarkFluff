package com.barkfluff.client.adapter

/**
 * Pure row projection shared by regular, pinned and E2E consumers. It owns structural rows
 * (deduplication and the terminal spacer), while [MessageAdapter] only binds the result.
 */
class MessageRowProjector {
    fun project(rows: List<MessageItem>, includeFooter: Boolean = true): List<MessageItem> {
        val seen = HashSet<Long>()
        val deduplicated = rows.filter { row ->
            row.type != MessageType.MESSAGE && row.type != MessageType.SYSTEM || seen.add(row.messageId)
        }
        if (!includeFooter) return deduplicated.toList()
        return deduplicated.filterNot { it.type == MessageType.FOOTER } + MessageItem.createFooter()
    }

    fun withSelection(rows: List<MessageItem>, selectedIds: Set<Long>, enabled: Boolean): List<MessageItem> =
        rows.map { row ->
            if (row.type != MessageType.MESSAGE) row
            else row.copy(
                selectionEnabled = enabled,
                isSelected = row.messageId in selectedIds,
            )
        }
}
