package com.barkfluff.client.cache

import com.barkfluff.client.domain.gateway.UserBadgeGateway
import com.barkfluff.client.domain.model.UserBadgeInfo
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.util.concurrent.ConcurrentHashMap

/** Кеш-first доступ к бейджам пользователя в рамках текущей сессии и выбранной ноды. */
class UserBadgeRepository(
    private val cache: UserBadgeCache,
    private val gateway: UserBadgeGateway,
) {
    private val userOperationLocks = ConcurrentHashMap<Long, Mutex>()

    fun observe(userId: Long): Flow<List<UserBadgeInfo>> =
        cache.observeUserBadges(userId)

    suspend fun cached(userId: Long): List<UserBadgeInfo> =
        cache.readUserBadges(userId)

    suspend fun refresh(userId: Long): Result<List<UserBadgeInfo>> {
        if (userId <= 0L) return Result.failure(IllegalArgumentException("Некорректный ID пользователя"))
        return try {
            userLock(userId).withLock {
                val remote = gateway.badges(userId)
                if (remote.isFailure) return@withLock remote
                val badges = remote.getOrThrow()
                cache.replaceUserBadges(userId, badges)
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
                Result.success(Unit)
            }
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            Result.failure(error)
        }
    }

    private fun userLock(userId: Long): Mutex = userOperationLocks.computeIfAbsent(userId) { Mutex() }
}
