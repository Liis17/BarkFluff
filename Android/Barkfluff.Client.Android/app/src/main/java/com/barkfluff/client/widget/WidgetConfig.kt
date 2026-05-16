package com.barkfluff.client.widget

/**
 * Конфигурация одного App Widget'а: имя + список закреплённых chatId (до 3).
 */
data class WidgetConfig(
    val name: String,
    val chatIds: List<String>
) {
    companion object {
        const val MAX_CHATS = 3
    }
}
