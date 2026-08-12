package com.barkfluff.client.send

import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.os.Build
import android.os.IBinder
import android.provider.OpenableColumns
import android.util.Log
import androidx.core.app.NotificationManagerCompat
import androidx.media3.common.MediaItem
import androidx.media3.effect.Presentation
import androidx.media3.transformer.Composition
import androidx.media3.transformer.EditedMediaItem
import androidx.media3.transformer.Effects
import androidx.media3.transformer.ExportException
import androidx.media3.transformer.ExportResult
import androidx.media3.transformer.ProgressHolder
import androidx.media3.transformer.Transformer
import barkfluff.files.FilesApiOuterClass.UploadFileType
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.ImageCompressor
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.coroutines.isActive
import java.io.File
import java.util.concurrent.atomic.AtomicReference
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * Foreground-сервис очереди отправки медиа в чат.
 * Жизненный цикл: запускается через MediaSendService.enqueue(), обрабатывает по одному заданию,
 * stopSelf() когда очередь пуста.
 */
class MediaSendService : Service() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private val queue = Channel<SendJob>(Channel.UNLIMITED)
    private var processor: Job? = null

    // Для прогресс-обновлений активного job'а
    private val activeJob = AtomicReference<SendJob?>(null)

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        MediaSendNotification.ensureChannel(this)
        startProcessor()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // Запускаем foreground сразу с placeholder-уведомлением
        val notif = MediaSendNotification.build(
            this,
            title = getString(R.string.notification_channel_media_send_name),
            text = getString(R.string.media_send_preparing, getString(R.string.media_send_default_chat)),
            progress = 0,
            indeterminate = true
        )
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(
                MediaSendNotification.FOREGROUND_NOTIFICATION_ID,
                notif,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
            )
        } else {
            startForeground(MediaSendNotification.FOREGROUND_NOTIFICATION_ID, notif)
        }

        intent?.getStringExtra(EXTRA_JOB_ID)?.let { jobId ->
            val job = pendingJobs.remove(jobId)
            if (job != null) {
                queue.trySend(job)
            }
        }
        return START_NOT_STICKY
    }

    override fun onDestroy() {
        super.onDestroy()
        processor?.cancel()
        scope.cancel()
        queue.close()
    }

    private fun startProcessor() {
        if (processor?.isActive == true) return
        processor = scope.launch {
            try {
                while (isActive) {
                    // Ждём job до 1 секунды; если ничего нет — выходим и останавливаем сервис.
                    // Race-кейс с новым enqueue: enqueue вызовет startForegroundService → onStartCommand → startProcessor()
                    // снова, который запустит новую корутину (предыдущая уже completed).
                    val job = withTimeoutOrNull(1000L) { queue.receive() } ?: break
                    activeJob.set(job)
                    runCatching { processJob(job) }
                        .onFailure { Log.e(TAG, "Job ${job.jobId} failed", it) }
                    activeJob.set(null)
                }
            } finally {
                stopForegroundCompat()
                stopSelf()
            }
        }
    }

    private suspend fun processJob(job: SendJob) {
        val grpcManager = (applicationContext as BarkFluffApplication).grpcManager
        val repo = ChatRepository(applicationContext, grpcManager)
        val titleBase = getString(
            R.string.media_send_title,
            job.chatTitle.ifBlank { getString(R.string.media_send_default_chat) }
        )

        // Подсчитываем стадии для агрегированного прогресса
        val total = job.attachments.size.coerceAtLeast(1)
        val fileIds = mutableListOf<String>()

        // Локальные IDs оптимистичных сообщений в ChatActivity. Если sendSeparately — каждый attachment
        // имеет свой localId; иначе все аплоады обновляют единый localId (первый из списка).
        fun localIdForAttachment(attachIdx: Int): String? = when {
            job.localIds.isEmpty() -> null
            job.sendSeparately -> job.localIds.getOrNull(attachIdx)
            else -> job.localIds.firstOrNull()
        }

        // Агрегированный прогресс одного inline-сообщения. При sendSeparately у каждого файла своё
        // сообщение → прогресс пофайловый. Иначе все файлы делят один localId → показываем общий
        // прогресс по всем N файлам, чтобы бар не сбрасывался в 0 на каждом следующем файле.
        fun aggregateProgress(idx: Int, pct: Int): Int =
            if (job.sendSeparately) pct
            else ((idx * 100 + pct) / total).coerceIn(0, 100)

        for ((idx, att) in job.attachments.withIndex()) {
            val pos = "${idx + 1}/$total"
            val localId = localIdForAttachment(idx)
            updateNotification(
                titleBase,
                getString(R.string.media_send_preparing, pos),
                aggregateProgress(idx, 0),
                indeterminate = true
            )
            if (localId != null) {
                uploadEvents.tryEmit(UploadEvent(job.chatId, localId, UploadState.PREPARING, aggregateProgress(idx, 0)))
            }
            val prepared = prepareAttachment(att, job, titleBase, pos)
            if (prepared == null) {
                if (localId != null) uploadEvents.tryEmit(UploadEvent(job.chatId, localId, UploadState.FAILED))
                continue
            }
            val (bytes, type, fileName, mime) = prepared

            updateNotification(
                titleBase,
                getString(R.string.media_send_uploading, pos, 0),
                aggregateProgress(idx, 0),
                indeterminate = false
            )
            val uploadResult = repo.uploadFile(
                jpegImageBytes = bytes,
                fileType = type,
                fileName = fileName,
                mimeType = mime,
                onProgress = { pct ->
                    val agg = aggregateProgress(idx, pct)
                    updateNotification(
                        titleBase,
                        getString(R.string.media_send_uploading, pos, pct),
                        agg,
                        indeterminate = false
                    )
                    if (localId != null) {
                        uploadEvents.tryEmit(UploadEvent(job.chatId, localId, UploadState.UPLOADING, agg))
                    }
                }
            )

            val fid = uploadResult.getOrNull()
            if (fid != null) {
                fileIds.add(fid)
            } else {
                Log.e(TAG, "uploadFile failed for attachment $idx in job ${job.jobId}")
                if (localId != null) uploadEvents.tryEmit(UploadEvent(job.chatId, localId, UploadState.FAILED))
            }
        }

        if (fileIds.isEmpty() && job.text.isBlank()) {
            return
        }

        updateNotification(titleBase, getString(R.string.media_send_message), 100, indeterminate = true)
        if (job.sendSeparately && fileIds.size > 1) {
            for ((idx, fid) in fileIds.withIndex()) {
                val text = if (idx == 0) job.text else ""
                val localId = localIdForAttachment(idx)
                if (localId != null) {
                    uploadEvents.tryEmit(UploadEvent(job.chatId, localId, UploadState.SENDING))
                }
                val r = repo.sendMessage(job.chatId, text, listOf(fid), job.replyId)
                val state = if (r.isSuccess) UploadState.SENT else UploadState.FAILED
                val mid = r.getOrNull()?.id ?: 0L
                if (localId != null) {
                    uploadEvents.tryEmit(UploadEvent(job.chatId, localId, state, serverMessageId = mid))
                }
            }
        } else {
            val localId = localIdForAttachment(0)
            if (localId != null) {
                uploadEvents.tryEmit(UploadEvent(job.chatId, localId, UploadState.SENDING))
            }
            val r = repo.sendMessage(job.chatId, job.text, fileIds, job.replyId)
            val state = if (r.isSuccess) UploadState.SENT else UploadState.FAILED
            val mid = r.getOrNull()?.id ?: 0L
            if (localId != null) {
                uploadEvents.tryEmit(UploadEvent(job.chatId, localId, state, serverMessageId = mid))
            }
        }
    }

    private data class PreparedAttachment(
        val bytes: ByteArray,
        val type: UploadFileType,
        val fileName: String?,
        val mimeType: String?
    )

    private suspend fun prepareAttachment(
        att: AttachmentSpec,
        job: SendJob,
        titleBase: String,
        pos: String
    ): PreparedAttachment? = withContext(Dispatchers.IO) {
        try {
            when (att) {
                is AttachmentSpec.EditedImage -> {
                    val raw = SendPayloadCache.take(att.cacheKey) ?: return@withContext null
                    // Дополнительно прогоняем через ImageCompressor (ограничение 2500px, JPEG 90%)
                    val compressed = if (job.sendAsFile) {
                        raw
                    } else {
                        ImageCompressor.compressImage(raw).getOrNull() ?: raw
                    }
                    val type = if (job.sendAsFile)
                        UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT
                    else
                        UploadFileType.MESSAGE_ATTACHMENT_IMAGE
                    PreparedAttachment(compressed, type, fileName = null, mimeType = null)
                }
                is AttachmentSpec.RawImage -> {
                    val mime = applicationContext.contentResolver.getType(att.uri)
                    val isWebp = mime == "image/webp"
                    val isVideo = mime?.startsWith("video/") == true
                    val type = when {
                        job.sendAsFile -> UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT
                        isVideo -> UploadFileType.MESSAGE_ATTACHMENT_VIDEO
                        isWebp -> UploadFileType.MESSAGE_ATTACHMENT_STICKER
                        else -> UploadFileType.MESSAGE_ATTACHMENT_IMAGE
                    }
                    val bytes: ByteArray = if (job.sendAsFile || isWebp || isVideo) {
                        readBytesFromUri(att.uri) ?: return@withContext null
                    } else {
                        ImageCompressor.compressImage(att.uri, applicationContext).getOrNull()
                            ?: return@withContext null
                    }
                    val (name, m) = if (job.sendAsFile || isVideo) getDocumentInfo(att.uri) else (null to null)
                    PreparedAttachment(bytes, type, name, m)
                }
                is AttachmentSpec.Video -> {
                    updateNotification(
                        titleBase,
                        getString(R.string.media_send_compressing, pos),
                        0,
                        indeterminate = true
                    )
                    val outFile = transformVideo(att.spec, titleBase, pos)
                        ?: return@withContext null
                    val bytes = outFile.readBytes()
                    runCatching { outFile.delete() }
                    val (origName, _) = getDocumentInfo(att.spec.uri)
                    val name = origName?.let { stripExt(it) + ".mp4" } ?: "video.mp4"
                    PreparedAttachment(bytes, UploadFileType.MESSAGE_ATTACHMENT_VIDEO, name, "video/mp4")
                }
                is AttachmentSpec.Document -> {
                    val bytes = readBytesFromUri(att.uri) ?: return@withContext null
                    val (name, mime) = getDocumentInfo(att.uri)
                    PreparedAttachment(bytes, UploadFileType.MESSAGE_ATTACHMENT_DOCUMENT, name, mime)
                }
                is AttachmentSpec.Sticker -> {
                    val raw = SendPayloadCache.take(att.cacheKey) ?: return@withContext null
                    PreparedAttachment(raw, UploadFileType.MESSAGE_ATTACHMENT_STICKER, fileName = null, mimeType = null)
                }
                is AttachmentSpec.Voice -> {
                    val bytes = att.file.readBytes()
                    runCatching { att.file.delete() }
                    PreparedAttachment(bytes, UploadFileType.MESSAGE_ATTACHMENT_VOICE, "voice.ogg", "audio/ogg")
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "prepareAttachment error", e)
            null
        }
    }

    private suspend fun transformVideo(
        spec: com.barkfluff.client.editor.EditedVideoSpec,
        titleBase: String,
        pos: String
    ): File? = withContext(Dispatchers.Main) {
        val outFile = File.createTempFile("send_${System.currentTimeMillis()}", ".mp4", cacheDir)
        try {
            val mediaItemBuilder = MediaItem.Builder().setUri(spec.uri)
            if (spec.trimStartMs > 0 || spec.trimEndMs > 0) {
                val clip = MediaItem.ClippingConfiguration.Builder()
                    .setStartPositionMs(spec.trimStartMs.coerceAtLeast(0))
                if (spec.trimEndMs > 0) clip.setEndPositionMs(spec.trimEndMs)
                // Frame-accurate cut вместо округления до ближайшего key-frame
                clip.setStartsAtKeyFrame(false)
                mediaItemBuilder.setClippingConfiguration(clip.build())
            }
            val mediaItem = mediaItemBuilder.build()

            val editedBuilder = EditedMediaItem.Builder(mediaItem)
            if (spec.compressTo480p) {
                val effects = Effects(
                    /* audioProcessors = */ emptyList(),
                    /* videoEffects = */ listOf(Presentation.createForHeight(480))
                )
                editedBuilder.setEffects(effects)
            }
            val edited = editedBuilder.build()

            val transformer = Transformer.Builder(this@MediaSendService).build()

            // Запускаем polling прогресса параллельно
            val progressJob = launch {
                val holder = ProgressHolder()
                while (true) {
                    delay(500)
                    val state = transformer.getProgress(holder)
                    if (state == Transformer.PROGRESS_STATE_AVAILABLE) {
                        updateNotification(
                            titleBase,
                            getString(R.string.media_send_compressing_progress, pos, holder.progress),
                            holder.progress,
                            indeterminate = false
                        )
                    } else if (state == Transformer.PROGRESS_STATE_UNAVAILABLE) {
                        updateNotification(
                            titleBase,
                            getString(R.string.media_send_compressing, pos),
                            0,
                            indeterminate = true
                        )
                    } else if (state == Transformer.PROGRESS_STATE_NOT_STARTED) {
                        break
                    }
                }
            }

            val ok = awaitTransform(transformer, edited, outFile.absolutePath)
            progressJob.cancel()

            if (!ok) {
                outFile.delete()
                return@withContext null
            }
            outFile
        } catch (e: Exception) {
            Log.e(TAG, "transformVideo error", e)
            outFile.delete()
            null
        }
    }

    private suspend fun awaitTransform(
        transformer: Transformer,
        item: EditedMediaItem,
        outputPath: String
    ): Boolean = suspendCancellableCoroutine { cont ->
        val listener = object : Transformer.Listener {
            override fun onCompleted(composition: Composition, exportResult: ExportResult) {
                if (cont.isActive) cont.resume(true)
            }

            override fun onError(
                composition: Composition,
                exportResult: ExportResult,
                exportException: ExportException
            ) {
                if (cont.isActive) cont.resume(false)
            }
        }
        transformer.addListener(listener)
        try {
            transformer.start(item, outputPath)
        } catch (e: Throwable) {
            if (cont.isActive) cont.resumeWithException(e)
        }
        cont.invokeOnCancellation {
            try { transformer.cancel() } catch (_: Throwable) {}
        }
    }

    private fun readBytesFromUri(uri: Uri): ByteArray? {
        return try {
            applicationContext.contentResolver.openInputStream(uri)?.use { it.readBytes() }
        } catch (e: Exception) {
            Log.e(TAG, "readBytesFromUri error", e)
            null
        }
    }

    private fun getDocumentInfo(uri: Uri): Pair<String?, String?> {
        var name: String? = null
        try {
            applicationContext.contentResolver.query(uri, null, null, null, null)?.use { c ->
                val nameIdx = c.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                if (nameIdx >= 0 && c.moveToFirst()) {
                    name = c.getString(nameIdx)
                }
            }
        } catch (_: Exception) {}
        val mime = applicationContext.contentResolver.getType(uri)
        return name to mime
    }

    private fun stripExt(name: String): String {
        val dot = name.lastIndexOf('.')
        return if (dot > 0) name.substring(0, dot) else name
    }

    private fun updateNotification(title: String, text: String, progress: Int, indeterminate: Boolean) {
        val notif = MediaSendNotification.build(this, title, text, progress, indeterminate)
        try {
            NotificationManagerCompat.from(this).notify(MediaSendNotification.FOREGROUND_NOTIFICATION_ID, notif)
        } catch (_: SecurityException) {}
    }

    private fun stopForegroundCompat() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
            stopForeground(STOP_FOREGROUND_REMOVE)
        } else {
            @Suppress("DEPRECATION")
            stopForeground(true)
        }
    }

    companion object {
        private const val TAG = "MediaSendService"
        private const val EXTRA_JOB_ID = "job_id"

        // Передаём SendJob в сервис через статическую map по jobId,
        // потому что parcelize'ить SendJob (с byte[] и URIs) дороже и хрупче.
        private val pendingJobs: MutableMap<String, SendJob> = mutableMapOf()

        /**
         * Глобальная шина прогресса аплоада медиа в чат — ChatActivity подписывается
         * чтобы рендерить inline-прогресс на оптимистичных сообщениях.
         */
        val uploadEvents: kotlinx.coroutines.flow.MutableSharedFlow<UploadEvent> =
            kotlinx.coroutines.flow.MutableSharedFlow(extraBufferCapacity = 64)

        @Synchronized
        fun enqueue(context: Context, job: SendJob) {
            pendingJobs[job.jobId] = job
            val intent = Intent(context, MediaSendService::class.java).apply {
                putExtra(EXTRA_JOB_ID, job.jobId)
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                context.startForegroundService(intent)
            } else {
                context.startService(intent)
            }
        }
    }
}

enum class UploadState { PREPARING, UPLOADING, SENDING, SENT, FAILED }

data class UploadEvent(
    val chatId: String,
    val localId: String,
    val state: UploadState,
    /** 0..100, для UPLOADING. Для других состояний игнорируется. */
    val progress: Int = 0,
    /** Реальный messageId с сервера — заполняется в state=SENT. 0 если неизвестен. */
    val serverMessageId: Long = 0L
)
