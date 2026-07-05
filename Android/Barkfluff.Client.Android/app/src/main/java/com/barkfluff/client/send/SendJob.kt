package com.barkfluff.client.send

import android.net.Uri
import com.barkfluff.client.editor.EditedVideoSpec
import java.io.File
import java.util.UUID

/**
 * Описание единицы вложения для отправки в чат.
 */
sealed class AttachmentSpec {
    /** Картинка с применёнными правками — байты JPEG лежат в кеше под этим ключом. */
    data class EditedImage(
        val cacheKey: String,
        val originalUri: Uri
    ) : AttachmentSpec()

    /** Картинка из галереи без правок — будет сжата ImageCompressor'ом. */
    data class RawImage(val uri: Uri) : AttachmentSpec()

    /** Видео с возможным трим/компресс. */
    data class Video(val spec: EditedVideoSpec) : AttachmentSpec()

    /** Документ (отправляется без сжатия как есть). */
    data class Document(val uri: Uri) : AttachmentSpec()

    /** Стикер из буфера обмена — байты передаются напрямую (WebP). */
    data class Sticker(val cacheKey: String) : AttachmentSpec()

    /** Голосовое сообщение — записанный OGG/Opus-файл во внутреннем кеше приложения. */
    data class Voice(val file: File) : AttachmentSpec()
}

/**
 * Задача отправки сообщения с вложениями в очередь MediaSendService.
 */
data class SendJob(
    val jobId: String = UUID.randomUUID().toString(),
    val chatId: String,
    val chatTitle: String,
    val text: String,
    val attachments: List<AttachmentSpec>,
    val replyId: Long = 0L,
    /** Если true — каждое вложение уходит отдельным сообщением (caption только в первом). */
    val sendSeparately: Boolean = false,
    /** Если true — все вложения отправляются как DOCUMENT (без сжатия и без VIDEO/IMAGE-преобразования). */
    val sendAsFile: Boolean = false,
    /**
     * Список localId — по одному на сообщение, которое будет создано (для оптимистичного UI чата).
     * Если sendSeparately=true → один localId на каждое attachment; иначе — один на job.
     * Пустой список = ChatActivity не подписан на оптимистичный UI (например, fast-forward без открытого чата).
     */
    val localIds: List<String> = emptyList()
)

/**
 * Внепроцессный кеш JPEG-байт картинок (отредактированных) и WebP-стикеров —
 * передавать ByteArray через Intent дорого/опасно (TransactionTooLarge).
 * Сервис читает по ключу.
 */
object SendPayloadCache {
    private val payloads: MutableMap<String, ByteArray> = mutableMapOf()

    @Synchronized
    fun put(bytes: ByteArray): String {
        val key = UUID.randomUUID().toString()
        payloads[key] = bytes
        return key
    }

    @Synchronized
    fun take(key: String): ByteArray? = payloads.remove(key)

    @Synchronized
    fun has(key: String): Boolean = payloads.containsKey(key)
}
