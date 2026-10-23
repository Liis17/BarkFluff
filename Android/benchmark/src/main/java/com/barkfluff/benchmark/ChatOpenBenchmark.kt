package com.barkfluff.benchmark

import androidx.benchmark.macro.CompilationMode
import androidx.benchmark.macro.ExperimentalMetricApi
import androidx.benchmark.macro.FrameTimingMetric
import androidx.benchmark.macro.TraceSectionMetric
import androidx.benchmark.macro.junit4.MacrobenchmarkRule
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.Parameterized

@RunWith(Parameterized::class)
class ChatOpenBenchmark(private val compilationMode: CompilationMode) {
    @get:Rule
    val benchmarkRule = MacrobenchmarkRule()

    @OptIn(ExperimentalMetricApi::class)
    @Test
    fun openFirstChat() {
        var firstSetup = true
        benchmarkRule.measureRepeated(
            packageName = TARGET_PACKAGE,
            metrics = listOf(
                TraceSectionMetric("BF:chatOpen", TraceSectionMetric.Mode.First),
                FrameTimingMetric(),
            ),
            compilationMode = compilationMode,
            iterations = 10,
            setupBlock = {
                if (firstSetup) {
                    pressHome()
                    startActivityAndWait()
                    firstSetup = false
                } else {
                    pressBack()
                }
                waitForChatList()
            },
        ) {
            firstChatCard().click()
            waitForMessages()
        }
    }

    companion object {
        @JvmStatic
        @Parameterized.Parameters(name = "{0}")
        fun compilationModes(): Collection<Array<Any>> = BenchmarkCompilationModes.parameters()
    }
}
