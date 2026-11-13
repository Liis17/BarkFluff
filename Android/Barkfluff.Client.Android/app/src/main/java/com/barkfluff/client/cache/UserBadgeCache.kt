package com.barkfluff.client.cache

import com.barkfluff.client.domain.model.UserBadgeInfo
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

interface UserBadgeCache {
    fun observeUserBadges(userId: Long): Flow<List<UserBadgeInfo>>
    suspend fun readUserBadges(userId: Long): List<UserBadgeInfo>
    suspend fun replaceUserBadges(userId: Long, badges: List<UserBadgeInfo>)
    suspend fun updatePrimaryUserBadge(userId: Long, badgeId: Int, primaryBadge: UserBadgeInfo?)
}

class RoomUserBadgeCache(private val cache: ChatCacheRepository) : UserBadgeCache {
    override fun observeUserBadges(userId: Long): Flow<List<UserBadgeInfo>> =
        cache.observeUserBadges(userId).map { badges -> badges.map(CachedUserBadgeEntity::toDomain) }

    override suspend fun readUserBadges(userId: Long): List<UserBadgeInfo> =
        cache.readUserBadges(userId).map(CachedUserBadgeEntity::toDomain)

    override suspend fun replaceUserBadges(userId: Long, badges: List<UserBadgeInfo>) =
        cache.replaceUserBadges(userId, badges)

    override suspend fun updatePrimaryUserBadge(userId: Long, badgeId: Int, primaryBadge: UserBadgeInfo?) =
        cache.updatePrimaryUserBadge(userId, badgeId, primaryBadge)
}
