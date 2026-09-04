package com.barkfluff.client.drafts

import android.content.Context
import android.net.Uri
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.cache.ComposerAttachment
import java.io.File
import java.security.MessageDigest
import java.util.concurrent.atomic.AtomicLong
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext

/**
 * Owns accepted composer previews. Metadata is encrypted in Room, while bytes are kept in the
 * app-private no-backup directory so a process restart cannot lose a selected attachment.
 */
class ComposerAttachmentStore(
    context: Context,
    private val cache: ChatCacheRepository,
) {
    private val appContext = context.applicationContext
    private val mutex = Mutex()
    private val sequence = AtomicLong()

    suspend fun stageUri(
        scope: CacheScope,
        chatId: String,
        uri: Uri,
        generation: Long,
        kind: String,
        fileName: String? = null,
        mimeType: String? = appContext.contentResolver.getType(uri),
    ): ComposerAttachment = withContext(Dispatchers.IO) {
        val input = appContext.contentResolver.openInputStream(uri)
            ?: throw IllegalStateException("Cannot open composer attachment")
        input.use { source -> stageStream(scope, chatId, source, generation, kind, fileName, mimeType) }
    }

    suspend fun stageFile(
        scope: CacheScope,
        chatId: String,
        source: File,
        generation: Long,
        kind: String,
        fileName: String? = source.name,
        mimeType: String? = null,
    ): ComposerAttachment = withContext(Dispatchers.IO) {
        require(source.isFile) { "Composer attachment is missing" }
        source.inputStream().use { input ->
            stageStream(scope, chatId, input, generation, kind, fileName, mimeType)
        }
    }

    suspend fun stageBytes(
        scope: CacheScope,
        chatId: String,
        bytes: ByteArray,
        generation: Long,
        kind: String,
        fileName: String? = null,
        mimeType: String? = null,
    ): ComposerAttachment = withContext(Dispatchers.IO) {
        bytes.inputStream().use { input ->
            stageStream(scope, chatId, input, generation, kind, fileName, mimeType)
        }
    }

    suspend fun restore(scope: CacheScope, chatId: String): List<ComposerAttachment> = mutex.withLock {
        val stored = cache.readComposerAttachments(scope, chatId)
        val valid = stored.filter { isOwnedPath(scope, chatId, File(it.path)) && File(it.path).isFile }
        if (valid.size != stored.size) {
            cache.saveComposerAttachments(scope, chatId, valid)
            val validPaths = valid.map { it.path }.toSet()
            stored.filter { it.path !in validPaths }.forEach { File(it.path).delete() }
        }
        valid
    }

    suspend fun remove(scope: CacheScope, chatId: String, attachmentIndex: Int) = mutex.withLock {
        val current = cache.readComposerAttachments(scope, chatId)
        current.firstOrNull { it.attachmentIndex == attachmentIndex }?.let { File(it.path).delete() }
        val next = current.filterNot { it.attachmentIndex == attachmentIndex }
            .mapIndexed { index, attachment -> attachment.copy(attachmentIndex = index) }
        cache.saveComposerAttachments(scope, chatId, next)
    }

    suspend fun clear(scope: CacheScope, chatId: String) = mutex.withLock {
        cache.deleteComposerAttachments(scope, chatId)
        directory(scope, chatId).deleteRecursively()
    }

    /**
     * Completes the composer-to-outbox handoff. A newer generation is deliberately retained when
     * the user picked another attachment while an older send was being enqueued.
     */
    suspend fun clearAfterEnqueue(scope: CacheScope, chatId: String, generation: Long) = mutex.withLock {
        val current = cache.readComposerAttachments(scope, chatId)
        val retained = current.filter { it.generation > generation }
        current.filter { it.generation <= generation }.forEach { File(it.path).delete() }
        if (retained.isEmpty()) {
            cache.deleteComposerAttachments(scope, chatId)
            directory(scope, chatId).deleteRecursively()
        } else {
            cache.saveComposerAttachments(scope, chatId, retained.mapIndexed { index, value ->
                value.copy(attachmentIndex = index)
            })
        }
    }

    suspend fun clearScope(scope: CacheScope) = mutex.withLock {
        cache.composerChatIds(scope).forEach { chatId -> cache.deleteComposerAttachments(scope, chatId) }
        cache.deleteAllComposerAttachments(scope)
        scopeDirectory(scope).deleteRecursively()
    }

    /** Removes records/files left by an interrupted copy or a deleted chat. */
    suspend fun cleanupOrphans(scope: CacheScope, knownChatIds: Set<String> = emptySet()) = mutex.withLock {
        val chatIds = cache.composerChatIds(scope).toSet()
        val liveDirectories = chatIds.mapTo(mutableSetOf(), ::safeSegment)
        liveDirectories += knownChatIds.map(::safeSegment)
        scopeDirectory(scope).listFiles()?.filter(File::isDirectory)?.forEach { chatDirectory ->
            if (chatDirectory.name !in liveDirectories) {
                chatDirectory.deleteRecursively()
            }
        }
        chatIds.forEach { chatId ->
            val records = cache.readComposerAttachments(scope, chatId)
            val valid = records.filter { isOwnedPath(scope, chatId, File(it.path)) && File(it.path).isFile }
            if (valid.size != records.size) cache.saveComposerAttachments(scope, chatId, valid)
        }
    }

    private suspend fun stageStream(
        scope: CacheScope,
        chatId: String,
        source: java.io.InputStream,
        generation: Long,
        kind: String,
        fileName: String?,
        mimeType: String?,
    ): ComposerAttachment = mutex.withLock {
        require(chatId.isNotBlank()) { "Chat id is required" }
        require(generation >= 0L) { "Composer generation cannot be negative" }
        val current = cache.readComposerAttachments(scope, chatId)
        val index = current.size
        val targetDirectory = directory(scope, chatId).apply { mkdirs() }
        val suffix = fileName?.substringAfterLast('.', "")?.takeIf { it.isNotBlank() }?.let { ".${safeSegment(it)}" }.orEmpty()
        val token = "${System.currentTimeMillis()}_${sequence.incrementAndGet()}"
        val target = File(targetDirectory, "attachment_${index}_$token$suffix")
        val temporary = File(targetDirectory, ".${target.name}.tmp")
        try {
            temporary.outputStream().use { output -> source.copyTo(output) }
            check(temporary.renameTo(target)) { "Cannot commit composer attachment" }
            val attachment = ComposerAttachment(
                attachmentIndex = index,
                path = target.absolutePath,
                kind = kind,
                fileName = fileName,
                mimeType = mimeType,
                generation = generation,
                createdAtMillis = System.currentTimeMillis(),
            )
            cache.saveComposerAttachments(scope, chatId, current + attachment)
            attachment
        } catch (error: Throwable) {
            temporary.delete()
            target.delete()
            throw error
        }
    }

    private fun scopeDirectory(scope: CacheScope): File =
        File(appContext.noBackupFilesDir, "composer/${safeSegment(scope.id)}")

    private fun directory(scope: CacheScope, chatId: String): File =
        File(scopeDirectory(scope), safeSegment(chatId))

    private fun isOwnedPath(scope: CacheScope, chatId: String, file: File): Boolean {
        val expected = directory(scope, chatId).canonicalFile
        val actual = runCatching { file.canonicalFile }.getOrNull() ?: return false
        return actual.parentFile == expected && actual.name.isNotBlank() && !actual.name.startsWith(".")
    }

    private fun safeSegment(value: String): String = value
        .trim()
        .replace(Regex("[^A-Za-z0-9._-]"), "_")
        .trim('.')
        .take(96)
        .ifBlank { sha256(value) }

    private fun sha256(value: String): String = MessageDigest.getInstance("SHA-256")
        .digest(value.toByteArray())
        .joinToString("") { "%02x".format(it) }
}
