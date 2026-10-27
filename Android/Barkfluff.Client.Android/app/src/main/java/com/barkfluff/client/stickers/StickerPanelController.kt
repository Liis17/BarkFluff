package com.barkfluff.client.stickers

import com.barkfluff.client.adapter.StickerPanelItem
import com.barkfluff.client.domain.gateway.StickerGateway
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch
import kotlin.coroutines.CoroutineContext

class StickerPanelController(
    private val scope: CoroutineScope,
    private val gateway: StickerGateway,
    private val loadCache: () -> List<StickerPanelItem>?,
    private val saveCache: (List<StickerPanelItem>) -> Unit,
    private val clearCache: () -> Unit,
    private val render: (List<StickerPanelItem>) -> Unit,
) {
    private enum class Status { Loading, Error, Empty }

    private var snapshot: List<StickerPanelItem> = emptyList()
    private var status: Status? = null
    private var requestActive = false

    var loaded: Boolean = false
        private set

    fun open() {
        if (requestActive || loaded) return

        val context = scope.coroutineContext
        context.ensureActive()
        val cached = loadCache()?.filter(::isContentItem).orEmpty()
        context.ensureActive()

        if (cached.isNotEmpty()) {
            snapshot = cached
            loaded = true
            status = null
        } else {
            status = Status.Loading
        }
        renderCurrent(context)
        startRequest()
    }

    fun retry() {
        if (requestActive || status != Status.Error) return

        val context = scope.coroutineContext
        context.ensureActive()
        status = Status.Loading
        renderCurrent(context)
        startRequest()
    }

    private fun startRequest() {
        requestActive = true
        scope.launch {
            try {
                refresh()
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (_: Exception) {
                val context = currentCoroutineContext()
                context.ensureActive()
                status = Status.Error
                renderCurrent(context)
            } finally {
                requestActive = false
            }
        }
    }

    private suspend fun refresh() {
        val context = currentCoroutineContext()
        val packsResult = gateway.packs()
        context.ensureActive()
        val packs = packsResult.getOrThrow()

        if (packs.isEmpty()) {
            context.ensureActive()
            clearCache()
            context.ensureActive()
            snapshot = emptyList()
            loaded = true
            status = Status.Empty
            renderCurrent(context)
            return
        }

        val updated = mutableListOf<StickerPanelItem>()
        for (pack in packs) {
            updated.add(
                StickerPanelItem.PackHeader(
                    packId = pack.id,
                    packName = pack.name,
                    stickerCount = pack.stickerCount,
                    coverStickerId = pack.coverStickerId,
                )
            )

            val stickersResult = gateway.stickerPack(pack.id)
            context.ensureActive()
            val stickers = stickersResult.getOrThrow()
            updated.addAll(stickers.map { StickerPanelItem.Sticker(it, pack.id) })
        }

        val newSnapshot = updated.toList()
        context.ensureActive()
        saveCache(newSnapshot)
        context.ensureActive()
        snapshot = newSnapshot
        loaded = true
        status = null
        renderCurrent(context)
    }

    private fun renderCurrent(context: CoroutineContext) {
        context.ensureActive()
        val items = when (status) {
            Status.Loading -> snapshot + StickerPanelItem.Loading
            Status.Error -> if (snapshot.isEmpty()) {
                listOf(StickerPanelItem.Error)
            } else {
                snapshot + StickerPanelItem.Error
            }
            Status.Empty -> listOf(StickerPanelItem.Empty)
            null -> snapshot
        }
        render(items)
    }

    private fun isContentItem(item: StickerPanelItem): Boolean =
        item is StickerPanelItem.PackHeader || item is StickerPanelItem.Sticker
}
