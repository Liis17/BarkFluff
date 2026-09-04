package com.barkfluff.client.send

import android.net.Uri
import com.barkfluff.client.editor.EditedVideoSpec
import java.io.File

/**
 * Описание единицы вложения для отправки в чат.
 */
sealed class AttachmentSpec {
    /** Картинка с применёнными правками; bytes are staged before this request is accepted. */
    data class EditedImage(
        val originalUri: Uri,
        /** Copied into app-private outbox storage before enqueue returns. */
        val bytes: ByteArray
    ) : AttachmentSpec()

    /** Картинка из галереи без правок — будет сжата ImageCompressor'ом. */
    data class RawImage(val uri: Uri) : AttachmentSpec()

    /** Видео с возможным трим/компресс. */
    data class Video(val spec: EditedVideoSpec) : AttachmentSpec()

    /** Документ (отправляется без сжатия как есть). */
    data class Document(val uri: Uri) : AttachmentSpec()

    /** Стикер из буфера обмена — байты передаются напрямую (WebP). */
    data class Sticker(val bytes: ByteArray) : AttachmentSpec()

    /** Голосовое сообщение — записанный OGG/Opus-файл во внутреннем кеше приложения. */
    data class Voice(val file: File) : AttachmentSpec()
}

/**
 * Запрос на durable staging в [OutgoingMessageQueue].
 */
data class SendJob(
    val chatId: String,
    val chatTitle: String,
    val text: String,
    val attachments: List<AttachmentSpec>,
    val replyId: Long = 0L,
    /** Если true — каждое вложение уходит отдельным сообщением (caption только в первом). */
    val sendSeparately: Boolean = false,
    /** Если true — все вложения отправляются как DOCUMENT (без сжатия и без VIDEO/IMAGE-преобразования). */
    val sendAsFile: Boolean = false,
    /** File ids that already exist on the server (for example, a sticker pack item). */
    val existingFileIds: List<String> = emptyList(),
    /** Generation of the draft represented by this send; cleared only after its ACK. */
    val draftGeneration: Long? = null
)
