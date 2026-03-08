package com.barkfluff.client.data

/**
 * Менеджер для отслеживания текущего открытого чата.
 * Хранится только в оперативной памяти (не сохраняется при перезапуске приложения).
 * Используется для определения необходимости показа уведомлений.
 */
object OpenChatManager {

    @Volatile
    private var currentOpenChatId: String? = null

    /**
     * Устанавливает ID открытого чата.
     */
    fun setOpenChat(chatId: String?) {
        currentOpenChatId = chatId
    }

    /**
     * Проверяет, является ли указанный чат текущим открытым.
     */
    fun isOpen(chatId: String): Boolean {
        return currentOpenChatId == chatId
    }

    /**
     * Закрывает текущий открытый чат.
     */
    fun closeChat() {
        currentOpenChatId = null
    }

    /**
     * Получает текущий открытый чат.
     */
    fun getCurrentChatId(): String? {
        return currentOpenChatId
    }
}
