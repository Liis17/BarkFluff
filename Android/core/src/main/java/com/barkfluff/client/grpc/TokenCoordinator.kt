package com.barkfluff.client.grpc

import android.content.Context
import com.barkfluff.client.data.GlobalParam
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * The small seam used by streams, workers and repositories before making an
 * authenticated request.
 *
 * Implementations own refresh serialization. Callers must not implement their
 * own retry/refresh lock around this interface.
 */
interface TokenCoordinator {
    suspend fun ensureValid(forceRefresh: Boolean = false): Boolean
}

/** Token values exchanged with the Identity service. */
data class TokenRefreshResult(
    val accessToken: String,
    val accessTokenExpiration: Long,
    val refreshToken: String,
    val refreshTokenExpiration: Long,
)

/** Narrow persistence seam for token refresh tests. */
interface TokenStore {
    var accessToken: String?
    var accessTokenExpiration: Long
    var refreshToken: String?
    var refreshTokenExpiration: Long
    val identityAddress: String
}

/** Production token store backed by the existing encrypted GlobalParam storage. */
class GlobalParamTokenStore(context: Context) : TokenStore {
    private val globalParam = GlobalParam(context.applicationContext)

    override var accessToken: String?
        get() = globalParam.accessToken
        set(value) {
            globalParam.accessToken = value
        }

    override var accessTokenExpiration: Long
        get() = globalParam.accessTokenExpiration
        set(value) {
            globalParam.accessTokenExpiration = value
        }

    override var refreshToken: String?
        get() = globalParam.refreshToken
        set(value) {
            globalParam.refreshToken = value
        }

    override var refreshTokenExpiration: Long
        get() = globalParam.refreshTokenExpiration
        set(value) {
            globalParam.refreshTokenExpiration = value
        }

    override val identityAddress: String
        get() = globalParam.socketIdentity
}

/**
 * Single implementation of the access-token freshness policy.
 *
 * The identity client creation and token RPC are injected so this module can be
 * tested without a gRPC channel or Android Activity.
 */
class GrpcTokenCoordinator(
    private val store: TokenStore,
    private val ensureIdentityClient: () -> Boolean,
    private val refreshAccessToken: suspend (refreshToken: String, expiration: Long) -> Result<TokenRefreshResult>,
    private val nowMillis: () -> Long = { System.currentTimeMillis() },
) : TokenCoordinator {

    companion object {
        const val TOKEN_BUFFER_MINUTES = 5
    }

    private val refreshMutex = Mutex()
    /** Monotonic generation lets concurrent force-refresh callers share one RPC. */
    private var refreshGeneration = 0L

    override suspend fun ensureValid(forceRefresh: Boolean): Boolean {
        val tokenBeforeRefresh = store.accessToken
        val generationBeforeRefresh = refreshGeneration
        val bufferMs = TOKEN_BUFFER_MINUTES * 60 * 1000L
        val expiration = store.accessTokenExpiration

        if (!forceRefresh && expiration > 0 && nowMillis() + bufferMs < expiration) {
            return true
        }

        return refreshMutex.withLock {
            val currentExpiration = store.accessTokenExpiration
            val currentToken = store.accessToken

            // Another caller can have refreshed while this caller waited. This check is also
            // intentionally applied to force-refresh requests: a burst of UNAUTHENTICATED
            // responses must result in one Identity RPC, not one RPC per failing stream.
            if (refreshGeneration != generationBeforeRefresh ||
                (!forceRefresh && currentExpiration > 0 && nowMillis() + bufferMs < currentExpiration)
            ) {
                return@withLock true
            }
            if (!tokenBeforeRefresh.isNullOrBlank() && currentToken != tokenBeforeRefresh) {
                return@withLock true
            }

            val refreshToken = store.refreshToken
            if (refreshToken.isNullOrBlank() || store.identityAddress.isBlank()) {
                return@withLock false
            }
            if (!ensureIdentityClient()) {
                return@withLock false
            }

            val result = refreshAccessToken(refreshToken, store.refreshTokenExpiration)
            if (result.isFailure) return@withLock false

            val refreshed = result.getOrNull() ?: return@withLock false
            store.accessToken = refreshed.accessToken
            store.accessTokenExpiration = refreshed.accessTokenExpiration
            store.refreshToken = refreshed.refreshToken
            store.refreshTokenExpiration = refreshed.refreshTokenExpiration
            refreshGeneration += 1
            true
        }
    }
}
