package com.barkfluff.client.utils

import java.io.ByteArrayInputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder

class BoundedFileDownloadTest {
    @get:Rule val directory = TemporaryFolder()

    @Test
    fun `a response larger than its declared size cannot exceed the budget or publish partial cache`() {
        val destination = File(directory.root, "media")
        assertThrows(IOException::class.java) {
            runBlocking {
                BoundedFileDownload.save(ByteArrayInputStream(ByteArray(11)), destination, 10, 1)
            }
        }
        assertFalse(destination.exists())
        assertArrayEquals(emptyArray<String>(), directory.root.list())
    }

    @Test
    fun `unknown length still enforces the actual byte limit`() {
        val destination = File(directory.root, "media")
        assertThrows(IOException::class.java) {
            runBlocking { BoundedFileDownload.save(ByteArrayInputStream(ByteArray(11)), destination, 10) }
        }
        assertFalse(destination.exists())
        assertArrayEquals(emptyArray<String>(), directory.root.list())
    }

    @Test
    fun `exact limit succeeds with known and unknown lengths`() = runBlocking {
        for (length in listOf(10L, -1L)) {
            val bytes = ByteArray(10) { it.toByte() }
            val destination = File(directory.root, "media")
            val progress = mutableListOf<Int>()
            assertEquals(destination, BoundedFileDownload.save(ByteArrayInputStream(bytes), destination, 10, length, progress::add))
            assertArrayEquals(bytes, destination.readBytes())
            assertEquals(100, progress.last())
            assertArrayEquals(arrayOf("media"), directory.root.list())
        }
    }

    @Test
    fun `declared oversized response is rejected before reading`() {
        var read = false
        val stream = object : InputStream() {
            override fun read(): Int { read = true; return -1 }
        }
        assertThrows(IOException::class.java) {
            runBlocking { BoundedFileDownload.save(stream, File(directory.root, "media"), 10, 11) }
        }
        assertFalse(read)
        assertArrayEquals(emptyArray<String>(), directory.root.list())
    }

    @Test
    fun `truncated or failed response leaves no partial file`() {
        val failing = object : InputStream() {
            var first = true
            override fun read(): Int = throw IOException("broken connection")
            override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
                if (!first) throw IOException("broken connection")
                first = false
                buffer[offset] = 42
                return 1
            }
        }
        for (stream in listOf(ByteArrayInputStream(ByteArray(2)), failing)) {
            assertThrows(IOException::class.java) {
                runBlocking { BoundedFileDownload.save(stream, File(directory.root, "media"), 10, 5) }
            }
            assertArrayEquals(emptyArray<String>(), directory.root.list())
        }
    }

    @Test
    fun `cancellation removes temporary data and preserves the previous cache`() = runBlocking {
        val destination = File(directory.root, "media").apply { writeText("previous") }
        val task = launch {
            val job = coroutineContext[Job]!!
            BoundedFileDownload.save(ByteArrayInputStream(ByteArray(9000)), destination, 10000, 9000) { job.cancel() }
        }
        task.join()
        assertTrue(task.isCancelled)
        assertEquals("previous", destination.readText())
        assertArrayEquals(arrayOf("media"), directory.root.list())
    }
}
