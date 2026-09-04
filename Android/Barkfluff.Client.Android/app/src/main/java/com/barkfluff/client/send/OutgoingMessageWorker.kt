package com.barkfluff.client.send

import android.content.Context
import androidx.work.CoroutineWorker
import androidx.work.ForegroundInfo
import androidx.work.WorkerParameters
import com.barkfluff.client.di.OutgoingQueueEntryPoint
import dagger.hilt.android.EntryPointAccessors

/**
 * Persistent WorkManager trigger for [OutgoingMessageQueue]. The worker owns no message data:
 * SQLCipher Room and no-backup files remain the source of truth across force-stop/process death.
 */
class OutgoingMessageWorker(
    appContext: Context,
    params: WorkerParameters
) : CoroutineWorker(appContext, params) {

    override suspend fun doWork(): Result {
        val queue = EntryPointAccessors.fromApplication(
            applicationContext,
            OutgoingQueueEntryPoint::class.java,
        ).outgoingMessageQueue()
        return try {
            queue.processReady { snapshot ->
                MediaSendNotification.ensureChannel(applicationContext)
                setForeground(
                    ForegroundInfo(
                        MediaSendNotification.FOREGROUND_NOTIFICATION_ID,
                        MediaSendNotification.build(
                            context = applicationContext,
                            title = snapshot.chatId,
                            text = "${snapshot.state.name.lowercase()} · ${snapshot.progress}%",
                            progress = snapshot.progress,
                            indeterminate = snapshot.state == com.barkfluff.client.cache.OutgoingMessageState.PREPARING,
                            cancelOperationId = snapshot.operationId
                        )
                    )
                )
            }
            Result.success()
        } catch (e: kotlinx.coroutines.CancellationException) {
            throw e
        } catch (_: Throwable) {
            // A failure outside one operation (for example an encrypted DB open race) may be
            // retried by WorkManager; transport failures are persisted by the processor itself.
            Result.retry()
        }
    }
}
