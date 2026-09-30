package com.barkfluff.client.domain.media

import barkfluff.shared.Shared.MessageAttachmentType
import org.junit.Assert.assertTrue
import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Test

class AutoDownloadPolicyTest {
    @Test
    fun `default settings allow a static photo on mobile data`() {
        assertTrue(AutoDownloadPolicy.mayStart(
            AutoDownloadSettings(),
            MessageAttachmentType.IMAGE,
            2_097_152L,
            AutoDownloadNetwork.OTHER,
        ))
    }
    @Test
    fun `all six categories download on wifi but only static photos on mobile by default`() {
        AutoDownloadSettings.TYPES.forEach { type ->
            assertTrue(type.name, AutoDownloadPolicy.mayStart(AutoDownloadSettings(), type, 100L, AutoDownloadNetwork.WIFI))
            assertEquals(type.name, type == MessageAttachmentType.IMAGE,
                AutoDownloadPolicy.mayStart(AutoDownloadSettings(), type, 100L, AutoDownloadNetwork.OTHER))
            assertFalse(AutoDownloadPolicy.mayStart(AutoDownloadSettings(), type, 100L, AutoDownloadNetwork.UNAVAILABLE))
        }
    }

    @Test
    fun `explicit modes apply to every category`() {
        AutoDownloadSettings.TYPES.forEach { type ->
            val manual = AutoDownloadSettings(modes = mapOf(type to AutoDownloadMode.MANUAL))
            assertFalse(AutoDownloadPolicy.mayStart(manual, type, 100L, AutoDownloadNetwork.WIFI))
            val any = AutoDownloadSettings(modes = mapOf(type to AutoDownloadMode.ANY_NETWORK))
            assertTrue(AutoDownloadPolicy.mayStart(any, type, 100L, AutoDownloadNetwork.OTHER))
            assertFalse(AutoDownloadPolicy.mayStart(any, type, 100L, AutoDownloadNetwork.UNAVAILABLE))
            val wifi = AutoDownloadSettings(modes = mapOf(type to AutoDownloadMode.WIFI_ONLY))
            assertFalse(AutoDownloadPolicy.mayStart(wifi, type, 100L, AutoDownloadNetwork.OTHER))
        }
    }

    @Test
    fun `unknown and oversized attachments cannot start and the exact limit is allowed`() {
        val settings = AutoDownloadSettings(maxSizeMb = 1)
        listOf(-1L, 0L, 1_048_577L, Long.MAX_VALUE).forEach { size ->
            assertFalse(AutoDownloadPolicy.mayStart(settings, MessageAttachmentType.IMAGE, size, AutoDownloadNetwork.WIFI))
        }
        assertTrue(AutoDownloadPolicy.mayStart(settings, MessageAttachmentType.IMAGE, 1_048_576L, AutoDownloadNetwork.WIFI))
        assertEquals(536_870_912L, AutoDownloadSettings(maxSizeMb = 512).maxBytes)
    }

    @Test
    fun `unconfigured attachment types are excluded`() {
        assertFalse(AutoDownloadPolicy.mayStart(AutoDownloadSettings(), MessageAttachmentType.STICKER, 100L, AutoDownloadNetwork.WIFI))
        assertFalse(AutoDownloadPolicy.mayStart(AutoDownloadSettings(), MessageAttachmentType.MESSAGE_ATTACHMENT_TYPE_UNKNOWN, 100L, AutoDownloadNetwork.WIFI))
    }
}
