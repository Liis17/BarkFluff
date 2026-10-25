package com.barkfluff.client.cache

import android.os.SystemClock
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import java.io.File
import java.security.SecureRandom
import net.zetetic.database.sqlcipher.SQLiteDatabase
import org.junit.After
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class SqlCipherRawKeyInstrumentedTest {
    private val context = InstrumentationRegistry.getInstrumentation().targetContext
    private lateinit var file: File

    @Before
    fun setUp() {
        System.loadLibrary("sqlcipher")
        file = context.getDatabasePath("raw_key_test.db")
        deleteDatabaseFiles()
    }

    @After
    fun tearDown() = deleteDatabaseFiles()

    @Test
    fun derivedRawKeyOpensPassphraseDatabaseWithWalAndIsFaster() {
        val passphrase = ByteArray(32).also(SecureRandom()::nextBytes)
        SQLiteDatabase.openOrCreateDatabase(file, passphrase, null, null, null).use { db ->
            db.enableWriteAheadLogging()
            db.execSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT)")
            db.execSQL("INSERT INTO t (v) VALUES ('a'), ('b'), ('c')")
        }

        val salt = fileSalt()
        val deriveStart = SystemClock.elapsedRealtime()
        val rawKey = SqlCipherRawKey.format(SqlCipherRawKey.deriveFromPassphrase(passphrase, salt))
            .toByteArray(Charsets.US_ASCII)
        val deriveMs = SystemClock.elapsedRealtime() - deriveStart

        val passphraseMs = timedRowCount(passphrase)
        val rawMs = timedRowCount(rawKey)
        Log.i("SqlCipherRawKeyTest", "derive=${deriveMs}ms open+query passphrase=${passphraseMs}ms raw=${rawMs}ms")

        assertTrue("raw key open should skip PBKDF2: passphrase=$passphraseMs raw=$rawMs", rawMs * 3 < passphraseMs)
    }

    @Test
    fun vacuumKeepsSaltAndReadOnlyOpenKeepsWal() {
        val passphrase = ByteArray(32).also(SecureRandom()::nextBytes)
        SQLiteDatabase.openOrCreateDatabase(file, passphrase, null, null, null).use { db ->
            db.enableWriteAheadLogging()
            db.execSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT)")
            db.execSQL("INSERT INTO t (v) VALUES ('a'), ('b'), ('c')")
        }
        val salt = fileSalt()
        val rawKey = SqlCipherRawKey.format(SqlCipherRawKey.deriveFromPassphrase(passphrase, salt))
            .toByteArray(Charsets.US_ASCII)

        SQLiteDatabase.openDatabase(file.path, rawKey, null, SQLiteDatabase.OPEN_READONLY, null, null).close()
        SQLiteDatabase.openDatabase(
            file.path, passphrase, null, SQLiteDatabase.OPEN_READWRITE or SQLiteDatabase.ENABLE_WRITE_AHEAD_LOGGING, null, null
        ).use { db ->
            db.rawQuery("PRAGMA journal_mode", null).use { cursor ->
                cursor.moveToFirst()
                assertEquals("wal", cursor.getString(0))
            }
            db.execSQL("VACUUM")
        }

        assertArrayEquals(salt, fileSalt())
        timedRowCount(rawKey)
    }

    private fun fileSalt(): ByteArray =
        file.inputStream().use { input -> ByteArray(SqlCipherRawKey.SALT_LENGTH).also { input.read(it) } }

    private fun timedRowCount(key: ByteArray): Long {
        val start = SystemClock.elapsedRealtime()
        SQLiteDatabase.openDatabase(file.path, key, null, SQLiteDatabase.OPEN_READWRITE, null, null).use { db ->
            db.rawQuery("SELECT count(*) FROM t", null).use { cursor ->
                cursor.moveToFirst()
                assertEquals(3, cursor.getInt(0))
            }
        }
        return SystemClock.elapsedRealtime() - start
    }

    private fun deleteDatabaseFiles() {
        context.deleteDatabase(file.name)
    }
}
