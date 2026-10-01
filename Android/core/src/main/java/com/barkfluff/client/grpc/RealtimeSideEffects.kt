package com.barkfluff.client.grpc

import barkfluff.updates.UpdatesApiOuterClass

/**
 * App-слойные побочные эффекты [RealtimeService]: уведомления и обновление виджетов.
 *
 * Модуль :core не зависит от UI/Notification/Widget, поэтому всё, что требует
 * Activity/NotificationManager/AppWidget или загрузку bitmap (Coil), вынесено сюда.
 * Каждый app-модуль предоставляет свою реализацию; V2 на этапе MVP передаёт null.
 */
interface RealtimeSideEffects {

    /** Чат изменился (новое/изменённое/удалённое сообщение) — обновить превью виджетов. */
    fun onChatChanged(chatId: String)

    /** Сообщения чата прочитаны — убрать уведомления чата из шторки. */
    fun dismissChatNotifications(chatId: String, messageId: Long = 0)

    /**
     * Показать уведомление о новом входящем сообщении.
     * Реализация сама решает, показывать ли (открыт ли чат, включены ли уведомления,
     * не своё ли сообщение), резолвит отправителя и грузит аватар/превью.
     */
    suspend fun showMessageNotification(event: UpdatesApiOuterClass.NewMessageEvent)

    /** Metadata-only markers: the app must not decrypt message contents to display them. */
    suspend fun showPrivateMessageNotification(
        chatId: String, messageId: Long, senderUserId: Long, expectedScopeId: String? = null
    ) {}

    suspend fun showSecretMessageNotification(
        messageId: String, senderUserId: Long, senderDeviceId: String, expectedScopeId: String? = null
    ) {}
}
