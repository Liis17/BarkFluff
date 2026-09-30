package com.barkfluff.client.data

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import barkfluff.shared.Shared.MessageAttachmentType
import com.barkfluff.client.domain.media.AutoDownloadMode
import com.barkfluff.client.domain.media.AutoDownloadSettings
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AutoDownloadSettingsInstrumentedTest {
    @Test
    fun settingsSurviveRecreationAndLogoutRestoresObservableDefaults() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        val globalParam = GlobalParam(context)
        val original = globalParam.autoDownloadSettings
        val store = AutoDownloadSettingsStore(globalParam)
        try {
            instrumentation.runOnMainSync {
                store.setMode(MessageAttachmentType.VOICE, AutoDownloadMode.MANUAL)
                store.setMaxSizeMb(10)
            }
            assertEquals(AutoDownloadMode.MANUAL, store.settings.value.mode(MessageAttachmentType.VOICE))
            assertEquals(10, GlobalParam(context).autoDownloadSettings.maxSizeMb)
            assertEquals(AutoDownloadMode.MANUAL, GlobalParam(context).autoDownloadSettings.mode(MessageAttachmentType.VOICE))
            instrumentation.runOnMainSync { globalParam.clearUserData() }
            assertEquals(AutoDownloadSettings(), store.settings.value)
        } finally {
            instrumentation.runOnMainSync { globalParam.autoDownloadSettings = original }
        }
    }
}
