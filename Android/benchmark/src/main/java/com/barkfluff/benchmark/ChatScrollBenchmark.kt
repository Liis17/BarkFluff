package com.barkfluff.benchmark

import androidx.benchmark.macro.CompilationMode
import androidx.benchmark.macro.ExperimentalMetricApi
import androidx.benchmark.macro.FrameTimingMetric
import androidx.benchmark.macro.TraceSectionMetric
import androidx.benchmark.macro.junit4.MacrobenchmarkRule
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
        var firstSetup = true
        benchmarkRule.measureRepeated(
            packageName = TARGET_PACKAGE,
            metrics = listOf(
                FrameTimingMetric(),
                TraceSectionMetric("RV OnBindView", TraceSectionMetric.Mode.Sum),
            ),
            compilationMode = compilationMode,
            iterations = 5,
            setupBlock = {
                if (firstSetup) {
                    pressHome()
                    startActivityAndWait()
                    firstSetup = false
                } else {
                    pressBack()
                }
                waitForChatList()
                firstChatCard().click()
                waitForMessages()
            },
        ) {
            repeat(4) { waitForMessages().fling(Direction.UP) }
            repeat(4) { waitForMessages().fling(Direction.DOWN) }
            device.waitForIdle(5_000L)
        }
    }

    companion object {
        @JvmStatic
        @Parameterized.Parameters(name = "{0}")
        fun compilationModes(): Collection<Array<Any>> = BenchmarkCompilationModes.parameters()
    }
}
