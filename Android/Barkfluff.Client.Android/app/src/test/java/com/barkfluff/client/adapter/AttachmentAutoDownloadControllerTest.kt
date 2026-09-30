package com.barkfluff.client.adapter

import barkfluff.shared.Shared
import com.barkfluff.client.domain.media.AutoDownloadNetwork
import com.barkfluff.client.domain.media.AutoDownloadMode
import com.barkfluff.client.domain.media.AutoDownloadSettings
import java.io.File
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class AttachmentAutoDownloadControllerTest {
    private fun attachment(id: String = "photo") = Shared.MessageAttachment.newBuilder().setFileId(id)
        .setType(Shared.MessageAttachmentType.IMAGE).setAttachmentSize(100L).build()

    private class Downloads {
        data class Request(val id: String, val limit: Long, val result: CompletableDeferred<File?> = CompletableDeferred())
        val requests = mutableListOf<Request>()
        val cache = mutableMapOf<String, File>()
        val cancelled = mutableListOf<String>()
        var running = 0
        var maximumRunning = 0
        val loader = CallbackAttachmentLoader(
            urlProvider = { null }, downloadProvider = { _, _ -> null }, cachedProvider = cache::get,
            autoDownloadProvider = { id, limit, _ ->
                val request = Request(id, limit)
                requests.add(request)
                running++
                maximumRunning = maxOf(maximumRunning, running)
                try {
                    request.result.await().also { if (it != null) cache[id] = it }
                } catch (e: CancellationException) {
                    cancelled.add(id)
                    throw e
                } finally { running-- }
            },
        )
        fun complete(id: String, success: Boolean = true) {
            requests.last { it.id == id }.result.complete(if (success) File(id) else null)
        }
    }

    @Test
    fun `only a visible attachment can start and duplicate bindings share the result`() = runTest {
        val result = CompletableDeferred<File?>()
        var requests = 0
        var cached: File? = null
        val loader = CallbackAttachmentLoader(
            urlProvider = { null }, downloadProvider = { _, _ -> null },
            cachedProvider = { cached },
            autoDownloadProvider = { _, _, _ -> requests++; result.await().also { cached = it } },
        )
        val controller = AttachmentAutoDownloadController(loader, this)
        controller.update(AutoDownloadSettings(), AutoDownloadNetwork.OTHER)
        controller.setActive(true)
        val attachment = Shared.MessageAttachment.newBuilder().setFileId("photo")
            .setType(Shared.MessageAttachmentType.IMAGE).setAttachmentSize(100L).build()
        val states = mutableListOf<AttachmentDownloadState>()
        val first = controller.bind(attachment, states::add)
        runCurrent()
        assertEquals(0, requests)
        first.setVisible(true)
        val second = controller.bind(attachment, states::add)
        second.setVisible(true)
        runCurrent()
        assertEquals(1, requests)
        result.complete(File("photo"))
        runCurrent()
        assertTrue(states.last() is AttachmentDownloadState.Cached)
        first.close()
        second.close()
    }

    @Test
    fun `settings and network changes preserve the running request and its original limit`() = runTest {
        val downloads = Downloads()
        val controller = AttachmentAutoDownloadController(downloads.loader, this)
        controller.update(AutoDownloadSettings(), AutoDownloadNetwork.OTHER)
        controller.setActive(true)
        val states = mutableListOf<AttachmentDownloadState>()
        controller.bind(attachment(), states::add).setVisible(true)
        runCurrent()
        controller.update(AutoDownloadSettings(mapOf(Shared.MessageAttachmentType.IMAGE to AutoDownloadMode.MANUAL), 512), AutoDownloadNetwork.UNAVAILABLE)
        controller.bind(attachment("second")) {}.setVisible(true)
        runCurrent()
        assertEquals(1, downloads.requests.size)
        assertEquals(2L * 1024 * 1024, downloads.requests.single().limit)
        assertTrue(downloads.cancelled.isEmpty())
        downloads.complete("photo")
        runCurrent()
        assertTrue(states.last() is AttachmentDownloadState.Cached)
        controller.setActive(false)
    }

    @Test
    fun `two transfer slots recheck queued items after policy changes`() = runTest {
        val downloads = Downloads()
        val controller = AttachmentAutoDownloadController(downloads.loader, this)
        controller.update(AutoDownloadSettings(), AutoDownloadNetwork.OTHER)
        controller.setActive(true)
        for (id in listOf("one", "two", "three")) controller.bind(attachment(id)) {}.setVisible(true)
        runCurrent()
        assertEquals(2, downloads.requests.size)
        controller.update(AutoDownloadSettings(mapOf(Shared.MessageAttachmentType.IMAGE to AutoDownloadMode.MANUAL)), AutoDownloadNetwork.OTHER)
        downloads.complete("one")
        runCurrent()
        assertEquals(2, downloads.requests.size)
        controller.update(AutoDownloadSettings(maxSizeMb = 1), AutoDownloadNetwork.OTHER)
        runCurrent()
        assertEquals(3, downloads.requests.size)
        assertEquals(1024L * 1024, downloads.requests.last().limit)
        assertEquals(2, downloads.maximumRunning)
        controller.setActive(false)
        runCurrent()
    }

    @Test
    fun `cache is available while offline and manual`() = runTest {
        val downloads = Downloads().apply { cache["photo"] = File("photo") }
        val controller = AttachmentAutoDownloadController(downloads.loader, this)
        controller.update(AutoDownloadSettings(mapOf(Shared.MessageAttachmentType.IMAGE to AutoDownloadMode.MANUAL)), AutoDownloadNetwork.UNAVAILABLE)
        controller.setActive(true)
        val states = mutableListOf<AttachmentDownloadState>()
        controller.bind(attachment(), states::add).setVisible(true)
        runCurrent()
        assertEquals(AttachmentDownloadState.Cached(File("photo")), states.last())
        assertTrue(downloads.requests.isEmpty())
    }

    @Test
    fun `failure does not loop and a recycled binding can retry`() = runTest {
        val downloads = Downloads()
        val controller = AttachmentAutoDownloadController(downloads.loader, this)
        controller.update(AutoDownloadSettings(), AutoDownloadNetwork.OTHER)
        controller.setActive(true)
        val states = mutableListOf<AttachmentDownloadState>()
        val binding = controller.bind(attachment(), states::add).apply { setVisible(true) }
        runCurrent()
        downloads.complete("photo", false)
        runCurrent()
        controller.refresh()
        runCurrent()
        assertEquals(1, downloads.requests.size)
        assertEquals(AttachmentDownloadState.Failed, states.last())
        binding.close()
        controller.bind(attachment(), states::add).setVisible(true)
        runCurrent()
        assertEquals(2, downloads.requests.size)
        controller.setActive(false)
        runCurrent()
    }

    @Test
    fun `lifecycle cancellation releases the claim so resume can start again`() = runTest {
        val downloads = Downloads()
        val controller = AttachmentAutoDownloadController(downloads.loader, this)
        controller.update(AutoDownloadSettings(), AutoDownloadNetwork.OTHER)
        controller.setActive(true)
        controller.bind(attachment()) {}.setVisible(true)
        runCurrent()
        controller.setActive(false)
        runCurrent()
        assertEquals(listOf("photo"), downloads.cancelled)
        controller.setActive(true)
        runCurrent()
        assertEquals(2, downloads.requests.size)
        controller.setActive(false)
        runCurrent()
    }

    @Test
    fun `recycling before coroutine starts also releases the claim`() = runTest {
        val downloads = Downloads()
        val controller = AttachmentAutoDownloadController(downloads.loader, this)
        controller.update(AutoDownloadSettings(), AutoDownloadNetwork.OTHER)
        controller.setActive(true)
        controller.bind(attachment()) {}.apply { setVisible(true); close() }
        runCurrent()
        assertTrue(downloads.requests.isEmpty())
        controller.bind(attachment()) {}.setVisible(true)
        runCurrent()
        assertEquals(1, downloads.requests.size)
        controller.setActive(false)
        runCurrent()
    }

    @Test
    fun `manual download prevents an automatic duplicate even after policy updates`() = runTest {
        val downloads = Downloads()
        val controller = AttachmentAutoDownloadController(downloads.loader, this)
        controller.update(AutoDownloadSettings(mapOf(Shared.MessageAttachmentType.IMAGE to AutoDownloadMode.MANUAL)), AutoDownloadNetwork.OTHER)
        controller.setActive(true)
        val states = mutableListOf<AttachmentDownloadState>()
        val binding = controller.bind(attachment(), states::add).apply { setVisible(true); setManualDownloading(true) }
        controller.update(AutoDownloadSettings(), AutoDownloadNetwork.OTHER)
        controller.bind(attachment()) {}.setVisible(true)
        runCurrent()
        assertTrue(downloads.requests.isEmpty())
        downloads.cache["photo"] = File("photo")
        binding.setManualDownloading(false)
        runCurrent()
        assertEquals(AttachmentDownloadState.Cached(File("photo")), states.last())
        assertTrue(downloads.requests.isEmpty())
    }
}
