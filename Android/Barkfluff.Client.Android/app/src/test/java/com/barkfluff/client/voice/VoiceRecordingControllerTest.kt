package com.barkfluff.client.voice

import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertFalse
import org.junit.Test

class VoiceRecordingControllerTest {
    @Test
    fun lockingKeepsRecordingAfterFingerIsReleasedAndStopProducesPreview() {
        var now = 100L
        val device = FakeRecordingDevice()
        val recording = VoiceRecordingController(device) { now }

        recording.start()
        recording.drag(0f, -72f)
        now = 900L

        assertNull(recording.release())
        assertEquals(VoiceRecordingMode.LOCKED, recording.state.value.mode)
        assertEquals(0, device.stopCount)
        assertEquals(800L, recording.stop()?.durationMillis)
        assertEquals(1, device.stopCount)
        assertEquals(VoiceRecordingMode.IDLE, recording.state.value.mode)
        device.file.delete()
    }

    private class FakeRecordingDevice : VoiceRecordingDevice {
        val file = File.createTempFile("voice-test", ".ogg").apply { writeBytes(byteArrayOf(1)) }
        var stopCount = 0
        override fun start() = Unit
        override fun pause() = Unit
        override fun resume() = Unit
        override fun stop(): File { stopCount++; return file }
        override fun cancel() { file.delete() }
    }

    @Test
    fun ordinaryReleaseProducesOneClipAndRepeatedReleaseCannotSendAgain() {
        var now = 0L
        val device = FakeRecordingDevice()
        val recording = VoiceRecordingController(device) { now }
        recording.start()
        now = 800L
        assertEquals(device.file, recording.release()?.file)
        assertNull(recording.release())
        assertEquals(1, device.stopCount)
        device.file.delete()
    }

    @Test
    fun pauseIsExcludedFromRecordedDuration() {
        var now = 0L
        val device = FakeRecordingDevice()
        val recording = VoiceRecordingController(device) { now }
        recording.start(locked = true)
        now = 400L
        recording.pause()
        now = 30_000L
        recording.refreshDuration()
        assertEquals(400L, recording.state.value.durationMillis)
        recording.resume()
        now = 30_400L
        assertEquals(800L, recording.stop()?.durationMillis)
        device.file.delete()
    }

    @Test
    fun horizontalCancelAndLeavingPausedRecordingDeleteTheClip() {
        val device = FakeRecordingDevice()
        val recording = VoiceRecordingController(device) { 1_000L }
        recording.start()
        recording.drag(-72f, -10f)
        assertNull(recording.release())
        assertFalse(device.file.exists())
        assertEquals(0, device.stopCount)

        val pausedDevice = FakeRecordingDevice()
        val paused = VoiceRecordingController(pausedDevice) { 1_000L }
        paused.start(locked = true)
        paused.pause()
        paused.cancel()
        assertFalse(pausedDevice.file.exists())
        assertEquals(VoiceRecordingMode.IDLE, paused.state.value.mode)
    }

    @Test
    fun shortRecordingIsDiscarded() {
        var now = 0L
        val device = FakeRecordingDevice()
        val recording = VoiceRecordingController(device) { now }
        recording.start()
        now = 499L
        assertNull(recording.release())
        assertFalse(device.file.exists())
    }
}
