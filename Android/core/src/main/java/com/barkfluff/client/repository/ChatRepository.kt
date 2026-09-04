package com.barkfluff.client.repository

import android.content.Context
import android.util.Log
import barkfluff.files.FilesApiOuterClass
import barkfluff.messages.MessagesApiOuterClass
import barkfluff.shared.Shared
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcApiTransport
import com.barkfluff.client.grpc.MediaHttpTransport
import java.io.File
import java.io.OutputStream
import java.util.concurrent.CancellationException
import java.util.concurrent.atomic.AtomicReference
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * Репозиторий для работы с чатами и сообщениями.
 * Инкапсулирует логику взаимодействия с gRPC Messages API.
 * Использует общий typed RPC transport из DI.
 */
class ChatRepository(
    private val context: Context,
    private val legacyTransport: GrpcApiTransport,
    private val mediaTransport: MediaHttpTransport = MediaHttpTransport(context),
) {

    companion object {
        private const val TAG = "ChatRepository"
        private const val DEFAULT_PAGE_SIZE = 30
    }

    private val globalParam = GlobalParam(context)

    /**
     * Загружает список сообщений чата с пагинацией.
     * @param chatId ID чата
     * @param fromMessageId ID сообщения, от которого начинается загрузка (0 для первой загрузки)
     * @param offsetBefore Количество сообщений для загрузки до fromMessageId (для подгрузки вверх)
     * @param offsetAfter Количество сообщений для загрузки после fromMessageId (для подгрузки вниз)
     * @param count Максимальное количество сообщений (max 50)
     */
    suspend fun loadMessages(
        chatId: String,
        fromMessageId: Long = 0L,
        offsetBefore: Int = 0,
        offsetAfter: Int = 0,
        count: Int = DEFAULT_PAGE_SIZE
    ): Result<List<Shared.Message>> = withContext(Dispatchers.IO) {
        try {
            if (legacyTransport.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val requestBuilder = MessagesApiOuterClass.ListMessagesRequest.newBuilder()
                .setChatId(chatId)

            if (fromMessageId > 0) {
                requestBuilder.fromMessageId = fromMessageId
            }

            if (offsetBefore > 0 || offsetAfter > 0) {
                // Загрузка относительно fromMessageId
                if (offsetBefore > 0) {
                    requestBuilder.offsetBefore = offsetBefore.coerceIn(0, 50)
                }
                if (offsetAfter > 0) {
                    requestBuilder.offsetAfter = offsetAfter.coerceIn(0, 50)
                }
            } else {
                // Если оба 0, используем count для обратной совместимости
                requestBuilder.count = count.coerceIn(1, 50)
            }

            val request = requestBuilder.build()
            val response = legacyTransport.messagesClient!!.listMessages(request)

            Log.d(TAG, "Loaded ${response.messagesList.size} messages for chat $chatId")
            Result.success(response.messagesList)
        } catch (e: Exception) {
            Log.e(TAG, "Error loading messages for chat $chatId", e)
            Result.failure(Exception("Ошибка загрузки сообщений: ${e.message}"))
        }
    }

    /**
     * Отправляет сообщение в чат.
     * @param chatId ID чата
     * @param text Текст сообщения
     * @param fileIds Список ID файлов для прикрепления
     * @param forwardedMessageId ID пересылаемого сообщения (0 = не пересылка). Используется и для reply, и для forward — backend различает по контексту.
     */
    /**
     * Ответ и пересылка — разные вещи и едут разными полями. Раньше оба шли одним
     * `forwarded_message_id`, и клиент угадывал, что перед ним, по наличию оригинала
     * в загруженной истории.
     */
    suspend fun sendMessage(
        chatId: String,
        text: String,
        fileIds: List<String> = emptyList(),
        replyToMessageId: Long = 0L,
        forwardedMessageIds: List<Long> = emptyList(),
        clientOperationId: String? = null
    ): Result<Shared.Message> = withContext(Dispatchers.IO) {
        try {
            if (legacyTransport.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val outgoingMessage = MessagesApiOuterClass.OutgoingMessage.newBuilder()
                .setText(text)
                .addAllFilesIds(fileIds)
                .setReplyToMessageId(replyToMessageId)
                .addAllForwardedMessageIds(forwardedMessageIds)
                .build()

            Log.d(TAG, "sendMessage: chatId=$chatId, textLength=${text.length}, fileIds=$fileIds, replyToMessageId=$replyToMessageId, forwardedMessageIds=$forwardedMessageIds")

            val requestBuilder = MessagesApiOuterClass.SendMessageRequest.newBuilder()
                .setChatId(chatId)
                .setMessage(outgoingMessage)
            clientOperationId?.takeIf { it.isNotBlank() }?.let(requestBuilder::setClientOperationId)
            val request = requestBuilder.build()

            Log.d(TAG, "sendMessage: request.message.filesIdsCount=${request.message.filesIdsCount}")

            val response = legacyTransport.messagesClient!!.sendMessage(request)
            Log.d(TAG, "Message sent to chat $chatId, id=${response.message.id}, attachments=${response.message.content.attachmentsList.size}")
            Result.success(response.message)
        } catch (e: Exception) {
            Log.e(TAG, "Error sending message to chat $chatId", e)
            // Keep the transport cause intact: the durable outbox distinguishes permanent
            // auth/access/validation errors from a retryable network failure.
            Result.failure(e)
        }
    }

    /**
     * Отмечает сообщения как прочитанные.
     */
    suspend fun markAsRead(messageIds: List<Long>): Result<Unit> {
        return legacyTransport.markAsRead(messageIds)
    }

    /**
     * Редактирует существующее сообщение.
     * @param messageId ID сообщения
     * @param text Новый текст
     * @param fileIds Идентификаторы файлов (forwarded-вложения backend сохраняет сам)
     */
    suspend fun editMessage(
        messageId: Long,
        text: String,
        fileIds: List<String> = emptyList()
    ): Result<Shared.Message> = withContext(Dispatchers.IO) {
        try {
            if (legacyTransport.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val request = MessagesApiOuterClass.EditMessageRequest.newBuilder()
                .setMessageId(messageId)
                .setText(text)
                .addAllFilesIds(fileIds)
                .build()

            val response = legacyTransport.messagesClient!!.editMessage(request)
            Log.d(TAG, "Message edited, id=${response.message.id}")
            Result.success(response.message)
        } catch (e: Exception) {
            Log.e(TAG, "Error editing message $messageId", e)
            Result.failure(Exception("Ошибка редактирования: ${e.message}"))
        }
    }

    /**
     * Удаляет сообщение (soft-delete на сервере).
     */
    suspend fun deleteMessage(messageId: Long): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            if (legacyTransport.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val request = MessagesApiOuterClass.DeleteMessageRequest.newBuilder()
                .setMessageId(messageId)
                .build()

            legacyTransport.messagesClient!!.deleteMessage(request)
            Log.d(TAG, "Message $messageId deleted")
            Result.success(Unit)
        } catch (e: Exception) {
            Log.e(TAG, "Error deleting message $messageId", e)
            Result.failure(Exception("Ошибка удаления: ${e.message}"))
        }
    }

    /**
     * Получает информацию о чате.
     */
    suspend fun getChatInfo(chatId: String): Result<ChatInfo> = withContext(Dispatchers.IO) {
        try {
            if (legacyTransport.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val request = MessagesApiOuterClass.GetChatInfoRequest.newBuilder()
                .setChatId(chatId)
                .build()

            val response = legacyTransport.messagesClient!!.getChatInfo(request)

            Result.success(
                ChatInfo(
                    chatId = chatId,
                    title = response.title,
                    pictureFileId = legacyTransport.extractGuidFromUrl(response.picture),
                    isGroupChat = response.isGroupChat,
                    lastMessageId = response.lastMessageId,
                    firstUnreadMessageId = response.firstUnreadMessageId,
                    countUnread = response.countUnread,
                    memberIds = response.membersIdList,
                    muted = response.muted
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Error getting chat info for $chatId", e)
            Result.failure(Exception("Ошибка получения информации о чате: ${e.message}"))
        }
    }

    suspend fun getChatDraft(chatId: String): Result<ChatDraft?> = withContext(Dispatchers.IO) {
        try {
            val client = legacyTransport.messagesClient
                ?: return@withContext Result.failure(IllegalStateException("Messages client not created"))
            val response = client.getChatDraft(
                MessagesApiOuterClass.GetChatDraftRequest.newBuilder().setChatId(chatId).build()
            )
            val draft = if (response.hasDraft()) response.draft.toChatDraft() else null
            Result.success(draft)
        } catch (e: Exception) {
            Log.e(TAG, "Error getting chat draft for $chatId", e)
            Result.failure(Exception("Ошибка получения черновика: ${e.message}"))
        }
    }

    suspend fun upsertChatDraft(chatId: String, text: String, replyToMessageId: Long): Result<ChatDraft> =
        withContext(Dispatchers.IO) {
            try {
                val client = legacyTransport.messagesClient
                    ?: return@withContext Result.failure(IllegalStateException("Messages client not created"))
                val response = client.upsertChatDraft(
                    MessagesApiOuterClass.UpsertChatDraftRequest.newBuilder()
                        .setChatId(chatId)
                        .setText(text)
                        .setReplyToMessageId(replyToMessageId)
                        .build()
                )
                Result.success(response.draft.toChatDraft())
            } catch (e: Exception) {
                Log.e(TAG, "Error saving chat draft for $chatId", e)
                Result.failure(Exception("Ошибка сохранения черновика: ${e.message}"))
            }
        }

    suspend fun deleteChatDraft(chatId: String, expectedRevision: String): Result<Boolean> =
        withContext(Dispatchers.IO) {
            try {
                val client = legacyTransport.messagesClient
                    ?: return@withContext Result.failure(IllegalStateException("Messages client not created"))
                val response = client.deleteChatDraft(
                    MessagesApiOuterClass.DeleteChatDraftRequest.newBuilder()
                        .setChatId(chatId)
                        .setExpectedRevision(expectedRevision)
                        .build()
                )
                Result.success(response.deleted)
            } catch (e: Exception) {
                Log.e(TAG, "Error deleting chat draft for $chatId", e)
                Result.failure(Exception("Ошибка удаления черновика: ${e.message}"))
            }
        }

    /**
     * Получает данные пользователя по ID.
     */
    suspend fun getUserData(userId: Long): Result<GrpcApiTransport.UserData> {
        return legacyTransport.getUserData(userId)
    }

    /**
     * Получает URL для скачивания файла.
     */
    suspend fun getFileDownloadUrl(fileId: String): Result<String> {
        return legacyTransport.getFileDownloadUrl(fileId)
    }

    /**
     * Получает URL для загрузки файла.
     */
    suspend fun getUploadUrl(
        fileType: FilesApiOuterClass.UploadFileType,
        clientOperationId: String? = null
    ): Result<UploadUrlResult> = withContext(Dispatchers.IO) {
        try {
            if (legacyTransport.filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files client not created"))
            }

            val requestBuilder = FilesApiOuterClass.GetUploadUrlRequest.newBuilder()
                .setFileType(fileType)
            clientOperationId?.takeIf { it.isNotBlank() }?.let(requestBuilder::setClientOperationId)
            val request = requestBuilder.build()

            val response = legacyTransport.filesClient!!.getUploadUrl(request)
            Result.success(UploadUrlResult(mediaTransport.rewrite(response.url), response.fileId))
        } catch (e: Exception) {
            Log.e(TAG, "Error getting upload URL", e)
            Result.failure(e)
        }
    }

    /**
     * Загружает файл на сервер.
     * @param jpegImageBytes Байты файла
     * @param fileType Тип файла
     * @param fileName Опциональное оригинальное имя файла (с расширением). Если задано вместе с mimeType — будут использованы вместо хардкод-значений.
     * @param mimeType Опциональный MIME-тип файла.
     * @param onProgress Колбек прогресса (0..100), вызывается из Dispatchers.IO во время записи тела запроса.
     */
    suspend fun uploadFile(
        jpegImageBytes: ByteArray,
        fileType: barkfluff.files.FilesApiOuterClass.UploadFileType,
        fileName: String? = null,
        mimeType: String? = null,
        onProgress: (Int) -> Unit = {},
        clientOperationId: String? = null,
        shouldCancel: () -> Boolean = { false }
    ): Result<String> = uploadFileInternal(
        source = UploadSource.Bytes(jpegImageBytes),
        fileType = fileType,
        fileName = fileName,
        mimeType = mimeType,
        onProgress = onProgress,
        clientOperationId = clientOperationId,
        shouldCancel = shouldCancel,
        uploadTarget = null
    )

    /** Streams a durable local file instead of loading it into process memory. */
    suspend fun uploadFile(
        file: File,
        fileType: FilesApiOuterClass.UploadFileType,
        fileName: String? = null,
        mimeType: String? = null,
        onProgress: (Int) -> Unit = {},
        clientOperationId: String? = null,
        shouldCancel: () -> Boolean = { false },
        /** Reuses the slot whose status was just checked by the durable outbox. */
        uploadTarget: UploadUrlResult? = null
    ): Result<String> {
        if (!file.isFile) return Result.failure(IllegalArgumentException("Upload source is unavailable"))
        return uploadFileInternal(
            source = UploadSource.FileSource(file),
            fileType = fileType,
            fileName = fileName,
            mimeType = mimeType,
            onProgress = onProgress,
            clientOperationId = clientOperationId,
            shouldCancel = shouldCancel,
            uploadTarget = uploadTarget
        )
    }

    /**
     * Reads a resumable upload slot while allowing the durable queue to cooperatively cancel
     * a blocked HTTP request instead of waiting for its socket timeout.
     */
    suspend fun getUploadStatus(
        uploadUrl: String,
        shouldCancel: () -> Boolean = { false }
    ): Result<UploadStatus?> = coroutineScope {
        val connectionRef = AtomicReference<java.net.HttpURLConnection?>(null)
        val request = async(Dispatchers.IO) {
            getUploadStatusBlocking(uploadUrl, shouldCancel, connectionRef)
        }
        val canceller = launch(Dispatchers.IO) {
            while (isActive && !request.isCompleted) {
                if (shouldCancel()) {
                    connectionRef.get()?.disconnect()
                    request.cancel(CancellationException("Outgoing upload status was cancelled"))
                    break
                }
                delay(100)
            }
        }
        try {
            request.await()
        } finally {
            canceller.cancel()
            connectionRef.get()?.disconnect()
        }
    }

    private fun getUploadStatusBlocking(
        uploadUrl: String,
        shouldCancel: () -> Boolean,
        connectionRef: AtomicReference<java.net.HttpURLConnection?>
    ): Result<UploadStatus?> {
        var connection: java.net.HttpURLConnection? = null
        return try {
            if (shouldCancel()) throw CancellationException("Outgoing upload status was cancelled")
            val statusUrl = uploadUrl.trimEnd('/') + "/status"
            connection = mediaTransport.openConnection(statusUrl)
            connectionRef.set(connection)
            connection.requestMethod = "GET"
            connection.connectTimeout = 30_000
            connection.readTimeout = 60_000
            val code = connection.responseCode
            if (shouldCancel()) throw CancellationException("Outgoing upload status was cancelled")
            if (code == java.net.HttpURLConnection.HTTP_NOT_FOUND) return Result.success(null)
            if (code !in 200..299) return Result.failure(UploadHttpException(code))
            val json = org.json.JSONObject(connection.inputStream.bufferedReader().readText())
            Result.success(
                UploadStatus(
                    state = json.optString("state"),
                    fileId = json.optString("fileId"),
                    retryAfterSeconds = json.optInt("retryAfterSeconds", 0)
                )
            )
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Result.failure(e)
        } finally {
            connection?.disconnect()
            connectionRef.compareAndSet(connection, null)
        }
    }

    private suspend fun uploadFileInternal(
        source: UploadSource,
        fileType: FilesApiOuterClass.UploadFileType,
        fileName: String?,
        mimeType: String?,
        onProgress: (Int) -> Unit,
        clientOperationId: String?,
        shouldCancel: () -> Boolean,
        uploadTarget: UploadUrlResult?
    ): Result<String> = withContext(Dispatchers.IO) {
        var connection: java.net.HttpURLConnection? = null
        try {
            if (legacyTransport.filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files client not created"))
            }

            // Дедупликация: считаем SHA-256 от тех самых байт, которые ушли бы на сервер
            // (для картинок это уже сжатый JPEG из ImageCompressor — совпадает с тем,
            // что хеширует backend в UploadFileCommandHandler).
            val fileHash = source.sha256Hex(shouldCancel)
            val existingFileId = legacyTransport.checkFileHash(fileHash).getOrNull()
            if (!existingFileId.isNullOrEmpty()) {
                Log.d(TAG, "File already exists on server (hash=$fileHash), reusing fileId: $existingFileId")
                try { onProgress(100) } catch (_: Throwable) {}
                return@withContext Result.success(existingFileId)
            }
            // На промахе или сетевой ошибке checkFileHash продолжаем обычный upload —
            // серверная пост-дедупликация всё равно отработает после полной загрузки.

            // The durable outbox may already have acquired this idempotent slot to inspect
            // its status. The HTTP body must target the same slot, not request a second one.
            val target = uploadTarget ?: run {
                val uploadUrlRequest = barkfluff.files.FilesApiOuterClass.GetUploadUrlRequest.newBuilder()
                    .setFileType(fileType)
                    .also { builder ->
                        clientOperationId?.takeIf { it.isNotBlank() }?.let(builder::setClientOperationId)
                    }
                    .build()
                val response = legacyTransport.filesClient!!.getUploadUrl(uploadUrlRequest)
                UploadUrlResult(mediaTransport.rewrite(response.url), response.fileId)
            }
            val fileId = target.fileId
            val uploadUrl = target.url

            Log.d(TAG, "Upload URL received, fileId: $fileId")

            // Выполняем HTTP POST multipart/form-data
            val boundary = "----BarkFluff${System.currentTimeMillis()}"
            connection = mediaTransport.openConnection(uploadUrl)

            connection.requestMethod = "POST"
            connection.setRequestProperty("Content-Type", "multipart/form-data; boundary=$boundary")
            connection.doOutput = true
            connection.connectTimeout = 30000
            connection.readTimeout = 60000

            // Определяем filename и Content-Type по типу загрузки
            val (defaultFileName, defaultContentType) = when (fileType) {
                barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_STICKER -> "sticker.webp" to "image/webp"
                barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT -> "file" to "application/octet-stream"
                barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_AUDIO -> "audio.mp3" to "audio/mpeg"
                barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_VOICE -> "voice.ogg" to "audio/ogg"
                barkfluff.files.FilesApiOuterClass.UploadFileType.MESSAGE_ATTACHMENT_VIDEO -> "video.mp4" to "video/mp4"
                else -> "file.jpg" to "image/jpeg"
            }
            val uploadFileName = fileName?.takeIf { it.isNotBlank() } ?: defaultFileName
            val uploadContentType = mimeType?.takeIf { it.isNotBlank() } ?: defaultContentType

            val header = (
                "--$boundary\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename=\"$uploadFileName\"\r\n" +
                "Content-Type: $uploadContentType\r\n" +
                "\r\n"
            ).toByteArray(Charsets.UTF_8)
            val footer = "\r\n--$boundary--\r\n".toByteArray(Charsets.UTF_8)

            // Фиксированная длина тела → HttpURLConnection пишет напрямую в сокет, без
            // внутреннего буфера. Без этого out.write() мгновенно уходит в память и прогресс
            // прыгает в 100% ещё до реальной отправки. С fixed-length onProgress отражает
            // действительную скорость загрузки на сервер.
            connection.setFixedLengthStreamingMode(
                header.size.toLong() + source.length + footer.size.toLong()
            )

            connection.outputStream.use { out ->
                out.write(header)
                source.writeTo(out, onProgress, shouldCancel)
                out.write(footer)
                out.flush()
            }

            val responseCode = connection.responseCode
            val responseBody = if (responseCode in 200..299) {
                connection.inputStream.bufferedReader().readText()
            } else {
                return@withContext Result.failure(UploadHttpException(responseCode))
            }

            // Сервер может вернуть другой fileId при дедупликации (тот же контент уже загружен)
            val actualFileId = try {
                val json = org.json.JSONObject(responseBody)
                json.optString("fileId", fileId)
            } catch (e: Exception) {
                fileId
            }

            Log.d(TAG, "File uploaded successfully, fileId: $actualFileId (original: $fileId)")
            Result.success(actualFileId)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.e(TAG, "Error uploading file", e)
            Result.failure(e)
        } finally {
            connection?.disconnect()
        }
    }

    /**
     * Получает список вложений в чате.
     */
    suspend fun getChatAttachments(
        chatId: String,
        attachmentType: barkfluff.shared.Shared.MessageAttachmentType = barkfluff.shared.Shared.MessageAttachmentType.MESSAGE_ATTACHMENT_TYPE_UNKNOWN,
        pageSize: Int = 100,
        fileNameQuery: String = ""
    ): Result<List<MessagesApiOuterClass.ChatAttachmentInfo>> = withContext(Dispatchers.IO) {
        try {
            if (legacyTransport.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val request = MessagesApiOuterClass.ListChatAttachmentsRequest.newBuilder()
                .setChatId(chatId)
                .setAttachmentType(attachmentType)
                .setSortDescending(true)
                .setFileNameQuery(fileNameQuery)
                .setPagination(
                    barkfluff.shared.Shared.PageRequest.newBuilder()
                        .setOffset(0)
                        .setSize(pageSize)
                        .build()
                )
                .build()

            val response = legacyTransport.messagesClient!!.listChatAttachments(request)
            Log.d(TAG, "Loaded ${response.attachmentsList.size} attachments for chat $chatId")
            Result.success(response.attachmentsList)
        } catch (e: Exception) {
            Log.e(TAG, "Error loading attachments for chat $chatId", e)
            Result.failure(Exception("Ошибка загрузки вложений: ${e.message}"))
        }
    }

    /**
     * Скачивает файл по fileId в кэш. Возвращает File или null при ошибке.
     * @param onProgress коллбек с прогрессом 0..100
     */
    suspend fun downloadFile(
        fileId: String,
        onProgress: (Int) -> Unit = {}
    ): java.io.File? = withContext(Dispatchers.IO) {
        try {
            val downloadUrl = getFileDownloadUrl(fileId).getOrNull()
                ?: return@withContext null

            val connection = mediaTransport.openConnection(downloadUrl)

            connection.connectTimeout = 30000
            connection.readTimeout = 60000
            connection.connect()

            val totalBytes = connection.contentLength.toLong()
            val buffer = ByteArray(8192)
            val outputStream = java.io.ByteArrayOutputStream()
            var bytesRead = 0L

            connection.inputStream.use { input ->
                var n: Int
                while (input.read(buffer).also { n = it } != -1) {
                    outputStream.write(buffer, 0, n)
                    bytesRead += n
                    if (totalBytes > 0) {
                        onProgress((bytesRead * 100L / totalBytes).toInt())
                    }
                }
            }

            com.barkfluff.client.utils.FileCache.saveFile(fileId, outputStream.toByteArray())
        } catch (e: Exception) {
            Log.e(TAG, "Error downloading file $fileId", e)
            null
        }
    }

    /**
     * No-op для совместимости. Каналы управляются GrpcClientRegistry.
     */
    fun close() {
        // Каналы управляются общим GrpcClientRegistry — не закрываем здесь
    }

    data class ChatInfo(
        val chatId: String,
        val title: String,
        val pictureFileId: String,
        val isGroupChat: Boolean,
        val lastMessageId: Long,
        val firstUnreadMessageId: Long,
        val countUnread: Long,
        val memberIds: List<Long>,
        val muted: Boolean = false
    )

    data class ChatDraft(
        val text: String,
        val replyToMessageId: Long,
        val revision: String,
        val updatedAtMillis: Long
    )

    private fun MessagesApiOuterClass.ChatDraftInfo.toChatDraft() = ChatDraft(
        text = text,
        replyToMessageId = replyToMessageId,
        revision = revision,
        updatedAtMillis = if (hasUpdatedAt()) updatedAt.seconds * 1000 else 0L
    )

    data class UploadUrlResult(
        val url: String,
        val fileId: String
    )

    data class UploadStatus(
        val state: String,
        val fileId: String,
        val retryAfterSeconds: Int
    )

    class UploadHttpException(val statusCode: Int) : Exception("Upload failed: HTTP $statusCode")
}

private fun ByteArray.sha256Hex(): String {
    val digest = java.security.MessageDigest.getInstance("SHA-256").digest(this)
    return digest.joinToString("") { "%02x".format(it) }
}

private sealed interface UploadSource {
    val length: Long
    suspend fun sha256Hex(shouldCancel: () -> Boolean): String
    suspend fun writeTo(output: OutputStream, onProgress: (Int) -> Unit, shouldCancel: () -> Boolean)

    data class Bytes(private val bytes: ByteArray) : UploadSource {
        override val length: Long get() = bytes.size.toLong()
        override suspend fun sha256Hex(shouldCancel: () -> Boolean): String {
            ensureUploadActive(shouldCancel)
            return bytes.sha256Hex()
        }

        override suspend fun writeTo(output: OutputStream, onProgress: (Int) -> Unit, shouldCancel: () -> Boolean) {
            var offset = 0
            var lastReported = -1
            while (offset < bytes.size) {
                ensureUploadActive(shouldCancel)
                val count = minOf(64 * 1024, bytes.size - offset)
                output.write(bytes, offset, count)
                output.flush()
                offset += count
                lastReported = reportUploadProgress(offset.toLong(), length, lastReported, onProgress)
            }
        }
    }

    data class FileSource(private val file: File) : UploadSource {
        override val length: Long get() = file.length()
        override suspend fun sha256Hex(shouldCancel: () -> Boolean): String = file.inputStream().use { input ->
            val digest = java.security.MessageDigest.getInstance("SHA-256")
            val buffer = ByteArray(64 * 1024)
            while (true) {
                ensureUploadActive(shouldCancel)
                val read = input.read(buffer)
                if (read < 0) break
                digest.update(buffer, 0, read)
            }
            digest.digest().joinToString("") { "%02x".format(it) }
        }

        override suspend fun writeTo(output: OutputStream, onProgress: (Int) -> Unit, shouldCancel: () -> Boolean) {
            file.inputStream().buffered().use { input ->
                val buffer = ByteArray(64 * 1024)
                var written = 0L
                var lastReported = -1
                while (true) {
                    ensureUploadActive(shouldCancel)
                    val read = input.read(buffer)
                    if (read < 0) break
                    output.write(buffer, 0, read)
                    output.flush()
                    written += read
                    lastReported = reportUploadProgress(written, length, lastReported, onProgress)
                }
            }
        }
    }
}

private suspend fun ensureUploadActive(shouldCancel: () -> Boolean) {
    currentCoroutineContext().ensureActive()
    if (shouldCancel()) throw CancellationException("Outgoing upload was cancelled")
}

private fun reportUploadProgress(
    written: Long,
    total: Long,
    lastReported: Int,
    onProgress: (Int) -> Unit
): Int {
    if (total <= 0) return lastReported
    val percent = (written * 100L / total).toInt()
    if (percent != lastReported) {
        onProgress(percent)
    }
    return percent
}
