package com.barkfluff.client

import org.junit.Assert.assertEquals
import org.junit.Test

class AndroidUiCheckerTest {
    @Test
    fun `Android UI source passes localization checks`() {
        val checkerScript = requireNotNull(System.getProperty("androidUiCheckerScript"))
        val process = ProcessBuilder("python3", checkerScript)
            .redirectErrorStream(true)
            .start()
        val output = process.inputStream.bufferedReader().use { it.readText() }
        val exitCode = process.waitFor()

        assertEquals("python3 tools/check_android_ui.py failed:\n$output", 0, exitCode)
    }
}
