package com.barkfluff.client.cache

import com.barkfluff.client.domain.gateway.UserBadgeGateway
import com.barkfluff.client.domain.model.UserBadgeInfo
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class UserBadgeRepositoryTest {
    @Test
    fun `refresh started before primary change cannot overwrite the successful selection`() = runTest {
        val firstBadge = badge(1, isPrimary = true)
        val secondBadge = badge(2)
        val cache = MemoryBadgeCache()
        val gateway = DelayedBadgeGateway(
            staleBadges = listOf(firstBadge, secondBadge),
            selectedBadge = secondBadge.copy(isPrimary = true),
        )
        val repository = UserBadgeRepository(cache, gateway)

        val refresh = async { repository.refresh(USER_ID) }
        gateway.refreshStarted.await()
        val selection = async { repository.setPrimary(USER_ID, secondBadge.badgeId) }
        runCurrent()

        assertFalse("Выбор должен ждать уже начатое обновление", gateway.selectionStarted.isCompleted)

        gateway.releaseRefresh.complete(Unit)
        assertTrue(refresh.await().isSuccess)
        assertTrue(selection.await().isSuccess)
        assertEquals(listOf(false, true), cache.badges.map(UserBadgeInfo::isPrimary))
    }

    private class DelayedBadgeGateway(
        private val staleBadges: List<UserBadgeInfo>,
        private val selectedBadge: UserBadgeInfo,
    ) : UserBadgeGateway {
        val refreshStarted = CompletableDeferred<Unit>()
        val releaseRefresh = CompletableDeferred<Unit>()
        val selectionStarted = CompletableDeferred<Unit>()

        override suspend fun badges(userId: Long): Result<List<UserBadgeInfo>> {
            refreshStarted.complete(Unit)
            releaseRefresh.await()
            return Result.success(staleBadges)
        }

        override suspend fun setPrimary(badgeId: Int): Result<UserBadgeInfo?> {
            selectionStarted.complete(Unit)
            return Result.success(selectedBadge)
        }
    }

    private class MemoryBadgeCache : UserBadgeCache {
        var badges: List<UserBadgeInfo> = emptyList()

        override fun observeUserBadges(userId: Long): Flow<List<UserBadgeInfo>> = flowOf(badges)

        override suspend fun readUserBadges(userId: Long): List<UserBadgeInfo> = badges

        override suspend fun replaceUserBadges(userId: Long, badges: List<UserBadgeInfo>) {
            this.badges = badges
        }

        override suspend fun updatePrimaryUserBadge(userId: Long, badgeId: Int, primaryBadge: UserBadgeInfo?) {
            badges = badges.map { it.copy(isPrimary = it.badgeId == badgeId) }
        }
    }

    private companion object {
        const val USER_ID = 42L

        fun badge(id: Int, isPrimary: Boolean = false) = UserBadgeInfo(
            badgeId = id,
            name = "Badge $id",
            description = "",
            imageUrl = "https://example.test/$id.png",
            miniImageUrl = "https://example.test/$id-mini.png",
            assignedAtMillis = id.toLong(),
            isPrimary = isPrimary,
        )
    }
}
