package com.barkfluff.client.stickers

import barkfluff.files.FilesApiOuterClass.StickerInfo
import barkfluff.files.FilesApiOuterClass.StickerPackInfo
import com.barkfluff.client.adapter.StickerPanelItem
import com.barkfluff.client.domain.gateway.StickerGateway
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.job
import kotlinx.coroutines.test.advanceUntilIdle
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

@OptIn(ExperimentalCoroutinesApi::class)
class StickerPanelControllerTest {
    @Test
    fun listFailureShowsErrorAndAnOpenRetriesUntilAuthoritativeEmpty() = runTest {
        var requestCount = 0
        val gateway = FakeStickerGateway(
            packsHandler = {
                requestCount++
                if (requestCount == 1) Result.failure(IllegalStateException("offline"))
                else Result.success(emptyList())
            }
        )
        val rendered = mutableListOf<List<StickerPanelItem>>()
        var clearCount = 0
        val controller = controller(
            scope = this,
            gateway = gateway,
            rendered = rendered,
            clearCache = { clearCount++ },
        )

        controller.open()
        assertEquals(listOf(StickerPanelItem.Loading), rendered.last())
        advanceUntilIdle()

        assertEquals(listOf(StickerPanelItem.Error), rendered.last())
        assertFalse(controller.loaded)
        assertEquals(0, clearCount)

        controller.open()
        assertEquals(listOf(StickerPanelItem.Loading), rendered.last())
        advanceUntilIdle()

        assertEquals(listOf(StickerPanelItem.Empty), rendered.last())
        assertTrue(controller.loaded)
        assertEquals(2, gateway.packsCalls)
        assertEquals(1, clearCount)
    }

    @Test
    fun packFailureKeepsCachedSnapshotAndDoesNotSavePartialData() = runTest {
        val oldSnapshot = listOf(header("old"))
        var cache: List<StickerPanelItem> = oldSnapshot
        val gateway = FakeStickerGateway(
            packsHandler = { Result.success(listOf(pack("first"), pack("second"))) },
            stickerPackHandler = { packId ->
                if (packId == "first") Result.success(listOf(sticker("partial", packId)))
                else Result.failure(IllegalStateException("offline"))
            },
        )
        val rendered = mutableListOf<List<StickerPanelItem>>()
        val saved = mutableListOf<List<StickerPanelItem>>()
        val controller = controller(
            scope = this,
            gateway = gateway,
            cached = { cache },
            saveCache = { cache = it; saved += it },
            rendered = rendered,
        )

        controller.open()
        assertEquals(oldSnapshot, rendered.last())
        advanceUntilIdle()

        assertEquals(oldSnapshot + StickerPanelItem.Error, rendered.last())
        assertEquals(oldSnapshot, cache)
        assertTrue(saved.isEmpty())
        assertEquals(listOf("first", "second"), gateway.stickerPackIds)
    }

    @Test
    fun completeSuccessPublishesAndCachesOnlyContentRows() = runTest {
        val gateway = FakeStickerGateway(
            packsHandler = { Result.success(listOf(pack("pack-a"), pack("pack-b"))) },
            stickerPackHandler = { packId ->
                Result.success(if (packId == "pack-a") listOf(sticker("sticker-a", packId)) else emptyList())
            },
        )
        val rendered = mutableListOf<List<StickerPanelItem>>()
        val saved = mutableListOf<List<StickerPanelItem>>()
        val controller = controller(
            scope = this,
            gateway = gateway,
            saveCache = { saved += it },
            rendered = rendered,
        )

        controller.open()
        advanceUntilIdle()

        val expected = listOf(header("pack-a"), stickerItem("sticker-a", "pack-a"), header("pack-b"))
        assertEquals(expected, rendered.last())
        assertEquals(listOf(expected), saved)
        assertTrue(controller.loaded)
    }

    @Test
    fun successfulEmptyPacksClearCacheAndAreNotFetchedAgain() = runTest {
        var cache: List<StickerPanelItem>? = listOf(header("old"))
        var clearCount = 0
        val gateway = FakeStickerGateway(packsHandler = { Result.success(emptyList()) })
        val rendered = mutableListOf<List<StickerPanelItem>>()
        val controller = controller(
            scope = this,
            gateway = gateway,
            cached = { cache },
            clearCache = { clearCount++; cache = null },
            rendered = rendered,
        )

        controller.open()
        advanceUntilIdle()

        assertEquals(listOf(StickerPanelItem.Empty), rendered.last())
        assertEquals(null, cache)
        assertTrue(controller.loaded)
        controller.open()
        advanceUntilIdle()

        assertEquals(1, gateway.packsCalls)
        assertEquals(1, clearCount)
        assertEquals(listOf(StickerPanelItem.Empty), rendered.last())
    }

    @Test
    fun cachedContentIsShownImmediatelyAndListFailureKeepsErrorWithoutAutoReload() = runTest {
        val cached = listOf(header("cached"))
        val gateway = FakeStickerGateway(
            packsHandler = { Result.failure(IllegalStateException("offline")) }
        )
        val rendered = mutableListOf<List<StickerPanelItem>>()
        val saved = mutableListOf<List<StickerPanelItem>>()
        val controller = controller(
            scope = this,
            gateway = gateway,
            cached = { cached },
            saveCache = { saved += it },
            rendered = rendered,
        )

        controller.open()
        assertEquals(cached, rendered.last())
        advanceUntilIdle()

        assertEquals(cached + StickerPanelItem.Error, rendered.last())
        assertEquals(0, saved.size)
        controller.open()
        advanceUntilIdle()

        assertEquals(1, gateway.packsCalls)
        assertEquals(cached + StickerPanelItem.Error, rendered.last())
    }

    @Test
    fun retryReplacesErrorWithLoadingAndCanRecover() = runTest {
        var requestCount = 0
        val gateway = FakeStickerGateway(
            packsHandler = {
                requestCount++
                if (requestCount == 1) Result.failure(IllegalStateException("offline"))
                else Result.success(listOf(pack("recovered")))
            },
            stickerPackHandler = { Result.success(listOf(sticker("ok", it))) },
        )
        val rendered = mutableListOf<List<StickerPanelItem>>()
        val controller = controller(scope = this, gateway = gateway, rendered = rendered)

        controller.open()
        advanceUntilIdle()
        assertEquals(listOf(StickerPanelItem.Error), rendered.last())

        controller.retry()
        assertEquals(listOf(StickerPanelItem.Loading), rendered.last())
        advanceUntilIdle()

        assertEquals(listOf(header("recovered"), stickerItem("ok", "recovered")), rendered.last())
        assertEquals(2, gateway.packsCalls)
        assertTrue(controller.loaded)
    }

    @Test
    fun retryKeepsCachedContentVisibleWhileLoadingAndRecovers() = runTest {
        val cached = listOf(header("cached"))
        var requestCount = 0
        val gateway = FakeStickerGateway(
            packsHandler = {
                requestCount++
                if (requestCount == 1) Result.failure(IllegalStateException("offline"))
                else Result.success(listOf(pack("fresh")))
            },
            stickerPackHandler = { Result.success(listOf(sticker("fresh-sticker", it))) },
        )
        val rendered = mutableListOf<List<StickerPanelItem>>()
        val controller = controller(
            scope = this,
            gateway = gateway,
            cached = { cached },
            rendered = rendered,
        )

        controller.open()
        advanceUntilIdle()
        assertEquals(cached + StickerPanelItem.Error, rendered.last())

        controller.retry()
        assertEquals(cached + StickerPanelItem.Loading, rendered.last())
        advanceUntilIdle()

        assertEquals(listOf(header("fresh"), stickerItem("fresh-sticker", "fresh")), rendered.last())
    }

    @Test
    fun duplicateOpenAndRetryCallsDoNotStartAnotherRequest() = runTest {
        val response = CompletableDeferred<Result<List<StickerPackInfo>>>()
        val gateway = FakeStickerGateway(packsHandler = { response.await() })
        val rendered = mutableListOf<List<StickerPanelItem>>()
        val controller = controller(scope = this, gateway = gateway, rendered = rendered)

        controller.open()
        controller.open()
        controller.retry()
        runCurrent()

        assertEquals(1, gateway.packsCalls)
        response.complete(Result.success(emptyList()))
        advanceUntilIdle()
        assertEquals(1, gateway.packsCalls)
    }

    @Test
    fun thrownAndReturnedCancellationDoNotBecomeErrors() = runTest {
        val thrownGateway = FakeStickerGateway(
            packsHandler = { throw CancellationException("cancelled") }
        )
        val thrownRendered = mutableListOf<List<StickerPanelItem>>()
        val thrownController = controller(this, thrownGateway, rendered = thrownRendered)

        thrownController.open()
        advanceUntilIdle()

        assertEquals(listOf(StickerPanelItem.Loading), thrownRendered.last())
        assertFalse(thrownController.loaded)

        val returnedGateway = FakeStickerGateway(
            packsHandler = { Result.failure(CancellationException("cancelled")) }
        )
        val returnedRendered = mutableListOf<List<StickerPanelItem>>()
        val returnedController = controller(this, returnedGateway, rendered = returnedRendered)

        returnedController.open()
        advanceUntilIdle()

        assertEquals(listOf(StickerPanelItem.Loading), returnedRendered.last())
        assertFalse(returnedController.loaded)
    }

    @Test
    fun successOrOrdinaryFailureReturnedAfterCancellationIsIgnored() = runTest {
        val successGateway = FakeStickerGateway(
            packsHandler = {
                currentCoroutineContext().job.cancel()
                Result.success(listOf(pack("late")))
            }
        )
        val successRendered = mutableListOf<List<StickerPanelItem>>()
        val successSaved = mutableListOf<List<StickerPanelItem>>()
        val successController = controller(
            this,
            successGateway,
            saveCache = { successSaved += it },
            rendered = successRendered,
        )

        successController.open()
        advanceUntilIdle()

        assertEquals(listOf(StickerPanelItem.Loading), successRendered.last())
        assertTrue(successSaved.isEmpty())

        val failureGateway = FakeStickerGateway(
            packsHandler = {
                currentCoroutineContext().job.cancel()
                Result.failure(IllegalStateException("masked cancellation"))
            }
        )
        val failureRendered = mutableListOf<List<StickerPanelItem>>()
        val failureController = controller(this, failureGateway, rendered = failureRendered)

        failureController.open()
        advanceUntilIdle()

        assertEquals(listOf(StickerPanelItem.Loading), failureRendered.last())
        assertFalse(failureController.loaded)
    }

    @Test
    fun stickerPackSuccessReturnedAfterCancellationIsNotPublished() = runTest {
        val gateway = FakeStickerGateway(
            packsHandler = { Result.success(listOf(pack("late"))) },
            stickerPackHandler = {
                currentCoroutineContext().job.cancel()
                Result.success(listOf(sticker("late-sticker", it)))
            },
        )
        val rendered = mutableListOf<List<StickerPanelItem>>()
        val saved = mutableListOf<List<StickerPanelItem>>()
        val controller = controller(this, gateway, saveCache = { saved += it }, rendered = rendered)

        controller.open()
        advanceUntilIdle()

        assertEquals(listOf(StickerPanelItem.Loading), rendered.last())
        assertTrue(saved.isEmpty())
    }

    private fun controller(
        scope: kotlinx.coroutines.CoroutineScope,
        gateway: FakeStickerGateway,
        cached: () -> List<StickerPanelItem>? = { null },
        saveCache: (List<StickerPanelItem>) -> Unit = {},
        clearCache: () -> Unit = {},
        rendered: MutableList<List<StickerPanelItem>>,
    ) = StickerPanelController(
        scope = scope,
        gateway = gateway,
        loadCache = cached,
        saveCache = saveCache,
        clearCache = clearCache,
        render = { rendered += it.toList() },
    )

    private class FakeStickerGateway(
        private val packsHandler: suspend () -> Result<List<StickerPackInfo>> = {
            Result.success(emptyList())
        },
        private val stickerPackHandler: suspend (String) -> Result<List<StickerInfo>> = {
            Result.success(emptyList())
        },
    ) : StickerGateway {
        var packsCalls = 0
        val stickerPackIds = mutableListOf<String>()

        override suspend fun packs(offset: Int, size: Int): Result<List<StickerPackInfo>> {
            packsCalls++
            return packsHandler()
        }

        override suspend fun stickerPack(packId: String): Result<List<StickerInfo>> {
            stickerPackIds += packId
            return stickerPackHandler(packId)
        }
    }

    private fun pack(id: String) = StickerPackInfo.newBuilder()
        .setId(id)
        .setName(id)
        .setStickerCount(1)
        .setCoverStickerId("")
        .build()

    private fun sticker(id: String, packId: String) = StickerInfo.newBuilder()
        .setId(id)
        .setStickerPackId(packId)
        .setFileId("file-$id")
        .build()

    private fun header(id: String) = StickerPanelItem.PackHeader(
        packId = id,
        packName = id,
        stickerCount = 1,
        coverStickerId = "",
    )

    private fun stickerItem(id: String, packId: String) = StickerPanelItem.Sticker(sticker(id, packId), packId)
}
