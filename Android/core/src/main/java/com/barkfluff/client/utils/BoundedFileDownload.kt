package com.barkfluff.client.utils

import java.io.File
import java.io.IOException
import java.io.InputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive

/** Publishes a complete bounded response atomically; partial files never become cache hits. */
object BoundedFileDownload {
    suspend fun save(
        input: InputStream,
        destination: File,
        maxBytes: Long,
        contentLength: Long = -1L,
        onProgress: (Int) -> Unit = {},
    ): File {
        require(maxBytes > 0)
        if (contentLength > maxBytes) throw IOException("Media exceeds the auto-download limit")
        val temporary = File.createTempFile("auto-", ".part", destination.parentFile)
        try {
            val buffer = ByteArray(8192)
            var total = 0L
            temporary.outputStream().use { output ->
                while (true) {
                    currentCoroutineContext().ensureActive()
                    val count = input.read(buffer)
                    if (count < 0) break
                    if (count.toLong() > maxBytes - total) throw IOException("Media exceeds the auto-download limit")
                    output.write(buffer, 0, count)
                    total += count
                    if (contentLength > 0) onProgress((total * 100L / contentLength).coerceAtMost(99).toInt())
                }
            }
            currentCoroutineContext().ensureActive()
            if (contentLength > 0 && total < contentLength) throw IOException("Incomplete media response")
            Files.move(temporary.toPath(), destination.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
            onProgress(100)
            return destination
        } finally {
            temporary.delete()
        }
    }
}
