package com.barkfluff.client.cache

import android.content.Context
import android.util.Base64
import androidx.room.Dao
import androidx.room.ColumnInfo
import androidx.room.Database
import androidx.room.Entity
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase
import androidx.room.withTransaction
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import barkfluff.shared.Shared
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import net.zetetic.database.sqlcipher.SupportOpenHelperFactory
import java.io.File
import java.security.SecureRandom

data class CacheScope(val id: String) {
    companion object {
        fun from(globalParam: GlobalParam): CacheScope? {
            val server = globalParam.socketBeacon.ifBlank { globalParam.socketMessages }
            if (server.isBlank() || globalParam.userId <= 0L) return null
            return CacheScope("$server|${globalParam.userId}")
        }
    }
}

data class CachedChatDisplay(
    val title: String,
    val avatarFileId: String?,
    val otherUserId: Long
)

data class CachedChatList(
    val chats: List<GrpcManager.ChatData>,
    val folders: List<GrpcManager.ChatFolder>,
    val displays: Map<String, CachedChatDisplay>,
    val totalCount: Int
)

data class ChatCacheStats(
    val chatCount: Int,
    val messageCount: Int,
    val sizeBytes: Long
)

data class CachedChatDraft(
    val chatId: String,
    val text: String,
    val replyToMessageId: Long,
    val revision: String,
    val generation: Long,
    val syncState: Int
)

@Entity(tableName = "chat_cache_meta")
data class CachedChatMetaEntity(
    @PrimaryKey val scopeId: String,
    val totalCount: Int,
    val updatedAtMillis: Long
)

@Entity(tableName = "cached_chats", primaryKeys = ["scopeId", "chatId"])
data class CachedChatEntity(
    val scopeId: String,
    val chatId: String,
    val title: String,
    val picture: String,
    val pictureFileId: String,
    val picturePreviewFileId: String,
    val isGroupChat: Boolean,
    val lastMessageId: Long?,
    val lastMessageSenderId: Long?,
    val lastMessageText: String?,
    val lastMessageSentAt: Long?,
    val lastMessageReadBy: String,
    val memberIds: String,
    val countUnread: Long,
    val firstUnreadMessageId: Long,
    val chatTypeNumber: Int,
    val lastActivityAt: Long,
    val privateInviteStateNumber: Int,
    val privateInviterUserId: Long,
    @ColumnInfo(defaultValue = "0") val hasDraft: Boolean
)

@Entity(tableName = "cached_chat_drafts", primaryKeys = ["scopeId", "chatId"])
data class CachedChatDraftEntity(
    val scopeId: String,
    val chatId: String,
    val text: String,
    val replyToMessageId: Long,
    val revision: String,
    val generation: Long,
    val syncState: Int
)

@Entity(tableName = "cached_chat_folders", primaryKeys = ["scopeId", "folderId"])
data class CachedChatFolderEntity(
    val scopeId: String,
    val folderId: String,
    val folderName: String,
    val folderIcon: String,
    val chatIds: String,
    val sortOrder: Int
)

@Entity(tableName = "cached_chat_displays", primaryKeys = ["scopeId", "chatId"])
data class CachedChatDisplayEntity(
    val scopeId: String,
    val chatId: String,
    val title: String,
    val avatarFileId: String?,
    val otherUserId: Long
)

@Entity(
    tableName = "cached_messages",
    primaryKeys = ["scopeId", "chatId", "messageId"]
)
data class CachedMessageEntity(
    val scopeId: String,
    val chatId: String,
    val messageId: Long,
    val sentAtMillis: Long,
    val payload: ByteArray
)

@Entity(
    tableName = "cached_private_messages",
    primaryKeys = ["scopeId", "chatId", "messageId"]
)
data class CachedPrivateMessageEntity(
    val scopeId: String,
    val chatId: String,
    val messageId: Long,
    val sentAtMillis: Long,
    val payload: ByteArray
)

@Entity(
    tableName = "cached_secret_messages",
    primaryKeys = ["scopeId", "chatId", "messageId"]
)
data class CachedSecretMessageEntity(
    val scopeId: String,
    val chatId: String,
    val messageId: String,
    val senderLabel: String,
    val plaintext: String,
    val sentAtMillis: Long
)

data class CachedSecretMessage(
    val messageId: String,
    val senderLabel: String,
    val plaintext: String,
    val sentAtMillis: Long
)
@Dao
interface ChatCacheDao {
    @Query("SELECT * FROM chat_cache_meta WHERE scopeId = :scopeId")
    suspend fun meta(scopeId: String): CachedChatMetaEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertMeta(meta: CachedChatMetaEntity)

    @Query("SELECT * FROM cached_chats WHERE scopeId = :scopeId ORDER BY lastActivityAt DESC")
    suspend fun chats(scopeId: String): List<CachedChatEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertChats(chats: List<CachedChatEntity>)

    @Query("SELECT * FROM cached_chat_folders WHERE scopeId = :scopeId ORDER BY sortOrder")
    suspend fun folders(scopeId: String): List<CachedChatFolderEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertFolders(folders: List<CachedChatFolderEntity>)

    @Query("DELETE FROM cached_chat_folders WHERE scopeId = :scopeId")
    suspend fun deleteFolders(scopeId: String)

    @Query("SELECT * FROM cached_chat_displays WHERE scopeId = :scopeId")
    suspend fun displays(scopeId: String): List<CachedChatDisplayEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertDisplay(display: CachedChatDisplayEntity)

    @Query("SELECT * FROM cached_chat_drafts WHERE scopeId = :scopeId")
    suspend fun drafts(scopeId: String): List<CachedChatDraftEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertDraft(draft: CachedChatDraftEntity)

    @Query("DELETE FROM cached_chat_drafts WHERE scopeId = :scopeId AND chatId = :chatId")
    suspend fun deleteDraft(scopeId: String, chatId: String)

    @Query("SELECT * FROM cached_messages WHERE scopeId = :scopeId AND chatId = :chatId ORDER BY sentAtMillis DESC LIMIT :limit")
    suspend fun latestMessages(scopeId: String, chatId: String, limit: Int): List<CachedMessageEntity>

    @Query("SELECT * FROM cached_messages WHERE scopeId = :scopeId AND chatId = :chatId AND messageId < :beforeMessageId ORDER BY messageId DESC LIMIT :limit")
    suspend fun messagesBefore(scopeId: String, chatId: String, beforeMessageId: Long, limit: Int): List<CachedMessageEntity>

    @Query("SELECT * FROM cached_messages WHERE scopeId = :scopeId AND chatId = :chatId AND messageId > :afterMessageId ORDER BY messageId ASC LIMIT :limit")
    suspend fun messagesAfter(scopeId: String, chatId: String, afterMessageId: Long, limit: Int): List<CachedMessageEntity>

    @Query("SELECT * FROM cached_messages WHERE scopeId = :scopeId AND chatId = :chatId AND messageId = :messageId")
    suspend fun message(scopeId: String, chatId: String, messageId: Long): CachedMessageEntity?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertMessages(messages: List<CachedMessageEntity>)

    @Query("DELETE FROM cached_messages WHERE scopeId = :scopeId AND chatId = :chatId AND messageId = :messageId")
    suspend fun deleteMessage(scopeId: String, chatId: String, messageId: Long)
    @Query("SELECT * FROM cached_private_messages WHERE scopeId = :scopeId AND chatId = :chatId ORDER BY sentAtMillis DESC LIMIT :limit")
    suspend fun latestPrivateMessages(scopeId: String, chatId: String, limit: Int): List<CachedPrivateMessageEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertPrivateMessages(messages: List<CachedPrivateMessageEntity>)

    @Query("DELETE FROM cached_private_messages WHERE scopeId = :scopeId AND chatId = :chatId AND messageId = :messageId")
    suspend fun deletePrivateMessage(scopeId: String, chatId: String, messageId: Long)

    @Query("SELECT * FROM cached_secret_messages WHERE scopeId = :scopeId AND chatId = :chatId ORDER BY sentAtMillis DESC LIMIT :limit")
    suspend fun latestSecretMessages(scopeId: String, chatId: String, limit: Int): List<CachedSecretMessageEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsertSecretMessage(message: CachedSecretMessageEntity)

    @Query("SELECT COUNT(*) FROM cached_private_messages")
    suspend fun privateMessageCount(): Int

    @Query("SELECT COUNT(*) FROM cached_secret_messages")
    suspend fun secretMessageCount(): Int

    @Query("SELECT COUNT(*) FROM cached_chats")
    suspend fun chatCount(): Int

    @Query("SELECT COUNT(*) FROM cached_messages")
    suspend fun messageCount(): Int
}

@Database(
    entities = [
        CachedChatMetaEntity::class,
        CachedChatEntity::class,
        CachedChatFolderEntity::class,
        CachedChatDisplayEntity::class,
        CachedChatDraftEntity::class,
        CachedMessageEntity::class,
        CachedPrivateMessageEntity::class,
        CachedSecretMessageEntity::class
    ],
    version = 2,
    exportSchema = true
)
abstract class ChatCacheDatabase : RoomDatabase() {
    abstract fun cacheDao(): ChatCacheDao
}

class ChatCacheRepository(context: Context) {

    private val appContext = context.applicationContext
    private val databaseMutex = Mutex()

    @Volatile
    private var database: ChatCacheDatabase? = null

    private val securePreferences by lazy {
        val masterKeyAlias = MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC)
        EncryptedSharedPreferences.create(
            KEY_PREFERENCES,
            masterKeyAlias,
            appContext,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    private val _clearedEvents = MutableSharedFlow<Unit>(extraBufferCapacity = 1)
    val clearedEvents = _clearedEvents.asSharedFlow()

    suspend fun readChatList(scope: CacheScope): CachedChatList? = withContext(Dispatchers.IO) {
        val dao = database().cacheDao()
        val meta = dao.meta(scope.id) ?: return@withContext null
        val displays = dao.displays(scope.id).associate { display ->
            display.chatId to CachedChatDisplay(display.title, display.avatarFileId, display.otherUserId)
        }
        CachedChatList(
            chats = dao.chats(scope.id).map { it.toChatData() },
            folders = dao.folders(scope.id).map { it.toChatFolder() },
            displays = displays,
            totalCount = meta.totalCount
        )
    }

    suspend fun saveChatPage(
        scope: CacheScope,
        chats: List<GrpcManager.ChatData>,
        totalCount: Int,
        folders: List<GrpcManager.ChatFolder>? = null
    ) = withContext(Dispatchers.IO) {
        val db = database()
        db.withTransaction {
            val dao = db.cacheDao()
            dao.upsertMeta(CachedChatMetaEntity(scope.id, totalCount, System.currentTimeMillis()))
            if (chats.isNotEmpty()) dao.upsertChats(chats.map { it.toEntity(scope.id) })
            if (folders != null) {
                dao.deleteFolders(scope.id)
                if (folders.isNotEmpty()) dao.upsertFolders(folders.map { it.toEntity(scope.id) })
            }
        }
    }

    suspend fun saveDisplay(scope: CacheScope, chatId: String, display: CachedChatDisplay) =
        withContext(Dispatchers.IO) {
            database().cacheDao().upsertDisplay(
                CachedChatDisplayEntity(scope.id, chatId, display.title, display.avatarFileId, display.otherUserId)
            )
        }

    suspend fun readChatDrafts(scope: CacheScope): List<CachedChatDraft> = withContext(Dispatchers.IO) {
        database().cacheDao().drafts(scope.id).map {
            CachedChatDraft(it.chatId, it.text, it.replyToMessageId, it.revision, it.generation, it.syncState)
        }
    }

    suspend fun saveChatDraft(scope: CacheScope, draft: CachedChatDraft) = withContext(Dispatchers.IO) {
        database().cacheDao().upsertDraft(
            CachedChatDraftEntity(scope.id, draft.chatId, draft.text, draft.replyToMessageId, draft.revision, draft.generation, draft.syncState)
        )
    }

    suspend fun deleteChatDraft(scope: CacheScope, chatId: String) = withContext(Dispatchers.IO) {
        database().cacheDao().deleteDraft(scope.id, chatId)
    }

    suspend fun latestMessages(scope: CacheScope, chatId: String, limit: Int): List<Shared.Message> =
        withContext(Dispatchers.IO) {
            database().cacheDao().latestMessages(scope.id, chatId, limit).toMessages()
        }

    suspend fun messagesBefore(
        scope: CacheScope,
        chatId: String,
        beforeMessageId: Long,
        limit: Int
    ): List<Shared.Message> = withContext(Dispatchers.IO) {
        database().cacheDao().messagesBefore(scope.id, chatId, beforeMessageId, limit).toMessages()
    }

    suspend fun messagesAfter(
        scope: CacheScope,
        chatId: String,
        afterMessageId: Long,
        limit: Int
    ): List<Shared.Message> = withContext(Dispatchers.IO) {
        database().cacheDao().messagesAfter(scope.id, chatId, afterMessageId, limit).toMessages()
    }

    suspend fun saveMessages(scope: CacheScope, chatId: String, messages: List<Shared.Message>) {
        if (messages.isEmpty()) return
        withContext(Dispatchers.IO) {
            database().cacheDao().upsertMessages(
                messages.map { message ->
                    CachedMessageEntity(
                        scopeId = scope.id,
                        chatId = chatId,
                        messageId = message.id,
                        sentAtMillis = message.sentAt.seconds * 1000,
                        payload = message.toByteArray()
                    )
                }
            )
        }
    }
    suspend fun latestPrivateMessages(
        scope: CacheScope,
        chatId: String,
        limit: Int
    ): List<Shared.EncryptedMessage> = withContext(Dispatchers.IO) {
        database().cacheDao().latestPrivateMessages(scope.id, chatId, limit)
            .mapNotNull { parsePrivateMessage(it.payload) }
            .sortedBy { it.sentAt.seconds }
    }

    suspend fun savePrivateMessages(
        scope: CacheScope,
        chatId: String,
        messages: List<Shared.EncryptedMessage>
    ) {
        if (messages.isEmpty()) return
        withContext(Dispatchers.IO) {
            database().cacheDao().upsertPrivateMessages(messages.map { message ->
                CachedPrivateMessageEntity(
                    scopeId = scope.id,
                    chatId = chatId,
                    messageId = message.id,
                    sentAtMillis = message.sentAt.seconds * 1000,
                    payload = message.toByteArray()
                )
            })
        }
    }

    suspend fun deletePrivateMessage(scope: CacheScope, chatId: String, messageId: Long) =
        withContext(Dispatchers.IO) {
            database().cacheDao().deletePrivateMessage(scope.id, chatId, messageId)
        }

    suspend fun latestSecretMessages(
        scope: CacheScope,
        chatId: String,
        limit: Int
    ): List<CachedSecretMessage> = withContext(Dispatchers.IO) {
        database().cacheDao().latestSecretMessages(scope.id, chatId, limit)
            .asReversed()
            .map { CachedSecretMessage(it.messageId, it.senderLabel, it.plaintext, it.sentAtMillis) }
    }

    suspend fun saveSecretMessage(
        scope: CacheScope,
        chatId: String,
        messageId: String,
        senderLabel: String,
        plaintext: String,
        sentAtMillis: Long
    ) = withContext(Dispatchers.IO) {
        database().cacheDao().upsertSecretMessage(
            CachedSecretMessageEntity(scope.id, chatId, messageId, senderLabel, plaintext, sentAtMillis)
        )
    }

    suspend fun updateReadBy(
        scope: CacheScope,
        chatId: String,
        messageId: Long,
        readBy: List<Long>
    ) = withContext(Dispatchers.IO) {
        val dao = database().cacheDao()
        val entity = dao.message(scope.id, chatId, messageId) ?: return@withContext
        val message = parseMessage(entity.payload) ?: return@withContext
        val updated = message.toBuilder().clearReadBy().addAllReadBy(readBy).build()
        dao.upsertMessages(listOf(entity.copy(payload = updated.toByteArray())))
    }

    suspend fun deleteMessage(scope: CacheScope, chatId: String, messageId: Long) =
        withContext(Dispatchers.IO) {
            database().cacheDao().deleteMessage(scope.id, chatId, messageId)
        }

    suspend fun stats(): ChatCacheStats = withContext(Dispatchers.IO) {
        val dao = database().cacheDao()
        ChatCacheStats(
            chatCount = dao.chatCount(),
            messageCount = dao.messageCount() + dao.privateMessageCount() + dao.secretMessageCount(),
            sizeBytes = databaseSize()
        )
    }

    suspend fun clearAll() {
        databaseMutex.withLock {
            withContext(Dispatchers.IO) {
                database?.close()
                database = null
                appContext.deleteDatabase(DATABASE_NAME)
                File(appContext.getDatabasePath(DATABASE_NAME).path + "-wal").delete()
                File(appContext.getDatabasePath(DATABASE_NAME).path + "-shm").delete()
                securePreferences.edit().remove(KEY_PASSPHRASE).apply()
            }
        }
        _clearedEvents.tryEmit(Unit)
    }

    private suspend fun database(): ChatCacheDatabase = databaseMutex.withLock {
        database ?: buildDatabase().also { database = it }
    }

    private fun buildDatabase(): ChatCacheDatabase {
        val passphrase = databasePassphrase()
        return Room.databaseBuilder(appContext, ChatCacheDatabase::class.java, DATABASE_NAME)
            .openHelperFactory(SupportOpenHelperFactory(passphrase))
            .addMigrations(MIGRATION_1_2)
            .build()
    }

    private fun databasePassphrase(): ByteArray {
        securePreferences.getString(KEY_PASSPHRASE, null)?.let {
            return Base64.decode(it, Base64.NO_WRAP)
        }
        val random = ByteArray(32)
        SecureRandom().nextBytes(random)
        securePreferences.edit()
            .putString(KEY_PASSPHRASE, Base64.encodeToString(random, Base64.NO_WRAP))
            .apply()
        return random
    }

    private fun databaseSize(): Long {
        val databaseFile = appContext.getDatabasePath(DATABASE_NAME)
        return databaseFile.length() +
            File(databaseFile.path + "-wal").length() +
            File(databaseFile.path + "-shm").length()
    }

    private fun List<CachedMessageEntity>.toMessages(): List<Shared.Message> =
        mapNotNull { parseMessage(it.payload) }.sortedBy { it.sentAt.seconds }

    private fun parseMessage(payload: ByteArray): Shared.Message? =
        runCatching { Shared.Message.parseFrom(payload) }.getOrNull()

    private fun parsePrivateMessage(payload: ByteArray): Shared.EncryptedMessage? =
        runCatching { Shared.EncryptedMessage.parseFrom(payload) }.getOrNull()

    private fun GrpcManager.ChatData.toEntity(scopeId: String): CachedChatEntity =
        CachedChatEntity(
            scopeId = scopeId,
            chatId = id,
            title = title,
            picture = picture,
            pictureFileId = pictureFileId,
            picturePreviewFileId = picturePreviewFileId,
            isGroupChat = isGroupChat,
            lastMessageId = lastMessage?.id,
            lastMessageSenderId = lastMessage?.senderId,
            lastMessageText = lastMessage?.text,
            lastMessageSentAt = lastMessage?.sentAt,
            lastMessageReadBy = lastMessage?.readBy.orEmpty().joinToString(SEPARATOR),
            memberIds = memberIds.joinToString(SEPARATOR),
            countUnread = countUnread,
            firstUnreadMessageId = firstUnreadMessageId,
            chatTypeNumber = chatType.number,
            lastActivityAt = lastActivityAt,
            privateInviteStateNumber = privateInviteState.number,
            privateInviterUserId = privateInviterUserId,
            hasDraft = hasDraft
        )

    private fun CachedChatEntity.toChatData(): GrpcManager.ChatData {
        val lastMessage = if (lastMessageId == null || lastMessageSenderId == null ||
            lastMessageText == null || lastMessageSentAt == null
        ) {
            null
        } else {
            GrpcManager.LastMessageData(
                id = lastMessageId,
                senderId = lastMessageSenderId,
                text = lastMessageText,
                sentAt = lastMessageSentAt,
                readBy = lastMessageReadBy.toLongList()
            )
        }
        return GrpcManager.ChatData(
            id = chatId,
            title = title,
            picture = picture,
            pictureFileId = pictureFileId,
            picturePreviewFileId = picturePreviewFileId,
            isGroupChat = isGroupChat,
            lastMessage = lastMessage,
            memberIds = memberIds.toLongList(),
            countUnread = countUnread,
            firstUnreadMessageId = firstUnreadMessageId,
            chatType = Shared.ChatType.forNumber(chatTypeNumber) ?: Shared.ChatType.CHAT_TYPE_REGULAR,
            lastActivityAt = lastActivityAt,
            privateInviteState = Shared.PrivateChatInviteState.forNumber(privateInviteStateNumber)
                ?: Shared.PrivateChatInviteState.PRIVATE_CHAT_INVITE_STATE_ACCEPTED,
            privateInviterUserId = privateInviterUserId,
            hasDraft = hasDraft
        )
    }

    private fun GrpcManager.ChatFolder.toEntity(scopeId: String) = CachedChatFolderEntity(
        scopeId = scopeId,
        folderId = folderId,
        folderName = folderName,
        folderIcon = folderIcon,
        chatIds = chatIds.joinToString(SEPARATOR),
        sortOrder = sortOrder
    )

    private fun CachedChatFolderEntity.toChatFolder() = GrpcManager.ChatFolder(
        folderId = folderId,
        folderName = folderName,
        folderIcon = folderIcon,
        chatIds = chatIds.toStringList(),
        sortOrder = sortOrder
    )

    private fun String.toLongList(): List<Long> =
        if (isBlank()) emptyList() else split(SEPARATOR).mapNotNull { it.toLongOrNull() }

    private fun String.toStringList(): List<String> =
        if (isBlank()) emptyList() else split(SEPARATOR)

    private companion object {
        const val DATABASE_NAME = "offline_chat_cache.db"
        const val KEY_PREFERENCES = "offline_chat_cache_secure"
        const val KEY_PASSPHRASE = "database_passphrase"
        const val SEPARATOR = ""
        val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE cached_chats ADD COLUMN hasDraft INTEGER NOT NULL DEFAULT 0")
                db.execSQL(
                    "CREATE TABLE IF NOT EXISTS cached_chat_drafts (" +
                        "scopeId TEXT NOT NULL, chatId TEXT NOT NULL, text TEXT NOT NULL, " +
                        "replyToMessageId INTEGER NOT NULL, revision TEXT NOT NULL, " +
                        "generation INTEGER NOT NULL, syncState INTEGER NOT NULL, " +
                        "PRIMARY KEY(scopeId, chatId))"
                )
            }
        }
    }
}
