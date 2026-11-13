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
    fun migrationFrom6To8CreatesBadgeCacheAndPreservesExistingDrafts() {
        val name = "user-badges-migration-from-6-test"
        helper.createDatabase(name, 6).apply {
            execSQL(
                "INSERT INTO cached_chat_drafts(scopeId, chatId, text, replyToMessageId, revision, generation, syncState) " +
                    "VALUES ('scope', 'chat', 'draft', 0, 'r1', 1, 0)"
            )
            execSQL(
                "INSERT INTO cached_chat_displays(scopeId, chatId, title, avatarFileId, otherUserId) " +
                    "VALUES ('scope', 'chat', 'title', NULL, 42)"
            )
            close()
        }

        helper.runMigrationsAndValidate(
            name,
            8,
            true,
            ChatCacheRepository.MIGRATION_6_7,
            ChatCacheRepository.MIGRATION_7_8
        ).apply {
            query("SELECT text FROM cached_chat_drafts WHERE scopeId = 'scope' AND chatId = 'chat'").use { cursor ->
                cursor.moveToFirst()
                assertEquals("draft", cursor.getString(0))
            }
            query("SELECT accountStatusNumber FROM cached_chat_displays WHERE scopeId = 'scope' AND chatId = 'chat'").use { cursor ->
                cursor.moveToFirst()
                assertEquals(0, cursor.getInt(0))
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

    @Test
    fun migrationFromNightlyVersion7PreservesBadgeCacheAndAddsAccountStatus() {
        val name = "user-badges-migration-from-7-test"
        helper.createDatabase(name, 7).apply {
            execSQL(
                "INSERT INTO cached_chat_displays(scopeId, chatId, title, avatarFileId, otherUserId) " +
                    "VALUES ('scope', 'chat', 'title', NULL, 42)"
            )
            execSQL(
                "INSERT INTO cached_user_badges(scopeId, userId, badgeId, name, description, imageUrl, " +
                    "miniImageUrl, assignedAt, isPrimary, position) " +
                    "VALUES ('scope', 42, 7, 'Helper', '', 'full.png', 'mini.png', 1234, 1, 0)"
            )
            close()
        }

        helper.runMigrationsAndValidate(name, 8, true, ChatCacheRepository.MIGRATION_7_8).apply {
            query("SELECT badgeId, miniImageUrl, isPrimary FROM cached_user_badges WHERE scopeId = 'scope' AND userId = 42").use { cursor ->
                cursor.moveToFirst()
                assertEquals(7, cursor.getInt(0))
                assertEquals("mini.png", cursor.getString(1))
                assertEquals(1, cursor.getInt(2))
            }
            query("SELECT accountStatusNumber FROM cached_chat_displays WHERE scopeId = 'scope' AND chatId = 'chat'").use { cursor ->
                cursor.moveToFirst()
                assertEquals(0, cursor.getInt(0))
            }
            close()
        }
    }
}
