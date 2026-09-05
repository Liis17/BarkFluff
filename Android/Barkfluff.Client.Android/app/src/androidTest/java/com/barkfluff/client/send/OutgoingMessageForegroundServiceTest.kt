package com.barkfluff.client.send

import android.content.ComponentName
import android.content.pm.ServiceInfo
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.work.impl.foreground.SystemForegroundService
import com.barkfluff.client.cache.OutgoingMessageState
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OutgoingMessageForegroundServiceTest {

    @Test
    fun workManagerForegroundServiceDeclaresDataSyncType() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val serviceInfo = context.packageManager.getServiceInfo(
            ComponentName(context, SystemForegroundService::class.java),
            0,
        )

        assertTrue(
            "WorkManager foreground service must declare dataSync",
            serviceInfo.foregroundServiceType and ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC != 0,
        )
    }

    @Test
    fun outgoingWorkerUsesDataSyncForegroundType() {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val snapshot = OutgoingMessageSnapshot(
            operationId = "operation",
            chatId = "chat",
            text = "text",
            replyToMessageId = 0L,
            createdAtMillis = 0L,
            draftGeneration = null,
            state = OutgoingMessageState.PREPARING,
            progress = 0,
            previewPaths = emptyList(),
            failureCategory = null,
            serverMessageId = 0L,
        )

        assertEquals(
            ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC,
            buildOutgoingMessageForegroundInfo(context, snapshot).foregroundServiceType,
        )
    }
}
