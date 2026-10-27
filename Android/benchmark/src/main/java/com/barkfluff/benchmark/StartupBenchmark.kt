package com.barkfluff.benchmark

import androidx.benchmark.macro.CompilationMode
import androidx.benchmark.macro.ExperimentalMetricApi
import androidx.benchmark.macro.StartupMode
import androidx.benchmark.macro.StartupTimingMetric
import androidx.benchmark.macro.TraceSectionMetric
import androidx.benchmark.macro.junit4.MacrobenchmarkRule
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.Parameterized

@RunWith(Parameterized::class)
class StartupBenchmark(private val compilationMode: CompilationMode) {
    @get:Rule
    val benchmarkRule = MacrobenchmarkRule()

    @OptIn(ExperimentalMetricApi::class)
    @Test
    fun coldStartup() = benchmarkRule.measureRepeated(
        packageName = TARGET_PACKAGE,
        metrics = listOf(
            StartupTimingMetric(),
            TraceSectionMetric("BF:appToChats", TraceSectionMetric.Mode.First),
            TraceSectionMetric("BF:Application.onCreate", TraceSectionMetric.Mode.First),
        ),
        compilationMode = compilationMode,
        startupMode = StartupMode.COLD,
        iterations = 10,
        setupBlock = { pressHome() },
    ) {
        startActivityAndWait()
        waitForChatList()
    }

    companion object {
        @JvmStatic
        @Parameterized.Parameters(name = "{0}")
        fun compilationModes(): Collection<Array<Any>> = BenchmarkCompilationModes.parameters()
    }
}
