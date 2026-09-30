package com.barkfluff.client.adapter

import barkfluff.shared.Shared
import com.barkfluff.client.domain.media.AutoDownloadNetwork
import com.barkfluff.client.domain.media.AutoDownloadPolicy
import com.barkfluff.client.domain.media.AutoDownloadSettings
import java.io.File
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.isActive
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit

sealed interface AttachmentDownloadState {
    data object Waiting : AttachmentDownloadState
    data class Downloading(val progress: Int) : AttachmentDownloadState
    data class Cached(val file: File) : AttachmentDownloadState
    data object Failed : AttachmentDownloadState
}

/** One controller per adapter: visible consumers share downloads and two transfer slots. */
class AttachmentAutoDownloadController(
    private val loader: AttachmentLoader,
    private val scope: CoroutineScope,
) {
    private var settings = AutoDownloadSettings()
    private var network = AutoDownloadNetwork.UNAVAILABLE
    private var active = false
    private val bindings = mutableSetOf<Binding>()
    private val downloads = mutableMapOf<String, Transfer>()
    private val slots = Semaphore(2)

    private class Transfer {
        var job: Job? = null
        var state: AttachmentDownloadState = AttachmentDownloadState.Downloading(0)
    }

    inner class Binding internal constructor(
        val attachment: Shared.MessageAttachment,
        private val onState: (AttachmentDownloadState) -> Unit,
    ) {
        internal var visible = false
        internal var manualDownloading = false
        internal var attempted: Pair<AutoDownloadSettings, AutoDownloadNetwork>? = null
        private var lastState: AttachmentDownloadState? = null

        fun setManualDownloading(value: Boolean) {
            manualDownloading = value
            refresh()
        }

        fun setVisible(value: Boolean) {
            if (visible == value) return
            visible = value
            if (!value) {
                attempted = null
                cancelUnobserved(attachment.fileId)
            }
            refresh()
        }

        fun close() {
            bindings.remove(this)
            visible = false
            cancelUnobserved(attachment.fileId)
            refresh()
        }

        internal fun emit(state: AttachmentDownloadState) {
            if (state == lastState) return
            lastState = state
            onState(state)
        }
    }

    fun bind(attachment: Shared.MessageAttachment, onState: (AttachmentDownloadState) -> Unit): Binding =
        Binding(attachment, onState).also {
            bindings.add(it)
            tryStart(it)
        }

    fun setActive(value: Boolean) {
        active = value
        if (!value) downloads.values.toList().forEach { it.job?.cancel() }
        refresh()
    }

    fun update(settings: AutoDownloadSettings, network: AutoDownloadNetwork) {
        this.settings = settings
        this.network = network
        refresh()
    }

    fun refresh() {
        bindings.toList().forEach(::tryStart)
    }

    private fun tryStart(binding: Binding) {
        val attachment = binding.attachment
        val fileId = attachment.fileId
        loader.cached(fileId)?.let {
            binding.emit(AttachmentDownloadState.Cached(it))
            return
        }
        downloads[fileId]?.let {
            binding.emit(it.state)
            return
        }
        if (!active || !scope.isActive || !binding.visible || fileId.isBlank() ||
            bindings.any { it.attachment.fileId == fileId && it.manualDownloading } ||
            !AutoDownloadPolicy.mayStart(settings, attachment.type, attachment.attachmentSize, network)) {
            binding.emit(AttachmentDownloadState.Waiting)
            return
        }
        if (binding.attempted == (settings to network)) return

        val transfer = Transfer()
        downloads[fileId] = transfer
        notify(fileId, transfer.state)
        transfer.job = scope.launch(start = CoroutineStart.LAZY) {
            var cancelled = false
            try {
                slots.withPermit {
                    // A queued transfer is still a new start: use the latest policy now.
                    if (!active || bindings.none { it.visible && it.attachment.fileId == fileId } ||
                        bindings.any { it.attachment.fileId == fileId && it.manualDownloading } ||
                        !AutoDownloadPolicy.mayStart(settings, attachment.type, attachment.attachmentSize, network)) return@withPermit
                    val snapshot = settings
                    val attempt = snapshot to network
                    bindings.filter { it.attachment.fileId == fileId }.forEach { it.attempted = attempt }
                    val file = loader.downloadAuto(fileId, snapshot.maxBytes) { progress ->
                        scope.launch {
                            if (downloads[fileId] === transfer) {
                                transfer.state = AttachmentDownloadState.Downloading(progress)
                                notify(fileId, transfer.state)
                            }
                        }
                    }
                    transfer.state = file?.let(AttachmentDownloadState::Cached) ?: AttachmentDownloadState.Failed
                    notify(fileId, transfer.state)
                }
            } catch (e: CancellationException) {
                cancelled = true
                throw e
            } catch (_: Exception) {
                transfer.state = AttachmentDownloadState.Failed
                notify(fileId, transfer.state)
            } finally {
                release(fileId, transfer, cancelled)
            }
        }
        // A recycled row can cancel a scheduled coroutine before its body/finally runs.
        transfer.job!!.invokeOnCompletion { cause -> release(fileId, transfer, cause is CancellationException) }
        transfer.job!!.start()
    }

    private fun release(fileId: String, transfer: Transfer, cancelled: Boolean) {
        if (downloads[fileId] !== transfer) return
        if (cancelled) bindings.filter { it.attachment.fileId == fileId }.forEach { it.attempted = null }
        downloads.remove(fileId)
        refresh()
    }

    private fun notify(fileId: String, state: AttachmentDownloadState) {
        bindings.filter { it.attachment.fileId == fileId }.forEach { it.emit(state) }
    }

    private fun cancelUnobserved(fileId: String) {
        if (bindings.none { it.visible && it.attachment.fileId == fileId }) downloads[fileId]?.job?.cancel()
    }
}
