package com.barkfluff.client.drafts

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import barkfluff.files.FilesApiOuterClass.UploadFileType
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.OutgoingAttachmentKind
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.send.AttachmentSpec
import com.barkfluff.client.send.SendJob
import java.io.File
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class VoiceDraftHandoffTest {
    @Test
    fun failedEnqueueRetainsVoiceAndReplyThenAcceptanceOwnsOneDurableCopy() = runBlocking {
        val app = InstrumentationRegistry.getInstrumentation().targetContext.applicationContext as BarkFluffApplication
        val preferences = GlobalParam(app)
        val oldBeacon = preferences.socketBeacon
        val oldUser = preferences.userId
        preferences.socketBeacon = "voice-handoff-${UUID.randomUUID()}"
        preferences.userId = Long.MAX_VALUE - 10
        val scope = requireNotNull(CacheScope.from(preferences))
        val store = ComposerAttachmentStore(app, app.chatCacheRepository)
        val source = File.createTempFile("voice-draft-test", ".ogg", app.cacheDir).apply { writeBytes(byteArrayOf(1, 2, 3)) }
        try {
            val draft = requireNotNull(app.chatDraftRepository.edit("chat", "", 42L))
            val staged = store.stageFile(scope, "chat", source, draft.generation, "VOICE", "voice.ogg", "audio/ogg")
            source.delete()
            val job = SendJob("chat", "Chat", "", listOf(AttachmentSpec.StagedFile(
                File(staged.path), OutgoingAttachmentKind.VOICE, UploadFileType.MESSAGE_ATTACHMENT_VOICE,
                fileName = "voice.ogg", mimeType = "audio/ogg",
            )), replyId = 42L, draftGeneration = draft.generation)
            assertTrue(runCatching { app.outgoingMessageQueue.enqueue(job.copy(chatId = "")) }.isFailure)
            assertTrue(File(staged.path).isFile)
            assertEquals(42L, app.chatDraftRepository.restore("chat")?.replyToMessageId)
            assertEquals(staged.path, ComposerAttachmentStore(app, app.chatCacheRepository).restore(scope, "chat").single().path)

            val operation = app.outgoingMessageQueue.enqueue(job).single()
            val outgoing = requireNotNull(app.chatCacheRepository.outgoing(scope, operation))
            assertEquals(42L, outgoing.replyToMessageId)
            assertEquals(OutgoingAttachmentKind.VOICE, outgoing.attachments.single().kind)
            assertEquals(setOf(operation), app.chatCacheRepository.outgoingOperationIds(scope))
            assertFalse(File(staged.path).exists())
            assertTrue(File(outgoing.attachments.single().sourcePath).isFile)
            assertTrue(store.restore(scope, "chat").isEmpty())
            assertTrue(app.outgoingMessageQueue.hasDurableHandoff("chat", draft.generation))
        } finally {
            app.outgoingMessageQueue.cancelAllForCurrentScope()
            store.clearScope(scope)
            source.delete()
            preferences.socketBeacon = oldBeacon
            preferences.userId = oldUser
        }
    }
}
