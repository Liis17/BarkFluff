package com.barkfluff.benchmark

import androidx.benchmark.macro.CompilationMode
import androidx.benchmark.macro.BaselineProfileMode
import androidx.benchmark.macro.MacrobenchmarkScope
import androidx.test.uiautomator.UiObject2

internal const val TARGET_PACKAGE = "com.barkfluff.nightly"
internal const val UI_WAIT_TIMEOUT_MS = 10_000L
internal const val UI_POLL_INTERVAL_MS = 100L

internal object BenchmarkCompilationModes {
    fun parameters(): Collection<Array<Any>> = listOf(
        arrayOf<Any>(CompilationMode.None()),
        arrayOf<Any>(
            CompilationMode.Partial(
                baselineProfileMode = BaselineProfileMode.Disable,
                warmupIterations = 3,
            )
        ),
    )
}

internal fun MacrobenchmarkScope.waitForChatList() {
    val chatTitle = onElementOrNull(
        timeoutMs = UI_WAIT_TIMEOUT_MS,
        pollIntervalMs = UI_POLL_INTERVAL_MS,
    ) {
        viewIdResourceName == "$TARGET_PACKAGE:id/chatTitle"
    }
    check(chatTitle != null) {
        "Не найден список чатов. Залогиньтесь в тестовый аккаунт в benchmark-сборке " +
            "и выдайте POST_NOTIFICATIONS."
    }
}

internal fun MacrobenchmarkScope.firstChatCard(): UiObject2 = onElementOrNull(
    timeoutMs = UI_WAIT_TIMEOUT_MS,
    pollIntervalMs = UI_POLL_INTERVAL_MS,
) {
    viewIdResourceName == "$TARGET_PACKAGE:id/chatCard"
} ?: error("Не найден chatCard в списке чатов тестового аккаунта.")

internal fun MacrobenchmarkScope.waitForMessages(): UiObject2 = onElementOrNull(
    timeoutMs = UI_WAIT_TIMEOUT_MS,
    pollIntervalMs = UI_POLL_INTERVAL_MS,
) {
    viewIdResourceName == "$TARGET_PACKAGE:id/messagesRecyclerView" && childCount > 0
} ?: error("Не появились сообщения в messagesRecyclerView за ${UI_WAIT_TIMEOUT_MS}ms.")
