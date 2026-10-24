package com.barkfluff.benchmark

import androidx.benchmark.macro.CompilationMode
import androidx.benchmark.macro.ExperimentalMetricApi
import androidx.benchmark.macro.FrameTimingMetric
import androidx.benchmark.macro.TraceSectionMetric
import androidx.benchmark.macro.junit4.MacrobenchmarkRule
import androidx.test.uiautomator.By
import androidx.test.uiautomator.Direction
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.Parameterized

@RunWith(Parameterized::class)
class ChatScrollBenchmark(private val compilationMode: CompilationMode) {
    @get:Rule
    val benchmarkRule = MacrobenchmarkRule()

    @OptIn(ExperimentalMetricApi::class)
    @Test
    fun scrollFirstChat() {
        benchmarkRule.measureRepeated(
            packageName = TARGET_PACKAGE,
            metrics = listOf(
                FrameTimingMetric(),
                TraceSectionMetric("RV onBindViewHolder%", TraceSectionMetric.Mode.Sum),
                TraceSectionMetric("RV onCreateViewHolder%", TraceSectionMetric.Mode.Sum),
            ),
            compilationMode = compilationMode,
            iterations = 5,
            setupBlock = {
                ensureChatList()
                firstChatCard().click()
                waitForMessages()
            },
        ) {
            val messages = waitForMessages()
            val listBounds = messages.visibleBounds
            val headerBounds = device.findObject(By.res("$TARGET_PACKAGE:id/chatHeaderBar"))?.visibleBounds
            val inputBounds = device.findObject(By.res("$TARGET_PACKAGE:id/inputBar"))?.visibleBounds
            val gestureMargins = if (headerBounds != null && inputBounds != null) {
                val top = headerBounds.bottom - listBounds.top + 1
                val bottom = listBounds.bottom - inputBounds.top + 1
                if (top >= 0 && bottom >= 0 && top + bottom < listBounds.height()) top to bottom else null
            } else {
                null
            }
            val flingMessages: (Direction) -> Unit = { direction ->
                waitForMessages().apply {
                    if (gestureMargins != null) {
                        setGestureMargins(0, gestureMargins.first, 0, gestureMargins.second)
                    } else {
                        setGestureMarginPercentage(0.15f)
                    }
                }.fling(direction)
            }

            // UiObject2.fling scrolls in the named direction and swipes opposite (2.4.0 source).
            // ChatActivity.kt uses stackFromEnd=true, so DOWN reveals older messages.
            repeat(4) { flingMessages(Direction.DOWN) }
            repeat(4) { flingMessages(Direction.UP) }
            device.waitForIdle(5_000L)
        }
    }

    companion object {
        @JvmStatic
        @Parameterized.Parameters(name = "{0}")
        fun compilationModes(): Collection<Array<Any>> = BenchmarkCompilationModes.parameters()
    }
}
