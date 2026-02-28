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
import java.net.HttpURLConnection
import java.net.URL
import java.security.cert.X509Certificate
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager

/**
 * Репозиторий для работы с чатами и сообщениями.
 * Инкапсулирует логику взаимодействия с gRPC Messages API.
 */
class ChatRepository(private val context: Context) {

    companion object {
        private const val TAG = "ChatRepository"
        private const val DEFAULT_PAGE_SIZE = 30
    }

    private val globalParam = GlobalParam(context)
    private var grpcManager: GrpcManager? = null

    /**
     * Инициализирует gRPC клиенты, если они еще не созданы.
     */
    fun ensureInitialized() {
        if (grpcManager != null) return

        grpcManager = GrpcManager().also { mgr ->
            val identityAddress = globalParam.socketIdentity
            val usersAddress = globalParam.socketUsers
            val filesAddress = globalParam.socketFiles
            val messagesAddress = globalParam.socketMessages

            if (identityAddress.isNotBlank()) {
                mgr.createIdentityClient(identityAddress, context, includeDeviceInfo = true)
            }
            if (usersAddress.isNotBlank()) {
                mgr.createUsersClient(usersAddress, context, includeDeviceInfo = true)
            }
            if (filesAddress.isNotBlank()) {
                mgr.createFilesClient(filesAddress, context, includeDeviceInfo = true)
            }
            if (messagesAddress.isNotBlank()) {
                mgr.createMessagesClient(messagesAddress, context, includeDeviceInfo = true)
            }
        }
        Log.d(TAG, "ChatRepository initialized with gRPC clients")
    }

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
            ensureInitialized()
            val mgr = grpcManager ?: return@withContext Result.failure(IllegalStateException("GrpcManager not initialized"))

            if (mgr.messagesClient == null) {
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
            val response = mgr.messagesClient!!.listMessages(request)

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
     */
    suspend fun sendMessage(
        chatId: String,
        text: String,
        fileIds: List<String> = emptyList()
    ): Result<Shared.Message> = withContext(Dispatchers.IO) {
        try {
            ensureInitialized()
            val mgr = grpcManager ?: return@withContext Result.failure(IllegalStateException("GrpcManager not initialized"))

            if (mgr.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val outgoingMessage = MessagesApiOuterClass.OutgoingMessage.newBuilder()
                .setText(text)
                .addAllFilesIds(fileIds)
                .build()

            val request = MessagesApiOuterClass.SendMessageRequest.newBuilder()
                .setChatId(chatId)
                .setMessage(outgoingMessage)
                .build()

            val response = mgr.messagesClient!!.sendMessage(request)
            Log.d(TAG, "Message sent to chat $chatId, id=${response.message.id}")
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
        ensureInitialized()
        return grpcManager?.markAsRead(messageIds) ?: Result.failure(IllegalStateException("GrpcManager not initialized"))
    }

    /**
     * Получает информацию о чате.
     */
    suspend fun getChatInfo(chatId: String): Result<ChatInfo> = withContext(Dispatchers.IO) {
        try {
            ensureInitialized()
            val mgr = grpcManager ?: return@withContext Result.failure(IllegalStateException("GrpcManager not initialized"))

            if (mgr.messagesClient == null) {
                return@withContext Result.failure(IllegalStateException("Messages client not created"))
            }

            val request = MessagesApiOuterClass.GetChatInfoRequest.newBuilder()
                .setChatId(chatId)
                .build()

            val response = mgr.messagesClient!!.getChatInfo(request)

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
        return grpcManager?.getUserData(userId) ?: Result.failure(IllegalStateException("GrpcManager not initialized"))
    }

    /**
     * Получает URL для скачивания файла.
     */
    suspend fun getFileDownloadUrl(fileId: String): Result<String> {
        ensureInitialized()
        return grpcManager?.getFileDownloadUrl(fileId) ?: Result.failure(IllegalStateException("GrpcManager not initialized"))
    }

    /**
     * Получает URL для загрузки файла.
     */
    suspend fun getUploadUrl(fileType: FilesApiOuterClass.UploadFileType): Result<UploadUrlResult> = withContext(Dispatchers.IO) {
        try {
            ensureInitialized()
            val mgr = grpcManager ?: return@withContext Result.failure(IllegalStateException("GrpcManager not initialized"))

            if (mgr.filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files client not created"))
            }

            val request = FilesApiOuterClass.GetUploadUrlRequest.newBuilder()
                .setFileType(fileType)
                .build()

            val response = mgr.filesClient!!.getUploadUrl(request)
            Result.success(UploadUrlResult(response.url, response.fileId))
        } catch (e: Exception) {
            Log.e(TAG, "Error getting upload URL", e)
            Result.failure(Exception("Ошибка получения URL загрузки: ${e.message}"))
        }
    }

    /**
     * Загружает файл на сервер.
     * @param jpegImageBytes Байты изображения в формате JPEG
     * @param fileType Тип файла
     */
    suspend fun uploadFile(jpegImageBytes: ByteArray, fileType: barkfluff.files.FilesApiOuterClass.UploadFileType): Result<String> = withContext(Dispatchers.IO) {
        try {
            ensureInitialized()
            val mgr = grpcManager ?: return@withContext Result.failure(IllegalStateException("GrpcManager not initialized"))

            if (mgr.filesClient == null) {
                return@withContext Result.failure(IllegalStateException("Files client not created"))
            }

            // Получаем URL для загрузки
            val uploadUrlRequest = barkfluff.files.FilesApiOuterClass.GetUploadUrlRequest.newBuilder()
                .setFileType(fileType)
                .build()

            val uploadUrlResponse = mgr.filesClient!!.getUploadUrl(uploadUrlRequest)
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

            connection.outputStream.use { out ->
                val writer = out.bufferedWriter()
                writer.write("--$boundary\r\n")
                writer.write("Content-Disposition: form-data; name=\"file\"; filename=\"file.jpg\"\r\n")
                writer.write("Content-Type: image/jpeg\r\n")
                writer.write("\r\n")
                writer.flush()
                out.write(jpegImageBytes)
                out.flush()
                writer.write("\r\n")
                writer.write("--$boundary--\r\n")
                writer.flush()
            }

            val responseCode = connection.responseCode
            connection.disconnect()

            if (responseCode !in 200..299) {
                return@withContext Result.failure(Exception("Upload failed: HTTP $responseCode"))
            }

            Log.d(TAG, "File uploaded successfully, fileId: $fileId")
            Result.success(fileId)
        } catch (e: Exception) {
            Log.e(TAG, "Error uploading file", e)
            Result.failure(Exception("Ошибка загрузки файла: ${e.message}"))
        }
    }

    /**
     * Освобождает ресурсы.
     */
    fun close() {
        grpcManager?.shutdown()
        grpcManager = null
        Log.d(TAG, "ChatRepository closed")
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
