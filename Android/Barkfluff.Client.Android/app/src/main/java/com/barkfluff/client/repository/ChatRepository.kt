package com.barkfluff.client.repository

import android.content.Context
import android.util.Log
import barkfluff.files.FilesApiOuterClass
import barkfluff.messages.MessagesApiOuterClass
import barkfluff.shared.Shared
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.security.cert.X509Certificate
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

/**
 * Репозиторий для работы с чатами и сообщениями.
 * Инкапсулирует логику взаимодействия с gRPC Messages API.
 * Использует общий GrpcManager из Application.
 */
class ChatRepository(private val context: Context, private val grpcManager: GrpcManager) {

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
            if (grpcManager.messagesClient == null) {
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
            val response = grpcManager.messagesClient!!.listMessages(request)

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
    suspend fun sendMessage(
        chatId: String,
        text: String,
        fileIds: List<String> = emptyList(),
        forwardedMessageId: Long = 0L
    ): Result<Shared.Message> = withContext(Dispatchers.IO) {
        try {
            if (grpcManager.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val outgoingMessage = MessagesApiOuterClass.OutgoingMessage.newBuilder()
                .setText(text)
                .addAllFilesIds(fileIds)
                .setForwardedMessageId(forwardedMessageId)
                .build()

            Log.d(TAG, "sendMessage: chatId=$chatId, text='$text', fileIds=$fileIds, forwardedMessageId=$forwardedMessageId")

            val request = MessagesApiOuterClass.SendMessageRequest.newBuilder()
                .setChatId(chatId)
                .setMessage(outgoingMessage)
                .build()

            Log.d(TAG, "sendMessage: request.message.filesIdsCount=${request.message.filesIdsCount}")

            val response = grpcManager.messagesClient!!.sendMessage(request)
            Log.d(TAG, "Message sent to chat $chatId, id=${response.message.id}, attachments=${response.message.content.attachmentsList.size}")
            Result.success(response.message)
        } catch (e: Exception) {
            Log.e(TAG, "Error sending message to chat $chatId", e)
            Result.failure(Exception("Ошибка отправки сообщения: ${e.message}"))
        }
    }

    /**
     * Отмечает сообщения как прочитанные.
     */
    suspend fun markAsRead(messageIds: List<Long>): Result<Unit> {
        return grpcManager.markAsRead(messageIds)
    }

    /**
     * Получает информацию о чате.
     */
    suspend fun getChatInfo(chatId: String): Result<ChatInfo> = withContext(Dispatchers.IO) {
        try {
            if (grpcManager.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val request = MessagesApiOuterClass.GetChatInfoRequest.newBuilder()
                .setChatId(chatId)
                .build()

            val response = grpcManager.messagesClient!!.getChatInfo(request)

            Result.success(
                ChatInfo(
                    chatId = chatId,
                    title = response.title,
                    pictureFileId = response.picture,
                    isGroupChat = response.isGroupChat,
                    lastMessageId = response.lastMessageId,
                    firstUnreadMessageId = response.firstUnreadMessageId,
                    countUnread = response.countUnread,
                    memberIds = response.membersIdList
                )
            )
        } catch (e: Exception) {
            Log.e(TAG, "Error getting chat info for $chatId", e)
            Result.failure(Exception("Ошибка получения информации о чате: ${e.message}"))
        }
    }

    /**
     * Получает данные пользователя по ID.
     */
    suspend fun getUserData(userId: Long): Result<GrpcManager.UserData> {
        return grpcManager.getUserData(userId)
    }

    /**
     * Получает URL для скачивания файла.
     */
    suspend fun getFileDownloadUrl(fileId: String): Result<String> {
        return grpcManager.getFileDownloadUrl(fileId)
    }

    /**
     * Получает URL для загрузки файла.
     */
    suspend fun getUploadUrl(fileType: FilesApiOuterClass.UploadFileType): Result<UploadUrlResult> = withContext(Dispatchers.IO) {
        try {
            if (grpcManager.filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files client not created"))
            }

            val request = FilesApiOuterClass.GetUploadUrlRequest.newBuilder()
                .setFileType(fileType)
                .build()

            val response = grpcManager.filesClient!!.getUploadUrl(request)
            Result.success(UploadUrlResult(response.url, response.fileId))
        } catch (e: Exception) {
            Log.e(TAG, "Error getting upload URL", e)
            Result.failure(Exception("Ошибка получения URL загрузки: ${e.message}"))
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
        onProgress: (Int) -> Unit = {}
    ): Result<String> = withContext(Dispatchers.IO) {
        try {
            if (grpcManager.filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files client not created"))
            }

            // Дедупликация: считаем SHA-256 от тех самых байт, которые ушли бы на сервер
            // (для картинок это уже сжатый JPEG из ImageCompressor — совпадает с тем,
            // что хеширует backend в UploadFileCommandHandler).
            val fileHash = jpegImageBytes.sha256Hex()
            val existingFileId = grpcManager.checkFileHash(fileHash).getOrNull()
            if (!existingFileId.isNullOrEmpty()) {
                Log.d(TAG, "File already exists on server (hash=$fileHash), reusing fileId: $existingFileId")
                try { onProgress(100) } catch (_: Throwable) {}
                return@withContext Result.success(existingFileId)
            }
            // На промахе или сетевой ошибке checkFileHash продолжаем обычный upload —
            // серверная пост-дедупликация всё равно отработает после полной загрузки.

            // Получаем URL для загрузки
            val uploadUrlRequest = barkfluff.files.FilesApiOuterClass.GetUploadUrlRequest.newBuilder()
                .setFileType(fileType)
                .build()

            val uploadUrlResponse = grpcManager.filesClient!!.getUploadUrl(uploadUrlRequest)
            val fileId = uploadUrlResponse.fileId
            val uploadUrl = uploadUrlResponse.url

            Log.d(TAG, "Upload URL received, fileId: $fileId, url: $uploadUrl")

            // Выполняем HTTP POST multipart/form-data
            val boundary = "----BarkFluff${System.currentTimeMillis()}"
            val url = java.net.URL(uploadUrl)
            val connection = url.openConnection() as java.net.HttpURLConnection

            // Если HTTPS — применяем trust-all для самоподписанного сертификата
            if (connection is HttpsURLConnection) {
                val trustManager = object : X509TrustManager {
                    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
                    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {}
                    override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
                }
                val sslContext = SSLContext.getInstance("TLS")
                sslContext.init(null, arrayOf<TrustManager>(trustManager), null)
                connection.sslSocketFactory = sslContext.socketFactory
                connection.hostnameVerifier = javax.net.ssl.HostnameVerifier { _, _ -> true }
            }

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

            connection.outputStream.use { out ->
                val writer = out.bufferedWriter()
                writer.write("--$boundary\r\n")
                writer.write("Content-Disposition: form-data; name=\"file\"; filename=\"$uploadFileName\"\r\n")
                writer.write("Content-Type: $uploadContentType\r\n")
                writer.write("\r\n")
                writer.flush()

                // Записываем тело чанками, чтобы отслеживать прогресс
                val total = jpegImageBytes.size
                if (total > 0) {
                    val chunk = 64 * 1024
                    var written = 0
                    var lastReported = -1
                    while (written < total) {
                        val len = minOf(chunk, total - written)
                        out.write(jpegImageBytes, written, len)
                        written += len
                        val pct = (written.toLong() * 100L / total.toLong()).toInt()
                        if (pct != lastReported) {
                            try { onProgress(pct) } catch (_: Throwable) {}
                            lastReported = pct
                        }
                    }
                }
                out.flush()
                writer.write("\r\n")
                writer.write("--$boundary--\r\n")
                writer.flush()
            }

            val responseCode = connection.responseCode
            val responseBody = if (responseCode in 200..299) {
                connection.inputStream.bufferedReader().readText()
            } else {
                connection.disconnect()
                return@withContext Result.failure(Exception("Upload failed: HTTP $responseCode"))
            }
            connection.disconnect()

            // Сервер может вернуть другой fileId при дедупликации (тот же контент уже загружен)
            val actualFileId = try {
                val json = org.json.JSONObject(responseBody)
                json.optString("fileId", fileId)
            } catch (e: Exception) {
                fileId
            }

            Log.d(TAG, "File uploaded successfully, fileId: $actualFileId (original: $fileId)")
            Result.success(actualFileId)
        } catch (e: Exception) {
            Log.e(TAG, "Error uploading file", e)
            Result.failure(Exception("Ошибка загрузки файла: ${e.message}"))
        }
    }

    /**
     * Получает список вложений в чате.
     */
    suspend fun getChatAttachments(
        chatId: String,
        attachmentType: barkfluff.shared.Shared.MessageAttachmentType = barkfluff.shared.Shared.MessageAttachmentType.MESSAGE_ATTACHMENT_TYPE_UNKNOWN,
        pageSize: Int = 100
    ): Result<List<MessagesApiOuterClass.ChatAttachmentInfo>> = withContext(Dispatchers.IO) {
        try {
            if (grpcManager.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val request = MessagesApiOuterClass.ListChatAttachmentsRequest.newBuilder()
                .setChatId(chatId)
                .setAttachmentType(attachmentType)
                .setSortDescending(true)
                .setPagination(
                    barkfluff.shared.Shared.PageRequest.newBuilder()
                        .setOffset(0)
                        .setSize(pageSize)
                        .build()
                )
                .build()

            val response = grpcManager.messagesClient!!.listChatAttachments(request)
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

            val url = java.net.URL(downloadUrl)
            val connection = url.openConnection() as java.net.HttpURLConnection

            if (connection is HttpsURLConnection) {
                val trustManager = object : X509TrustManager {
                    override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
                    override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {}
                    override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
                }
                val sslContext = SSLContext.getInstance("TLS")
                sslContext.init(null, arrayOf<TrustManager>(trustManager), null)
                connection.sslSocketFactory = sslContext.socketFactory
                connection.hostnameVerifier = javax.net.ssl.HostnameVerifier { _, _ -> true }
            }

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
     * No-op для совместимости. Каналы управляются GrpcManager.
     */
    fun close() {
        // Каналы управляются общим GrpcManager — не закрываем здесь
    }

    data class ChatInfo(
        val chatId: String,
        val title: String,
        val pictureFileId: String,
        val isGroupChat: Boolean,
        val lastMessageId: Long,
        val firstUnreadMessageId: Long,
        val countUnread: Long,
        val memberIds: List<Long>
    )

    data class UploadUrlResult(
        val url: String,
        val fileId: String
    )
}

private fun ByteArray.sha256Hex(): String {
    val digest = java.security.MessageDigest.getInstance("SHA-256").digest(this)
    return digest.joinToString("") { "%02x".format(it) }
}
