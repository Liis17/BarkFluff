package com.barkfluff.client.voice

import android.Manifest
import android.content.Intent
import android.view.View
import android.view.accessibility.AccessibilityNodeInfo
import androidx.lifecycle.ViewModelProvider
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.ChatActivity
import com.barkfluff.client.ChatIntent
import com.barkfluff.client.ChatViewModel
import com.barkfluff.client.R
import com.barkfluff.client.adapter.MessageItem
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.drafts.ComposerAttachmentStore
import java.io.File
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.After
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class VoiceComposerTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private lateinit var app: BarkFluffApplication
    private lateinit var preferences: GlobalParam
    private lateinit var scope: CacheScope
    private lateinit var oldBeacon: String
    private var oldUser = 0L
    private val chatId = "voice-ui-chat"
    private var scenario: ActivityScenario<ChatActivity>? = null

    @Before
    fun setUp() {
        app = instrumentation.targetContext.applicationContext as BarkFluffApplication
        if (app.checkSelfPermission(Manifest.permission.RECORD_AUDIO) != android.content.pm.PackageManager.PERMISSION_GRANTED)
            instrumentation.uiAutomation.grantRuntimePermission(app.packageName, Manifest.permission.RECORD_AUDIO)
        preferences = GlobalParam(app)
        oldBeacon = preferences.socketBeacon
        oldUser = preferences.userId
        preferences.socketBeacon = "voice-ui-${UUID.randomUUID()}"
        preferences.userId = Long.MAX_VALUE - 11
        scope = requireNotNull(CacheScope.from(preferences))
    }

    @After
    fun tearDown() = runBlocking {
        scenario?.close()
        app.outgoingMessageQueue.cancelAllForCurrentScope()
        ComposerAttachmentStore(app, app.chatCacheRepository).clearScope(scope)
        preferences.socketBeacon = oldBeacon
        preferences.userId = oldUser
    }

    @Test
    fun completedVoiceRestoresReplyAfterClosingChatAndDoubleSendQueuesOnlyOnce() {
        var model = openChat()
        val source = File.createTempFile("voice-ui-source", ".ogg", app.cacheDir)
        instrumentation.context.assets.open("voice_playback.ogg").use { input -> source.outputStream().use { input.copyTo(it) } }
        scenario!!.onActivity {
            model.dispatch(ChatIntent.SetReply(MessageItem(42L, 2L, senderName = "Sender", text = "Original", timestamp = 0L, attachments = emptyList())))
            model.dispatch(ChatIntent.StageVoice(source))
        }
        await { model.state.value.composer.attachmentKinds == listOf("VOICE") && !model.state.value.composer.isVoiceStaging }
        val draftPath = model.state.value.composer.attachmentPaths.single()
        assertFalse(source.exists())
        scenario!!.close()
        model = openChat(awaitRestoration = false)
        // Sending during either restoration phase must not enqueue a reply-less clip.
        scenario!!.onActivity {
            if (model.state.value.composer.isRestoring || model.state.value.composer.isRestoringAttachments) {
                model.dispatch(ChatIntent.Send(""))
                assertFalse(model.state.value.composer.isSending)
            }
        }
        await { model.state.value.composer.attachmentPaths == listOf(draftPath) && model.state.value.pendingReply?.messageId == 42L && !model.state.value.composer.isRestoring }
        scenario!!.onActivity {
            model.dispatch(ChatIntent.Send(""))
            model.dispatch(ChatIntent.Send(""))
        }
        await { !model.state.value.composer.isSending && model.state.value.composer.attachmentPaths.isEmpty() }
        runBlocking {
            val operations = app.chatCacheRepository.outgoingOperationIds(scope)
            assertEquals(1, operations.size)
            val outgoing = app.chatCacheRepository.outgoing(scope, operations.single())!!
            assertEquals(42L, outgoing.replyToMessageId)
            assertTrue(File(outgoing.attachments.single().sourcePath).isFile)
        }
        assertFalse(File(draftPath).exists())
    }

    @Test
    fun talkBackCanStartLockedRecordingAndLeavingPausedRecordingDeletesFile() {
        openChat()
        val existing = app.cacheDir.listFiles().orEmpty().map { it.name }.toSet()
        scenario!!.onActivity { activity ->
            assertTrue(activity.findViewById<View>(R.id.sendButton).performAccessibilityAction(AccessibilityNodeInfo.ACTION_CLICK, null))
            assertEquals(View.VISIBLE, activity.findViewById<View>(R.id.voiceRecordStop).visibility)
        }
        Thread.sleep(650L)
        scenario!!.onActivity { it.findViewById<View>(R.id.voiceRecordPause).performClick() }
        val active = app.cacheDir.listFiles().orEmpty().filter { it.name.startsWith("voice_") && it.name !in existing }
        assertEquals(1, active.size)
        val leavingAt = android.os.SystemClock.elapsedRealtime()
        scenario!!.moveToState(androidx.lifecycle.Lifecycle.State.CREATED)
        assertTrue("Canceling a paused recorder must not block the UI", android.os.SystemClock.elapsedRealtime() - leavingAt < 5_000L)
        assertFalse(active.single().exists())
        assertFalse(app.audioPlayback.state.value.isPlaying)
        assertTrue(app.audioPlayback.playbackAllowed)
        runBlocking { assertTrue(ComposerAttachmentStore(app, app.chatCacheRepository).restore(scope, chatId).isEmpty()) }
    }

    @Test
    fun stoppingPausedNativeRecordingCreatesPlayableVoiceDraft() {
        val model = openChat()
        await { !model.state.value.composer.isRestoring }
        val recordingAt = android.os.SystemClock.elapsedRealtime()
        scenario!!.onActivity { activity ->
            activity.findViewById<View>(R.id.sendButton).performAccessibilityAction(AccessibilityNodeInfo.ACTION_CLICK, null)
        }
        Thread.sleep(1_100L)
        var timer = ""
        scenario!!.onActivity {
            it.findViewById<View>(R.id.voiceRecordPause).performClick()
            timer = it.findViewById<android.widget.TextView>(R.id.voiceRecordTimer).text.toString()
        }
        val activeMillis = android.os.SystemClock.elapsedRealtime() - recordingAt
        Thread.sleep(1_200L)
        val stoppingAt = android.os.SystemClock.elapsedRealtime()
        scenario!!.onActivity {
            assertEquals(timer, it.findViewById<android.widget.TextView>(R.id.voiceRecordTimer).text.toString())
            it.findViewById<View>(R.id.voiceRecordStop).performClick()
        }
        assertTrue("Stopping a paused recorder must not block the UI", android.os.SystemClock.elapsedRealtime() - stoppingAt < 5_000L)
        await { model.state.value.composer.attachmentKinds == listOf("VOICE") && !model.state.value.composer.isVoiceStaging }
        assertTrue(File(model.state.value.composer.attachmentPaths.single()).isFile)
        val retriever = android.media.MediaMetadataRetriever()
        try {
            retriever.setDataSource(model.state.value.composer.attachmentPaths.single())
            val duration = retriever.extractMetadata(android.media.MediaMetadataRetriever.METADATA_KEY_DURATION)!!.toLong()
            assertTrue("Paused interval must not be encoded: $duration ms, active $activeMillis ms", duration in 500L..(activeMillis + 300L))
        } finally { retriever.release() }
        scenario!!.onActivity {
            assertEquals(View.VISIBLE, it.findViewById<View>(R.id.voicePreviewPlay).visibility)
            it.findViewById<View>(R.id.voicePreviewPlay).performClick()
        }
        await {
            var playing = false
            scenario!!.onActivity { playing = it.findViewById<View>(R.id.voicePreviewPlay).contentDescription == app.getString(R.string.cd_pause) }
            playing
        }
    }

    private fun openChat(awaitRestoration: Boolean = true): ChatViewModel {
        scenario = ActivityScenario.launch(Intent(app, ChatActivity::class.java)
            .putExtra("chat_id", chatId).putExtra("chat_title", "Voice test"))
        var result: ChatViewModel? = null
        scenario!!.onActivity { result = ViewModelProvider(it)[ChatViewModel::class.java] }
        val model = requireNotNull(result)
        if (awaitRestoration) await { !model.state.value.composer.isRestoringAttachments && !model.state.value.composer.isRestoring }
        return model
    }

    private fun await(condition: () -> Boolean) {
        val deadline = android.os.SystemClock.elapsedRealtime() + 10_000L
        while (!condition()) {
            if (android.os.SystemClock.elapsedRealtime() >= deadline) throw AssertionError("Timed out waiting for composer state")
            Thread.sleep(50L)
        }
    }
}
