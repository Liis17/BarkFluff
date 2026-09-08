package com.barkfluff.client.cache

import androidx.room.testing.MigrationTestHelper
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OutgoingMessageMigrationTest {

    @get:Rule
    val helper = MigrationTestHelper(
        InstrumentationRegistry.getInstrumentation(),
        ChatCacheDatabase::class.java
    )

    @Test
    fun migrationFrom2PreservesCachedMessagesAndAddsOutboxTables() {
        val name = "outgoing-migration-test"
        helper.createDatabase(name, 2).apply {
            execSQL(
                "INSERT INTO cached_messages(scopeId, chatId, messageId, sentAtMillis, payload) " +
                    "VALUES ('scope', 'chat', 1, 123, X'00')"
            )
            execSQL(
                "INSERT INTO cached_chats(scopeId, chatId, title, picture, pictureFileId, " +
                    "picturePreviewFileId, isGroupChat, lastMessageId, lastMessageSenderId, " +
                    "lastMessageText, lastMessageSentAt, lastMessageReadBy, memberIds, countUnread, " +
                    "firstUnreadMessageId, chatTypeNumber, lastActivityAt, privateInviteStateNumber, " +
                    "privateInviterUserId, hasDraft) VALUES " +
                    "('scope', 'chat', 'Chat', '', '', '', 0, NULL, NULL, NULL, NULL, '', '', " +
                    "0, 0, 0, 0, 0, 0, 0)"
            )
            execSQL(
                "INSERT INTO cached_chat_drafts(scopeId, chatId, text, replyToMessageId, revision, generation, syncState) " +
                    "VALUES ('scope', 'chat', 'draft', 0, 'r1', 1, 0)"
            )
            close()
        }

        helper.runMigrationsAndValidate(name, 3, true, ChatCacheRepository.MIGRATION_2_3).apply {
            query("SELECT COUNT(*) FROM cached_messages").use { cursor ->
                cursor.moveToFirst()
                assertEquals(1, cursor.getInt(0))
            }
            query("SELECT COUNT(*) FROM cached_chats").use { cursor ->
                cursor.moveToFirst()
                assertEquals(1, cursor.getInt(0))
            }
            query("SELECT COUNT(*) FROM cached_chat_drafts").use { cursor ->
                cursor.moveToFirst()
                assertEquals(1, cursor.getInt(0))
            }
            query("SELECT COUNT(*) FROM outgoing_messages").use { cursor ->
                cursor.moveToFirst()
                assertEquals(0, cursor.getInt(0))
            }
            query("SELECT COUNT(*) FROM outgoing_attachments").use { cursor ->
                cursor.moveToFirst()
                assertEquals(0, cursor.getInt(0))
            }
            close()
        }
    }
}
