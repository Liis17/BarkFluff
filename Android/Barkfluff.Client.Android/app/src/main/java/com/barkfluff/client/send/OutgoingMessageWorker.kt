package com.barkfluff.client.send

import android.content.Context
import android.content.pm.ServiceInfo
import androidx.work.CoroutineWorker
import androidx.work.ForegroundInfo
import androidx.work.WorkerParameters
import com.barkfluff.client.cache.OutgoingMessageState
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
                setForeground(buildOutgoingMessageForegroundInfo(applicationContext, snapshot))
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

internal fun buildOutgoingMessageForegroundInfo(
    context: Context,
    snapshot: OutgoingMessageSnapshot,
): ForegroundInfo {
    MediaSendNotification.ensureChannel(context)
    return ForegroundInfo(
        MediaSendNotification.FOREGROUND_NOTIFICATION_ID,
        MediaSendNotification.build(
            context = context,
            title = snapshot.chatId,
            text = "${snapshot.state.name.lowercase()} · ${snapshot.progress}%",
            progress = snapshot.progress,
            indeterminate = snapshot.state == OutgoingMessageState.PREPARING,
            cancelOperationId = snapshot.operationId,
        ),
        ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC,
    )
}
