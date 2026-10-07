package com.barkfluff.client.drafts

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.cache.CacheScope
import java.io.File
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class VoiceDraftStoreTest {
    @Test
    fun completedVoiceSurvivesStoreRecreationAndIsIsolatedByAccountAndChat() = runBlocking {
        val app = InstrumentationRegistry.getInstrumentation().targetContext.applicationContext as BarkFluffApplication
        val scope = CacheScope("voice-test-${UUID.randomUUID()}|1")
        val source = File.createTempFile("voice", ".ogg", app.cacheDir).apply { writeBytes(byteArrayOf(1, 2, 3, 4)) }
        val store = ComposerAttachmentStore(app, app.chatCacheRepository)
        try {
            store.stageFile(scope, "chat", source, 7L, "VOICE", "voice.ogg", "audio/ogg")
            source.delete()
            val recreated = ComposerAttachmentStore(app, app.chatCacheRepository)
            val restored = recreated.restore(scope, "chat").single()
            assertEquals("VOICE", restored.kind)
            assertArrayEquals(byteArrayOf(1, 2, 3, 4), File(restored.path).readBytes())
            assertEquals(emptyList<Any>(), recreated.restore(CacheScope(scope.id + "other"), "chat"))
            assertEquals(emptyList<Any>(), recreated.restore(scope, "another-chat"))
            recreated.clearAfterEnqueue(scope, "chat", 7L)
            assertEquals(emptyList<Any>(), recreated.restore(scope, "chat"))
            assertFalse(File(restored.path).exists())
        } finally {
            source.delete()
            store.clearScope(scope)
        }
    }
}
