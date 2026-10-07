package com.barkfluff.client.voice

import android.content.Intent
import android.graphics.Rect
import android.view.View
import android.view.inputmethod.InputMethodManager
import androidx.appcompat.app.AppCompatDelegate
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.lifecycle.ViewModelProvider
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.ChatActivity
import com.barkfluff.client.ChatIntent
import com.barkfluff.client.ChatViewModel
import com.barkfluff.client.R
import com.barkfluff.client.audio.AudioTrack
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.drafts.ComposerAttachmentStore
import java.io.File
import java.util.UUID
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

/** Run with wm size/density overrides and voice_theme=light/dark for window-size QA. */
@RunWith(AndroidJUnit4::class)
class VoiceUiLayoutTest {
    @Test
    fun previewAndMiniPlayerFitWithKeyboardAndAccessibleControls() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val app = instrumentation.targetContext.applicationContext as BarkFluffApplication
        val preferences = GlobalParam(app)
        val oldBeacon = preferences.socketBeacon
        val oldUser = preferences.userId
        val oldTheme = AppCompatDelegate.getDefaultNightMode()
        val theme = InstrumentationRegistry.getArguments().getString("voice_theme", "light")
        preferences.socketBeacon = "voice-layout-${UUID.randomUUID()}"
        preferences.userId = Long.MAX_VALUE - 12
        val scope = requireNotNull(CacheScope.from(preferences))
        val source = File.createTempFile("voice-layout", ".ogg", app.cacheDir)
        instrumentation.context.assets.open("voice_playback.ogg").use { input -> source.outputStream().use { input.copyTo(it) } }
        instrumentation.runOnMainSync { AppCompatDelegate.setDefaultNightMode(if (theme == "dark") AppCompatDelegate.MODE_NIGHT_YES else AppCompatDelegate.MODE_NIGHT_NO) }
        val scenario = ActivityScenario.launch<ChatActivity>(Intent(app, ChatActivity::class.java)
            .putExtra("chat_id", "voice-layout-chat").putExtra("chat_title", "Voice layout"))
        try {
            lateinit var model: ChatViewModel
            scenario.onActivity { model = ViewModelProvider(it)[ChatViewModel::class.java] }
            await { !model.state.value.composer.isRestoring && !model.state.value.composer.isRestoringAttachments }
            scenario.onActivity { model.dispatch(ChatIntent.StageVoice(source)) }
            await { model.state.value.composer.attachmentKinds == listOf("VOICE") && !model.state.value.composer.isVoiceStaging }
            scenario.onActivity {
                app.audioPlayback.play(AudioTrack("layout-player", senderName = "A long sender name for the mini-player"), File(model.state.value.composer.attachmentPaths.single()))
            }
            await { app.audioPlayback.state.value.isPlaying && app.audioPlayback.state.value.durationMillis > 0L }
            scenario.onActivity {
                it.findViewById<View>(R.id.messageEditText).apply {
                    requestFocus()
                    it.getSystemService(InputMethodManager::class.java).showSoftInput(this, InputMethodManager.SHOW_IMPLICIT)
                }
            }
            var previousInset = 0
            var stableSince = 0L
            await {
                var bottom = 0
                scenario.onActivity {
                    val insets = ViewCompat.getRootWindowInsets(it.window.decorView)
                    if (insets?.isVisible(WindowInsetsCompat.Type.ime()) == true) bottom = insets.getInsets(WindowInsetsCompat.Type.ime()).bottom
                }
                val now = android.os.SystemClock.elapsedRealtime()
                if (bottom == 0 || bottom != previousInset) {
                    previousInset = bottom
                    stableSince = now
                    false
                } else now - stableSince >= 400L
            }
            instrumentation.uiAutomation.takeScreenshot()?.let { bitmap ->
                File(app.cacheDir, "voice_ui_$theme.png").outputStream().use { bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }
                bitmap.recycle()
            }
            scenario.onActivity { activity ->
                val frame = Rect().also(activity.window.decorView::getWindowVisibleDisplayFrame)
                val minimum = (48 * activity.resources.displayMetrics.density).toInt()
                for (id in listOf(R.id.voicePreviewPlay, R.id.voicePreviewDelete, R.id.voicePreviewSend,
                    R.id.voicePreviewSpeed, R.id.voicePreviewWaveform, R.id.miniPlay, R.id.miniSpeed, R.id.miniClose)) {
                    val view = activity.findViewById<View>(id)
                    val rectangle = Rect()
                    assertTrue("Control $id must remain visible", view.getGlobalVisibleRect(rectangle))
                    assertTrue("Control $id needs a 48dp hit target", view.width >= minimum && view.height >= minimum)
                    assertEquals("Control $id must not be clipped", view.height, rectangle.height())
                    assertEquals("Control $id must not be clipped horizontally", view.width, rectangle.width())
                    assertTrue("Control $id must stay above the keyboard", rectangle.bottom <= frame.bottom)
                    assertFalse("Control $id needs a TalkBack label", view.contentDescription.isNullOrBlank())
                }
                val header = activity.findViewById<View>(R.id.chatHeaderBar)
                val headerRectangle = Rect()
                assertTrue("The chat header must remain visible", header.getGlobalVisibleRect(headerRectangle))
                assertEquals("The chat header must not be clipped", header.height, headerRectangle.height())
                val previewRectangle = Rect()
                assertTrue(activity.findViewById<View>(R.id.voiceDraft).getGlobalVisibleRect(previewRectangle))
                assertTrue("The voice preview must not cover the chat header: preview=$previewRectangle, header=$headerRectangle", previewRectangle.top >= headerRectangle.bottom)
            }
        } finally {
            instrumentation.runOnMainSync { app.audioPlayback.stop() }
            scenario.close()
            runBlocking { ComposerAttachmentStore(app, app.chatCacheRepository).clearScope(scope) }
            source.delete()
            preferences.socketBeacon = oldBeacon
            preferences.userId = oldUser
            instrumentation.runOnMainSync { AppCompatDelegate.setDefaultNightMode(oldTheme) }
        }
    }

    private fun await(condition: () -> Boolean) {
        val deadline = android.os.SystemClock.elapsedRealtime() + 15_000L
        while (!condition()) {
            if (android.os.SystemClock.elapsedRealtime() >= deadline) throw AssertionError("Timed out waiting for voice layout")
            Thread.sleep(50L)
        }
    }
}
