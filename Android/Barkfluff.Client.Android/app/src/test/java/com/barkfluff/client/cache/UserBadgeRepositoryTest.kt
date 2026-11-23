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
import org.junit.Assert.assertNull
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

    @Test
    fun `concurrent primary refreshes share one request and stay cached until ttl`() = runTest {
        var now = 0L
        val gateway = CountingBadgeGateway(listOf(badge(1), badge(2, isPrimary = true)))
        val repository = UserBadgeRepository(MemoryBadgeCache(), gateway, backgroundScope, nowMillis = { now })

        val first = async { repository.refreshPrimary(USER_ID) }
        val second = async { repository.refreshPrimary(USER_ID) }
        runCurrent()
        gateway.release.complete(Unit)

        assertEquals(2, first.await()?.badgeId)
        assertEquals(2, second.await()?.badgeId)
        assertEquals(2, repository.peekPrimary(USER_ID)?.badgeId)
        assertEquals(1, gateway.calls)

        now += UserBadgeRepository.PRIMARY_TTL_MILLIS - 1
        repository.refreshPrimary(USER_ID)
        assertEquals(1, gateway.calls)

        now += 1
        repository.refreshPrimary(USER_ID)
        assertEquals(2, gateway.calls)
    }

    @Test
    fun `failed primary refresh keeps cached badge and backs off`() = runTest {
        var now = 0L
        val cache = MemoryBadgeCache().apply { badges = listOf(badge(3, isPrimary = true)) }
        val gateway = CountingBadgeGateway(emptyList(), failure = IllegalStateException("offline"))
        gateway.release.complete(Unit)
        val repository = UserBadgeRepository(cache, gateway, backgroundScope, nowMillis = { now })

        assertEquals(3, repository.cachedPrimary(USER_ID)?.badgeId)
        assertEquals(3, repository.refreshPrimary(USER_ID)?.badgeId)
        repository.refreshPrimary(USER_ID)
        assertEquals(1, gateway.calls)

        now += UserBadgeRepository.PRIMARY_RETRY_MILLIS
        repository.refreshPrimary(USER_ID)
        assertEquals(2, gateway.calls)
    }

    @Test
    fun `primary badge is not reused across cache scopes`() = runTest {
        val cache = MemoryBadgeCache()
        val gateway = CountingBadgeGateway(listOf(badge(1, isPrimary = true)))
        gateway.release.complete(Unit)
        val repository = UserBadgeRepository(cache, gateway, backgroundScope)

        repository.refreshPrimary(USER_ID)
        cache.scope = "other-node|7"

        assertNull(repository.peekPrimary(USER_ID))
    }

    private class CountingBadgeGateway(
        private val badges: List<UserBadgeInfo>,
        private val failure: Exception? = null,
    ) : UserBadgeGateway {
        val release = CompletableDeferred<Unit>()
        var calls = 0

        override suspend fun badges(userId: Long): Result<List<UserBadgeInfo>> {
            calls++
            release.await()
            return failure?.let { Result.failure(it) } ?: Result.success(badges)
        }

        override suspend fun setPrimary(badgeId: Int): Result<UserBadgeInfo?> = Result.success(null)
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
        var scope: String? = "node|1"

        override fun scopeKey(): String? = scope

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
