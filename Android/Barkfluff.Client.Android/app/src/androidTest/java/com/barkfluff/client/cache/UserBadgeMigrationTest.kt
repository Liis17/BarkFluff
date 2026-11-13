package com.barkfluff.client.cache

import androidx.room.testing.MigrationTestHelper
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class UserBadgeMigrationTest {

    @get:Rule
    val helper = MigrationTestHelper(
        InstrumentationRegistry.getInstrumentation(),
        ChatCacheDatabase::class.java,
    )

    @Test
    fun migrationFrom6CreatesBadgeCacheAndPreservesExistingDrafts() {
        val name = "user-badges-migration-test"
        helper.createDatabase(name, 6).apply {
            execSQL(
                "INSERT INTO cached_chat_drafts(scopeId, chatId, text, replyToMessageId, revision, generation, syncState) " +
                    "VALUES ('scope', 'chat', 'draft', 0, 'r1', 1, 0)"
            )
            close()
        }

        helper.runMigrationsAndValidate(name, 7, true, ChatCacheRepository.MIGRATION_6_7).apply {
            query("SELECT text FROM cached_chat_drafts WHERE scopeId = 'scope' AND chatId = 'chat'").use { cursor ->
                cursor.moveToFirst()
                assertEquals("draft", cursor.getString(0))
            }
            execSQL(
                "INSERT INTO cached_user_badges(scopeId, userId, badgeId, name, description, imageUrl, " +
                    "miniImageUrl, assignedAt, isPrimary, position) " +
                    "VALUES ('scope', 42, 7, 'Helper', '', 'full.png', 'mini.png', 1234, 1, 0)"
            )
            query("SELECT userId, badgeId, miniImageUrl, assignedAt, isPrimary, position FROM cached_user_badges").use { cursor ->
                cursor.moveToFirst()
                assertEquals(42L, cursor.getLong(0))
                assertEquals(7, cursor.getInt(1))
                assertEquals("mini.png", cursor.getString(2))
                assertEquals(1234L, cursor.getLong(3))
                assertEquals(1, cursor.getInt(4))
                assertEquals(0, cursor.getInt(5))
            }
            close()
        }
    }
}
