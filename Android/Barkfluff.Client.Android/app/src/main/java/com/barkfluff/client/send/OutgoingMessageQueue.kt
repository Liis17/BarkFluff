package com.barkfluff.client.send

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import androidx.media3.common.MediaItem
import androidx.media3.effect.Presentation
import androidx.media3.transformer.Composition
import androidx.media3.transformer.EditedMediaItem
import androidx.media3.transformer.ExportException
import androidx.media3.transformer.ExportResult
import androidx.media3.transformer.Effects
import androidx.media3.transformer.Transformer
import androidx.work.Constraints
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import barkfluff.files.FilesApiOuterClass.UploadFileType
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.cache.OutgoingAttachmentKind
import com.barkfluff.client.cache.OutgoingAttachmentRecord
import com.barkfluff.client.cache.OutgoingFailureCategory
import com.barkfluff.client.cache.OutgoingMessageRecord
import com.barkfluff.client.cache.OutgoingMessageState
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.repository.ChatRepository.UploadHttpException
import com.barkfluff.client.utils.ImageCompressor
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.Dispatchers
import java.io.File
import java.io.InputStream
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.math.roundToInt

typealias OperationId = String

/** UI-safe view of one durable outgoing operation. Paths point only into app-private storage. */
data class OutgoingMessageSnapshot(
    val operationId: OperationId,
    val chatId: String,
    val text: String,
    val replyToMessageId: Long,
    val createdAtMillis: Long,
    val draftGeneration: Long?,
    val state: OutgoingMessageState,
    val progress: Int,
    val previewPaths: List<String>,
    val failureCategory: OutgoingFailureCategory?,
    val serverMessageId: Long
)

/**
 * SQLCipher-backed source of truth for normal-chat sends. A message becomes user-visible only
 * after all input has been copied below noBackupFilesDir and its state has reached QUEUED.
 */
class OutgoingMessageQueue(
    private val context: Context,
    private val cache: ChatCacheRepository,
    private val chatRepository: ChatRepository,
    private val grpcManager: GrpcManager
) {
    companion object {
        private const val LEASE_MILLIS = 10 * 60 * 1000L
        private const val SENT_RETENTION_MILLIS = 24 * 60 * 60 * 1000L
        private val drainMutex = Mutex()
        private val cancellationRequests = ConcurrentHashMap<String, Long>()
    }

    private val appContext = context.applicationContext
    private val workManager by lazy { WorkManager.getInstance(appContext) }

    suspend fun enqueue(request: SendJob): List<OperationId> {
        require(request.chatId.isNotBlank()) { "Chat id is required" }
        val scope = requireScope()
        val groups = if (request.sendSeparately && request.attachments.isNotEmpty()) {
            request.attachments.mapIndexed { index, attachment ->
                SendPart(
                    text = if (index == 0) request.text else "",
                    attachments = listOf(attachment),
                    existingFileIds = if (index == 0) request.existingFileIds else emptyList()
                )
            }
        } else {
            listOf(SendPart(request.text, request.attachments, request.existingFileIds))
        }
        val batchId = if (groups.size > 1) UUID.randomUUID().toString() else null
        val operationIds = mutableListOf<OperationId>()
        try {
            groups.forEach { part ->
                operationIds += stageAndQueue(scope, request, part, batchId)
            }
        } catch (e: Throwable) {
            operationIds.forEach { cancel(it) }
            throw e
        }
        wake()
        return operationIds
    }

    fun observeChat(chatId: String): Flow<List<OutgoingMessageSnapshot>> {
        val scope = currentScopeOrNull() ?: return kotlinx.coroutines.flow.flowOf(emptyList())
        return cache.observeOutgoing(scope, chatId).map { records -> records.map(::snapshotOf) }
    }

    suspend fun retry(operationId: OperationId) {
        val scope = requireScope()
        val record = cache.outgoing(scope, operationId) ?: return
        if (record.state != OutgoingMessageState.FAILED) return
        cache.saveOutgoing(scope, record.copy(
            state = OutgoingMessageState.QUEUED,
            progress = 0,
            nextAttemptAtMillis = 0,
            failureCategory = null,
            failureDetail = null,
            leaseOwner = null,
            leaseExpiresAtMillis = 0
        ))
        wake()
    }

    /** Cancellation is local only: a server message already committed can still reappear in history. */
    suspend fun cancel(operationId: OperationId) {
        val scope = currentScopeOrNull() ?: return
        val record = cache.outgoing(scope, operationId) ?: return
        cancellationRequests[operationId] = System.currentTimeMillis()
        cache.saveOutgoing(scope, record.copy(state = OutgoingMessageState.CANCEL_REQUESTED))
        cache.deleteOutgoing(scope, operationId)
        operationDirectory(scope, operationId).deleteRecursively()
    }

    /** Called on app start and after a network becomes available. */
    fun resume() {
        wake()
    }

    suspend fun cancelAllForCurrentScope() {
        val scope = currentScopeOrNull() ?: return
        val now = System.currentTimeMillis()
        cache.outgoingOperationIds(scope).forEach { operationId ->
            cancellationRequests[operationId] = now
        }
        workManager.cancelAllWorkByTag(workTag(scope))
        cache.clearOutgoing(scope)
        scopeDirectory(scope).deleteRecursively()
    }

    /** Worker seam. It drains at most two different chat heads at one time. */
    suspend fun processReady(onForeground: suspend (OutgoingMessageSnapshot) -> Unit = {}) {
        drainMutex.withLock {
            val scope = currentScopeOrNull() ?: return
            val now = System.currentTimeMillis()
            cache.discardStagingOutgoing(scope)
            cleanupOrphanDirectories(scope)
            cancellationRequests.forEach { (operationId, requestedAt) ->
                if (requestedAt < now - 60_000L) cancellationRequests.remove(operationId, requestedAt)
            }
            cache.recoverExpiredOutgoingLeases(scope, now)
            cache.oldSentOutgoing(scope, now - SENT_RETENTION_MILLIS).forEach { record ->
                cache.deleteOutgoing(scope, record.operationId)
                operationDirectory(scope, record.operationId).deleteRecursively()
            }

            val ready = cache.readyOutgoing(scope, now, limit = 2)
            coroutineScope {
                ready.map { record -> async { process(scope, record, onForeground) } }.awaitAll()
            }
            scheduleNext(scope)
        }
    }

    private suspend fun stageAndQueue(
        scope: CacheScope,
        request: SendJob,
        part: SendPart,
        batchId: String?
    ): OperationId {
        val operationId = UUID.randomUUID().toString()
        val now = System.currentTimeMillis()
        val staging = OutgoingMessageRecord(
            operationId = operationId,
            batchId = batchId,
            chatId = request.chatId,
            chatTitle = request.chatTitle,
            text = part.text,
            replyToMessageId = request.replyId,
            draftGeneration = request.draftGeneration,
            sendAsFile = request.sendAsFile,
            existingFileIds = part.existingFileIds,
            createdAtMillis = now,
            state = OutgoingMessageState.STAGING,
            progress = 0,
            attemptCount = 0,
            nextAttemptAtMillis = 0,
            failureCategory = null,
            failureDetail = null,
            leaseOwner = null,
            leaseExpiresAtMillis = 0,
            serverMessageId = 0,
            serverMessagePayload = null,
            attachments = emptyList()
        )
        cache.saveOutgoing(scope, staging)
        val directory = operationDirectory(scope, operationId).apply { mkdirs() }
        return try {
            val attachments = withContext(Dispatchers.IO) {
                part.attachments.mapIndexed { index, spec -> stageAttachment(directory, index, spec, request.sendAsFile) }
            }
            cache.saveOutgoing(scope, staging.copy(
                state = OutgoingMessageState.QUEUED,
                attachments = attachments
            ))
            operationId
        } catch (e: Throwable) {
            cache.deleteOutgoing(scope, operationId)
            directory.deleteRecursively()
            throw e
        }
    }

    private suspend fun process(
        scope: CacheScope,
        initial: OutgoingMessageRecord,
        onForeground: suspend (OutgoingMessageSnapshot) -> Unit
    ) {
        var record = cache.outgoing(scope, initial.operationId) ?: return
        val leaseOwner = UUID.randomUUID().toString()
        fun leased(state: OutgoingMessageState) = record.copy(
            state = state,
            leaseOwner = leaseOwner,
            leaseExpiresAtMillis = System.currentTimeMillis() + LEASE_MILLIS
        )
        try {
            ensureNotCancelled(record.operationId)
            record = leased(OutgoingMessageState.PREPARING)
            cache.saveOutgoing(scope, record)
            onForeground(snapshotOf(record))

            if (!grpcManager.ensureTokenValid(appContext)) {
                throw PermanentOutgoingException(OutgoingFailureCategory.AUTH_REQUIRED, "Token refresh failed")
            }

            for (index in record.attachments.indices) {
                ensureNotCancelled(record.operationId)
                record = cache.outgoing(scope, record.operationId) ?: return
                var attachment = record.attachments[index]
                if (!attachment.finalFileId.isNullOrBlank()) continue
                val prepared = prepareAttachment(record, attachment)
                if (prepared != attachment) {
                    attachment = prepared
                    record = record.withAttachment(index, attachment)
                    cache.saveOutgoing(scope, record)
                }

                record = leased(OutgoingMessageState.UPLOADING).withAttachment(index, attachment)
                cache.saveOutgoing(scope, record)
                onForeground(snapshotOf(record))
                val file = File(attachment.preparedPath ?: attachment.sourcePath)
                if (!file.isFile) {
                    throw PermanentOutgoingException(OutgoingFailureCategory.SOURCE_UNAVAILABLE, "Staged attachment is missing")
                }
                val type = UploadFileType.forNumber(attachment.uploadFileTypeNumber)
                    ?: UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT
                val uploadUrl = chatRepository.getUploadUrl(type, attachment.uploadOperationId).getOrElse { throw it }
                val status = chatRepository.getUploadStatus(
                    uploadUrl = uploadUrl.url,
                    shouldCancel = { cancellationRequests.containsKey(record.operationId) }
                ).getOrElse { throw it }
                val completedId = status?.takeIf {
                    it.state.equals("completed", ignoreCase = true) || it.state.equals("complete", ignoreCase = true)
                }?.fileId?.takeIf(String::isNotBlank)
                if (completedId == null && status?.state.equals("processing", ignoreCase = true)) {
                    throw RetryOutgoingException((status?.retryAfterSeconds ?: 0) * 1_000L)
                }
                var lastPersistedProgress = -1
                val finalId = completedId ?: chatRepository.uploadFile(
                    file = file,
                    fileType = type,
                    fileName = attachment.fileName,
                    mimeType = attachment.mimeType,
                    clientOperationId = attachment.uploadOperationId,
                    shouldCancel = { cancellationRequests.containsKey(record.operationId) },
                    uploadTarget = uploadUrl,
                    onProgress = { attachmentProgress ->
                        val rounded = attachmentProgress.coerceIn(0, 100) / 5 * 5
                        if (rounded != lastPersistedProgress) {
                            lastPersistedProgress = rounded
                            // HttpURLConnection invokes this callback synchronously. Persisting only
                            // 5%-steps keeps the progress recoverable after process death without a
                            // storm of SQL writes or keeping media bytes in memory.
                            runBlocking {
                                cache.outgoing(scope, record.operationId)?.let { active ->
                                    cache.saveOutgoing(scope, active.copy(progress = uploadingProgress(active, rounded)))
                                }
                            }
                        }
                    }
                ).getOrElse { throw it }
                attachment = attachment.copy(
                    reservedFileId = uploadUrl.fileId,
                    finalFileId = finalId
                )
                record = (cache.outgoing(scope, record.operationId) ?: return).withAttachment(index, attachment)
                    .copy(progress = completedProgress(record.withAttachment(index, attachment)))
                cache.saveOutgoing(scope, record)
            }

            record = cache.outgoing(scope, record.operationId) ?: return
            ensureNotCancelled(record.operationId)
            record = leased(OutgoingMessageState.SENDING).copy(progress = 100)
            cache.saveOutgoing(scope, record)
            onForeground(snapshotOf(record))
            val fileIds = record.existingFileIds + record.attachments.sortedBy { it.attachmentIndex }
                .mapNotNull { it.finalFileId?.takeIf(String::isNotBlank) }
            val sent = chatRepository.sendMessage(
                chatId = record.chatId,
                text = record.text,
                fileIds = fileIds,
                replyToMessageId = record.replyToMessageId,
                clientOperationId = record.operationId
            ).getOrElse { throw it }
            ensureNotCancelled(record.operationId)
            cache.saveMessages(scope, record.chatId, listOf(sent))
            record = record.copy(
                state = OutgoingMessageState.SENT,
                serverMessageId = sent.id,
                serverMessagePayload = sent.toByteArray(),
                leaseOwner = null,
                leaseExpiresAtMillis = 0,
                failureCategory = null,
                failureDetail = null
            )
            cache.saveOutgoing(scope, record)
            operationDirectory(scope, record.operationId).deleteRecursively()
        } catch (e: CancellationException) {
            if (cancellationRequests.containsKey(initial.operationId)) return
            throw e
        } catch (e: Throwable) {
            val latest = cache.outgoing(scope, initial.operationId) ?: return
            val permanent = classifyPermanent(e)
            if (permanent != null) {
                cache.saveOutgoing(scope, latest.copy(
                    state = OutgoingMessageState.FAILED,
                    failureCategory = permanent,
                    failureDetail = safeDetail(e),
                    leaseOwner = null,
                    leaseExpiresAtMillis = 0
                ))
            } else {
                val attempt = latest.attemptCount + 1
                val retryAfter = (e as? RetryOutgoingException)?.minimumDelayMillis ?: 0L
                cache.saveOutgoing(scope, latest.copy(
                    state = OutgoingMessageState.QUEUED,
                    attemptCount = attempt,
                    nextAttemptAtMillis = System.currentTimeMillis() + maxOf(
                        OutgoingRetryPolicy.delayForAttempt(attempt),
                        retryAfter
                    ),
                    failureCategory = OutgoingFailureCategory.NETWORK,
                    failureDetail = safeDetail(e),
                    leaseOwner = null,
                    leaseExpiresAtMillis = 0
                ))
            }
        }
    }

    private suspend fun prepareAttachment(
        record: OutgoingMessageRecord,
        attachment: OutgoingAttachmentRecord
    ): OutgoingAttachmentRecord {
        if (!attachment.preparedPath.isNullOrBlank()) return attachment
        val source = File(attachment.sourcePath)
        if (!source.isFile) throw PermanentOutgoingException(OutgoingFailureCategory.SOURCE_UNAVAILABLE, "Staged attachment is missing")
        if ((attachment.kind == OutgoingAttachmentKind.RAW_IMAGE || attachment.kind == OutgoingAttachmentKind.EDITED_IMAGE) &&
            !record.sendAsFile &&
            attachment.uploadFileTypeNumber == UploadFileType.MESSAGE_ATTACHMENT_IMAGE.number
        ) {
            val output = File(source.parentFile, "prepared_${attachment.attachmentIndex}.jpg")
            val compressed = withContext(Dispatchers.IO) {
                ImageCompressor.compressImage(Uri.fromFile(source), appContext).getOrElse {
                    throw PermanentOutgoingException(OutgoingFailureCategory.UNKNOWN, "Image preparation failed")
                }
            }
            ensureNotCancelled(record.operationId)
            output.outputStream().use { it.write(compressed) }
            return attachment.copy(
                preparedPath = output.absolutePath,
                fileName = "image.jpg",
                mimeType = "image/jpeg"
            )
        }
        if (attachment.kind != OutgoingAttachmentKind.VIDEO ||
            (attachment.trimStartMs <= 0 && attachment.trimEndMs <= 0 && !attachment.compressTo480p)
        ) {
            return attachment.copy(preparedPath = source.absolutePath)
        }
        val output = File(source.parentFile, "prepared_${attachment.attachmentIndex}.mp4")
        if (!transformVideo(source, attachment, output, record.operationId)) {
            throw PermanentOutgoingException(OutgoingFailureCategory.UNKNOWN, "Video preparation failed")
        }
        return attachment.copy(
            preparedPath = output.absolutePath,
            fileName = attachment.fileName?.let { it.substringBeforeLast('.', it) + ".mp4" } ?: "video.mp4",
            mimeType = "video/mp4"
        )
    }

    private suspend fun transformVideo(
        source: File,
        attachment: OutgoingAttachmentRecord,
        output: File,
        operationId: String
    ): Boolean = suspendCancellableCoroutine { continuation ->
        val mediaItemBuilder = MediaItem.Builder().setUri(Uri.fromFile(source))
        if (attachment.trimStartMs > 0 || attachment.trimEndMs > 0) {
            val clip = MediaItem.ClippingConfiguration.Builder()
                .setStartPositionMs(attachment.trimStartMs.coerceAtLeast(0))
            if (attachment.trimEndMs > 0) clip.setEndPositionMs(attachment.trimEndMs)
            clip.setStartsAtKeyFrame(false)
            mediaItemBuilder.setClippingConfiguration(clip.build())
        }
        val editedBuilder = EditedMediaItem.Builder(mediaItemBuilder.build())
        if (attachment.compressTo480p) {
            editedBuilder.setEffects(Effects(emptyList(), listOf(Presentation.createForHeight(480))))
        }
        val transformer = Transformer.Builder(appContext).build()
        val watcher = CoroutineScope(Dispatchers.Default).launch {
            while (continuation.isActive) {
                delay(200)
                if (cancellationRequests.containsKey(operationId)) {
                    transformer.cancel()
                    continuation.cancel(CancellationException("Outgoing video preparation was cancelled"))
                }
            }
        }
        transformer.addListener(object : Transformer.Listener {
            override fun onCompleted(composition: Composition, exportResult: ExportResult) {
                watcher.cancel()
                if (continuation.isActive) continuation.resume(true)
            }

            override fun onError(
                composition: Composition,
                exportResult: ExportResult,
                exportException: ExportException
            ) {
                watcher.cancel()
                output.delete()
                if (continuation.isActive) continuation.resume(false)
            }
        })
        continuation.invokeOnCancellation {
            watcher.cancel()
            transformer.cancel()
            output.delete()
        }
        try {
            transformer.start(editedBuilder.build(), output.absolutePath)
        } catch (error: Throwable) {
            watcher.cancel()
            output.delete()
            if (continuation.isActive) continuation.resumeWithException(error)
        }
    }

    private fun stageAttachment(
        directory: File,
        index: Int,
        spec: AttachmentSpec,
        sendAsFile: Boolean
    ): OutgoingAttachmentRecord {
        val (kind, source, fileName, mime, type, trimStart, trimEnd, compress) = when (spec) {
            is AttachmentSpec.RawImage -> {
                val mime = appContext.contentResolver.getType(spec.uri)
                val name = displayName(spec.uri) ?: "image_$index"
                val file = File(directory, "source_$index${extension(name, mime)}")
                copyUri(spec.uri, file)
                val type = when {
                    sendAsFile -> UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT
                    mime == "image/webp" -> UploadFileType.MESSAGE_ATTACHMENT_STICKER
                    mime?.startsWith("video/") == true -> UploadFileType.MESSAGE_ATTACHMENT_VIDEO
                    else -> UploadFileType.MESSAGE_ATTACHMENT_IMAGE
                }
                StagedAttachmentInput(OutgoingAttachmentKind.RAW_IMAGE, file, name, mime, type, 0L, -1L, false)
            }
            is AttachmentSpec.EditedImage -> {
                val file = File(directory, "source_$index.jpg")
                file.outputStream().use { it.write(spec.bytes) }
                StagedAttachmentInput(
                    OutgoingAttachmentKind.EDITED_IMAGE, file, "image.jpg", "image/jpeg",
                    if (sendAsFile) UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT else UploadFileType.MESSAGE_ATTACHMENT_IMAGE,
                    0L, -1L, false
                )
            }
            is AttachmentSpec.Video -> {
                val mime = appContext.contentResolver.getType(spec.spec.uri) ?: "video/mp4"
                val name = displayName(spec.spec.uri) ?: "video.mp4"
                val file = File(directory, "source_$index${extension(name, mime)}")
                copyUri(spec.spec.uri, file)
                StagedAttachmentInput(
                    OutgoingAttachmentKind.VIDEO, file, name, mime,
                    if (sendAsFile) UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT else UploadFileType.MESSAGE_ATTACHMENT_VIDEO,
                    spec.spec.trimStartMs, spec.spec.trimEndMs, spec.spec.compressTo480p
                )
            }
            is AttachmentSpec.Document -> {
                val mime = appContext.contentResolver.getType(spec.uri) ?: "application/octet-stream"
                val name = displayName(spec.uri) ?: "file"
                val file = File(directory, "source_$index${extension(name, mime)}")
                copyUri(spec.uri, file)
                StagedAttachmentInput(OutgoingAttachmentKind.DOCUMENT, file, name, mime, UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT, 0L, -1L, false)
            }
            is AttachmentSpec.Sticker -> {
                val file = File(directory, "source_$index.webp")
                file.outputStream().use { it.write(spec.bytes) }
                StagedAttachmentInput(OutgoingAttachmentKind.STICKER, file, "sticker.webp", "image/webp", UploadFileType.MESSAGE_ATTACHMENT_STICKER, 0L, -1L, false)
            }
            is AttachmentSpec.Voice -> {
                val file = File(directory, "source_$index.ogg")
                spec.file.inputStream().use { input -> file.outputStream().use { input.copyTo(it) } }
                StagedAttachmentInput(OutgoingAttachmentKind.VOICE, file, "voice.ogg", "audio/ogg", UploadFileType.MESSAGE_ATTACHMENT_VOICE, 0L, -1L, false)
            }
        }
        return OutgoingAttachmentRecord(
            attachmentIndex = index,
            kind = kind,
            uploadFileTypeNumber = type.number,
            sourcePath = source.absolutePath,
            preparedPath = null,
            previewPath = if (kind == OutgoingAttachmentKind.RAW_IMAGE || kind == OutgoingAttachmentKind.EDITED_IMAGE || kind == OutgoingAttachmentKind.VIDEO) source.absolutePath else null,
            fileName = fileName,
            mimeType = mime,
            uploadOperationId = UUID.randomUUID().toString(),
            reservedFileId = null,
            finalFileId = null,
            trimStartMs = trimStart,
            trimEndMs = trimEnd,
            compressTo480p = compress
        )
    }

    private fun copyUri(uri: Uri, destination: File) {
        val input = appContext.contentResolver.openInputStream(uri)
            ?: throw IllegalStateException("Cannot open attachment")
        input.use { source -> destination.outputStream().use(source::copyTo) }
    }

    private fun displayName(uri: Uri): String? = runCatching {
        appContext.contentResolver.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0) else null
        }
    }.getOrNull()

    private fun extension(name: String?, mime: String?): String {
        val suffix = name?.substringAfterLast('.', "")?.takeIf { it.isNotBlank() }
        if (suffix != null) return ".${suffix.take(12)}"
        return when {
            mime?.startsWith("image/") == true -> ".jpg"
            mime?.startsWith("video/") == true -> ".mp4"
            else -> ""
        }
    }

    private suspend fun scheduleNext(scope: CacheScope) {
        val next = cache.nextOutgoingAttempt(scope) ?: return
        val delay = (next - System.currentTimeMillis()).coerceAtLeast(0)
        val request = OneTimeWorkRequestBuilder<OutgoingMessageWorker>()
            .setConstraints(networkConstraints())
            .setInitialDelay(delay, java.util.concurrent.TimeUnit.MILLISECONDS)
            .addTag(workTag(scope))
            .build()
        workManager.enqueueUniqueWork(retryWorkName(scope), ExistingWorkPolicy.REPLACE, request)
    }

    private fun wake() {
        val scope = currentScopeOrNull() ?: return
        val request = OneTimeWorkRequestBuilder<OutgoingMessageWorker>()
            .setConstraints(networkConstraints())
            .addTag(workTag(scope))
            .build()
        workManager.enqueueUniqueWork(nowWorkName(scope), ExistingWorkPolicy.KEEP, request)
    }

    private fun networkConstraints() = Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()

    private suspend fun cleanupOrphanDirectories(scope: CacheScope) {
        val known = cache.outgoingOperationIds(scope)
        scopeDirectory(scope).listFiles()?.filter(File::isDirectory)?.forEach { directory ->
            if (directory.name !in known) directory.deleteRecursively()
        }
    }

    private fun snapshotOf(record: OutgoingMessageRecord) = OutgoingMessageSnapshot(
        operationId = record.operationId,
        chatId = record.chatId,
        text = record.text,
        replyToMessageId = record.replyToMessageId,
        createdAtMillis = record.createdAtMillis,
        draftGeneration = record.draftGeneration,
        state = record.state,
        progress = record.progress,
        previewPaths = record.attachments.mapNotNull { it.previewPath },
        failureCategory = record.failureCategory,
        serverMessageId = record.serverMessageId
    )

    private fun completedProgress(record: OutgoingMessageRecord): Int = when {
        record.attachments.isEmpty() -> 100
        else -> ((record.attachments.count { !it.finalFileId.isNullOrBlank() }.toDouble() / record.attachments.size) * 100).roundToInt()
    }

    private fun uploadingProgress(record: OutgoingMessageRecord, attachmentPercent: Int): Int {
        if (record.attachments.isEmpty()) return attachmentPercent
        val done = record.attachments.count { !it.finalFileId.isNullOrBlank() }
        return (((done * 100) + attachmentPercent).toDouble() / record.attachments.size)
            .roundToInt().coerceIn(0, 99)
    }

    private fun OutgoingMessageRecord.withAttachment(index: Int, replacement: OutgoingAttachmentRecord) = copy(
        attachments = attachments.mapIndexed { attachmentIndex, attachment ->
            if (attachmentIndex == index) replacement else attachment
        }
    )

    private fun classifyPermanent(error: Throwable): OutgoingFailureCategory? {
        if (error is PermanentOutgoingException) return error.category
        val httpCode = (error as? UploadHttpException)?.statusCode
        if (httpCode != null) return when (httpCode) {
            408, 409, 429 -> null
            401 -> OutgoingFailureCategory.AUTH_REQUIRED
            403 -> OutgoingFailureCategory.ACCESS
            in 400..499 -> OutgoingFailureCategory.VALIDATION
            else -> null
        }
        val message = error.message.orEmpty().lowercase()
        return when {
            "unauthenticated" in message || "unauthorized" in message -> OutgoingFailureCategory.AUTH_REQUIRED
            "permission" in message || "forbidden" in message -> OutgoingFailureCategory.ACCESS
            "invalid" in message || "validation" in message -> OutgoingFailureCategory.VALIDATION
            else -> null
        }
    }

    private fun safeDetail(error: Throwable): String = error::class.java.simpleName.take(120)

    private fun ensureNotCancelled(operationId: String) {
        if (cancellationRequests.containsKey(operationId)) {
            throw CancellationException("Outgoing operation was cancelled")
        }
    }

    private fun currentScopeOrNull() = CacheScope.from(GlobalParam(appContext))
    private fun requireScope() = requireNotNull(currentScopeOrNull()) { "No active server/account scope" }
    private fun scopeDirectory(scope: CacheScope) = File(appContext.noBackupFilesDir, "outgoing/${sha256(scope.id)}")
    private fun operationDirectory(scope: CacheScope, operationId: String) = File(scopeDirectory(scope), operationId)
    private fun nowWorkName(scope: CacheScope) = "outgoing-now-${sha256(scope.id)}"
    private fun retryWorkName(scope: CacheScope) = "outgoing-retry-${sha256(scope.id)}"
    private fun workTag(scope: CacheScope) = "outgoing-${sha256(scope.id)}"
    private fun sha256(value: String) = MessageDigest.getInstance("SHA-256").digest(value.toByteArray()).joinToString("") { "%02x".format(it) }

    private data class SendPart(
        val text: String,
        val attachments: List<AttachmentSpec>,
        val existingFileIds: List<String>
    )

    private data class StagedAttachmentInput(
        val kind: OutgoingAttachmentKind,
        val source: File,
        val fileName: String?,
        val mime: String?,
        val type: UploadFileType,
        val trimStart: Long,
        val trimEnd: Long,
        val compress: Boolean
    )

    private class PermanentOutgoingException(
        val category: OutgoingFailureCategory,
        message: String
    ) : IllegalStateException(message)

    private class RetryOutgoingException(val minimumDelayMillis: Long) : IllegalStateException("Outgoing upload is still processing")
}
