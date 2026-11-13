package com.barkfluff.client.cache

import com.barkfluff.client.domain.gateway.UserBadgeGateway
import com.barkfluff.client.domain.model.UserBadgeInfo
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

/** Кеш-first доступ к бейджам пользователя в рамках текущей сессии и выбранной ноды. */
class UserBadgeRepository(
    private val cache: ChatCacheRepository,
    private val gateway: UserBadgeGateway,
) {
    fun observe(userId: Long): Flow<List<UserBadgeInfo>> =
        cache.observeUserBadges(userId).map { badges -> badges.map(CachedUserBadgeEntity::toDomain) }

    suspend fun cached(userId: Long): List<UserBadgeInfo> =
        cache.readUserBadges(userId).map(CachedUserBadgeEntity::toDomain)

    suspend fun refresh(userId: Long): Result<List<UserBadgeInfo>> {
        if (userId <= 0L) return Result.failure(IllegalArgumentException("Некорректный ID пользователя"))
        val remote = gateway.badges(userId)
        if (remote.isFailure) return remote
        return try {
            val badges = remote.getOrThrow()
            cache.replaceUserBadges(userId, badges)
            Result.success(badges)
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
        val remote = gateway.setPrimary(badgeId)
        if (remote.isFailure) return Result.failure(remote.exceptionOrNull()!!)
        return try {
            cache.updatePrimaryUserBadge(userId, badgeId, remote.getOrNull())
            Result.success(Unit)
        } catch (error: CancellationException) {
            throw error
        } catch (error: Exception) {
            Result.failure(error)
        }
    }
}
