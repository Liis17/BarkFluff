package com.barkfluff.client.cache

import com.barkfluff.client.domain.gateway.UserBadgeGateway
import com.barkfluff.client.domain.model.BadgeFeatureUnavailableException
import com.barkfluff.client.domain.model.UserBadgeInfo
import com.barkfluff.client.domain.model.splitUserBadges
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.sync.withPermit
import java.util.concurrent.ConcurrentHashMap

/** Кеш-first доступ к бейджам пользователя в рамках текущей сессии и выбранной ноды. */
class UserBadgeRepository(
    private val cache: UserBadgeCache,
    private val gateway: UserBadgeGateway,
    private val primaryLoadScope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.IO),
    private val nowMillis: () -> Long = { System.nanoTime() / 1_000_000L },
) {
    private val userOperationLocks = ConcurrentHashMap<Long, Mutex>()

    /** Основной бейдж в памяти: списки перерисовывают строки синхронно, без мигания значка. */
    private class PrimarySnapshot(val badge: UserBadgeInfo?, val validUntilMillis: Long)

    private val primarySnapshots = ConcurrentHashMap<String, PrimarySnapshot>()
    private val primaryRefreshes = ConcurrentHashMap<String, Deferred<UserBadgeInfo?>>()
    private val primaryRefreshPermits = Semaphore(PRIMARY_REFRESH_PARALLELISM)

    fun observe(userId: Long): Flow<List<UserBadgeInfo>> =
        cache.observeUserBadges(userId)

    suspend fun cached(userId: Long): List<UserBadgeInfo> =
        cache.readUserBadges(userId)

    /** Последний известный основной бейдж из памяти, без обращения к БД и сети. */
    fun peekPrimary(userId: Long): UserBadgeInfo? =
        primaryKey(userId)?.let { primarySnapshots[it]?.badge }

    /** Основной бейдж из памяти или SQLCipher-кеша; сетевое обновление — [refreshPrimary]. */
    suspend fun cachedPrimary(userId: Long): UserBadgeInfo? {
        val key = primaryKey(userId) ?: return null
        primarySnapshots[key]?.let { return it.badge }
        val stored = splitUserBadges(cache.readUserBadges(userId)).primary
        // Значение из БД считается устаревшим: первый же refreshPrimary сходит в сеть.
        return (primarySnapshots.putIfAbsent(key, PrimarySnapshot(stored, Long.MIN_VALUE)) ?: return stored).badge
    }

    /**
     * Актуальный основной бейдж. Сеть запрашивается не чаще раза в [PRIMARY_TTL_MILLIS] на пользователя,
     * одновременные запросы объединяются, а число параллельных GetUserBadges ограничено.
     */
    suspend fun refreshPrimary(userId: Long): UserBadgeInfo? {
        val key = primaryKey(userId) ?: return null
        primarySnapshots[key]?.takeIf { it.validUntilMillis > nowMillis() }?.let { return it.badge }
        val load = primaryRefreshes.computeIfAbsent(key) {
            primaryLoadScope.async(start = CoroutineStart.LAZY) {
                primaryRefreshPermits.withPermit {
                    val result = refresh(userId)
                    val error = result.exceptionOrNull()
                    if (error != null) {
                        // Ошибка не стирает известный бейдж; повтор — после паузы, чтобы не долбить ноду.
                        val retryAfter = if (error is BadgeFeatureUnavailableException) PRIMARY_TTL_MILLIS else PRIMARY_RETRY_MILLIS
                        val previous = primarySnapshots[key]?.badge
                        primarySnapshots[key] = PrimarySnapshot(previous, nowMillis() + retryAfter)
                    }
                    primarySnapshots[key]?.badge
                }
            }
        }
        load.invokeOnCompletion { primaryRefreshes.remove(key, load) }
        load.start()
        return load.await()
    }

    suspend fun refresh(userId: Long): Result<List<UserBadgeInfo>> {
        if (userId <= 0L) return Result.failure(IllegalArgumentException("Некорректный ID пользователя"))
        return try {
            userLock(userId).withLock {
                val remote = gateway.badges(userId)
                if (remote.isFailure) return@withLock remote
                val badges = remote.getOrThrow()
                cache.replaceUserBadges(userId, badges)
                rememberPrimary(userId, splitUserBadges(badges).primary)
                Result.success(badges)
            }
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            Result.failure(error)
        }
    }

    suspend fun setPrimary(userId: Long, badgeId: Int): Result<Unit> {
        if (userId <= 0L || badgeId < 0) {
            return Result.failure(IllegalArgumentException("Некорректные данные бейджа"))
        }
        return try {
            userLock(userId).withLock {
                val remote = gateway.setPrimary(badgeId)
                if (remote.isFailure) return@withLock Result.failure(remote.exceptionOrNull()!!)
                cache.updatePrimaryUserBadge(userId, badgeId, remote.getOrNull())
                rememberPrimary(userId, remote.getOrNull()?.copy(isPrimary = true))
                Result.success(Unit)
            }
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            Result.failure(error)
        }
    }

    private fun rememberPrimary(userId: Long, badge: UserBadgeInfo?) {
        val key = primaryKey(userId) ?: return
        primarySnapshots[key] = PrimarySnapshot(badge, nowMillis() + PRIMARY_TTL_MILLIS)
    }

    private fun primaryKey(userId: Long): String? {
        if (userId <= 0L) return null
        val scope = cache.scopeKey() ?: return null
        return "$scope|$userId"
    }

    private fun userLock(userId: Long): Mutex = userOperationLocks.computeIfAbsent(userId) { Mutex() }

    internal companion object {
        const val PRIMARY_TTL_MILLIS = 10 * 60_000L
        const val PRIMARY_RETRY_MILLIS = 60_000L
        const val PRIMARY_REFRESH_PARALLELISM = 4
    }
}
