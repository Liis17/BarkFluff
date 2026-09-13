package com.barkfluff.client

import com.barkfluff.client.data.ServerDataElement
import kotlinx.coroutines.withTimeoutOrNull

internal enum class ServerListState {
    LOADING,
    CONTENT,
    EMPTY,
    ERROR,
}

internal object ServerSelectionPolicy {
    fun listState(result: Result<List<ServerDataElement>>): ServerListState = result.fold(
        onSuccess = { servers ->
            if (servers.isEmpty()) ServerListState.EMPTY else ServerListState.CONTENT
        },
        onFailure = { ServerListState.ERROR },
    )

    suspend fun <T> withTimeout(timeoutMillis: Long, block: suspend () -> T): T? =
        withTimeoutOrNull(timeoutMillis) { block() }

    fun normalizeEndpoint(input: String, normalizer: (String) -> String): String? =
        runCatching { normalizer(input) }.getOrNull()?.takeIf { it.isNotBlank() }
}
