package com.barkfluff.client.audio

import android.content.ComponentName
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import androidx.lifecycle.Lifecycle
import androidx.media3.session.MediaController
import androidx.media3.session.SessionToken
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.LoginActivity
import com.barkfluff.client.voice.VoicePlaybackSpeed
import java.io.File
import java.util.concurrent.TimeUnit
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AudioPlaybackTest {
    @Test
    fun sessionControlsSpeedPausedPositionAndBackgroundShareOnePlayer() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val app = instrumentation.targetContext.applicationContext as BarkFluffApplication
        val playback = app.audioPlayback
        val preferences = app.getSharedPreferences("voice_playback", 0)
        val previousSpeed = VoicePlaybackSpeed.read(app)
        val file = File.createTempFile("playback-test", ".ogg", app.cacheDir)
        instrumentation.context.assets.open("voice_playback.ogg").use { source -> file.outputStream().use(source::copyTo) }
        var controller: MediaController? = null
        val scenario = ActivityScenario.launch(LoginActivity::class.java)
        try {
            instrumentation.runOnMainSync {
                preferences.edit().putFloat("speed", 1f).commit()
                playback.play(AudioTrack("test-voice", senderName = "Sender"), file)
            }
            await { playback.state.value.isPlaying && playback.state.value.durationMillis > 0L }
            val future = arrayOfNulls<com.google.common.util.concurrent.ListenableFuture<MediaController>>(1)
            instrumentation.runOnMainSync {
                future[0] = MediaController.Builder(app, SessionToken(app, ComponentName(app, VoicePlaybackService::class.java))).buildAsync()
            }
            controller = future[0]!!.get(10L, TimeUnit.SECONDS)
            val system = controller
            instrumentation.runOnMainSync { system.pause(); system.seekTo(4_000L) }
            await { !playback.state.value.isPlaying && playback.state.value.positionMillis >= 4_000L }
            val paused = playback.state.value.positionMillis
            Thread.sleep(400L)
            assertEquals(paused, playback.state.value.positionMillis)

            instrumentation.runOnMainSync { playback.cycleSpeed() }
            await { playback.state.value.speed == 1.5f }
            awaitMain { system.playbackParameters.speed == 1.5f }
            instrumentation.runOnMainSync {
                assertEquals(1.5f, system.playbackParameters.speed, 0f)
                assertEquals(1f, system.playbackParameters.pitch, 0f)
            }
            // The preview uses the same stored choice; its changes must reach the session too.
            instrumentation.runOnMainSync { VoicePlaybackSpeed.cycle(app) }
            await { playback.state.value.speed == 2f }
            awaitMain { system.playbackParameters.speed == 2f }
            instrumentation.runOnMainSync { assertEquals(2f, system.playbackParameters.speed, 0f); system.play() }
            await { playback.state.value.isPlaying }
            scenario.moveToState(Lifecycle.State.CREATED)
            val backgroundPosition = playback.state.value.positionMillis
            await { playback.state.value.positionMillis > backgroundPosition + 300L }
            assertTrue(playback.state.value.isPlaying)
            scenario.moveToState(Lifecycle.State.RESUMED)

            val audioManager = app.getSystemService(AudioManager::class.java)
            val focus = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN_TRANSIENT)
                .setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_VOICE_COMMUNICATION).build())
                .setOnAudioFocusChangeListener { }
                .build()
            instrumentation.runOnMainSync { assertEquals(AudioManager.AUDIOFOCUS_REQUEST_GRANTED, audioManager.requestAudioFocus(focus)) }
            await { !playback.state.value.isPlaying }
            instrumentation.runOnMainSync { audioManager.abandonAudioFocusRequest(focus) }
            Thread.sleep(400L)
            assertFalse(playback.state.value.isPlaying)
            instrumentation.runOnMainSync { playback.resume() }
            await { playback.state.value.isPlaying }
            instrumentation.runOnMainSync { playback.setRecordingActive(true); system.play() }
            await { !playback.state.value.isPlaying }
            instrumentation.runOnMainSync { playback.setRecordingActive(false) }
            assertFalse(playback.state.value.isPlaying)
        } finally {
            instrumentation.runOnMainSync { playback.setRecordingActive(false); playback.stop(); controller?.release(); preferences.edit().putFloat("speed", previousSpeed).commit() }
            scenario.close()
            file.delete()
        }
    }

    private fun awaitMain(condition: () -> Boolean) {
        await {
            var ready = false
            InstrumentationRegistry.getInstrumentation().runOnMainSync { ready = condition() }
            ready
        }
    }

    private fun await(condition: () -> Boolean) {
        val deadline = android.os.SystemClock.elapsedRealtime() + 10_000L
        while (!condition()) {
            if (android.os.SystemClock.elapsedRealtime() >= deadline) throw AssertionError("Timed out waiting for playback state")
            Thread.sleep(50L)
        }
    }
}
