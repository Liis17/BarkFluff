package com.barkfluff.client.crypto

import android.content.Context
import android.content.ContextWrapper
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Assert.assertFalse
import org.junit.Test
import org.junit.runner.RunWith
import org.signal.libsignal.protocol.IdentityKeyPair
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class BarkFluffSignalStoreInstrumentedTest {

    @Test
    fun failedCryptoOperationRollsBackAndReopenedStoreKeepsCommittedIdentity() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val scope = newScope()
        val store = newStore(context, scope)
        val committedIdentity = IdentityKeyPair.generate()
        try {
            store.transaction { store.initialize(committedIdentity, 101) }

            assertThrows(IllegalStateException::class.java) {
                store.transaction {
                    store.initialize(IdentityKeyPair.generate(), 202)
                    error("interrupt the staged crypto operation")
                }
            }

            val reopened = newStore(context, scope)
            assertArrayEquals(committedIdentity.serialize(), reopened.getIdentityKeyPair().serialize())
            assertEquals(101, reopened.getLocalRegistrationId())
        } finally {
            store.reset()
            deleteScope(context, scope)
        }
    }

    @Test
    fun failedLogoutCleanupRetainsCapturedScopeAndRetriesAfterCredentialsDisappear() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val oldScope = newScope()
        val unrelatedScope = newScope()
        var activeScope: CryptoStoreScope? = oldScope
        val old = newStore(context, oldScope)
        val unrelated = newStore(context, unrelatedScope)
        try {
            old.transaction { old.initialize(IdentityKeyPair.generate(), 101) }
            unrelated.transaction { unrelated.initialize(IdentityKeyPair.generate(), 202) }
            val runningOperation = old.captureOperation()
            val rejecting = BarkFluffSignalStore(RejectCommitContext(context, scopedFileName(oldScope))) { activeScope }
            val captured = rejecting.invalidateForLogout()
            assertThrows(DurableCryptoCommitException::class.java) { rejecting.reset(captured) }
            assertTrue(rejecting.hasPendingCleanup(oldScope))
            assertThrows(CryptoOperationInvalidatedException::class.java) { old.assertCurrent(runningOperation) }
            activeScope = null
            // Reopening with no active credentials still has enough information for exact cleanup.
            BarkFluffSignalStore(context) { activeScope }.retryPendingCleanup()
            assertFalse(newStore(context, oldScope).isInitialized())
            assertTrue(newStore(context, unrelatedScope).isInitialized())
            assertFalse(rejecting.hasPendingCleanup(oldScope))
        } finally {
            old.reset()
            unrelated.reset()
            deleteScope(context, oldScope)
            deleteScope(context, unrelatedScope)
        }
    }

    @Test
    fun failedDurableCommitLeavesPriorSnapshotAndFailsClosed() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val scope = newScope()
        val name = scopedFileName(scope)
        val store = newStore(context, scope)
        try {
            store.transaction { store.initialize(IdentityKeyPair.generate(), 303) }
            val before = encryptedPreferences(context, name).getString(STATE_BLOB_KEY, null)

            val rejectingContext = RejectCommitContext(context, name)
            val rejectingStore = BarkFluffSignalStore(rejectingContext) { scope }
            assertEquals(303, rejectingStore.transaction { rejectingStore.getLocalRegistrationId() })
            assertThrows(DurableCryptoCommitException::class.java) {
                rejectingStore.transaction {
                    rejectingStore.initialize(IdentityKeyPair.generate(), 404)
                }
            }
            assertThrows(IllegalStateException::class.java) { rejectingStore.getIdentityKeyPair() }

            val after = encryptedPreferences(context, name).getString(STATE_BLOB_KEY, null)
            assertEquals(before, after)
        } finally {
            store.reset()
            deleteScope(context, scope)
        }
    }

    @Test
    fun logoutClearsUnmigratedLegacyFilesForEmptyAndNullScopesButKeepsOtherAccounts() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val emptyScope = newScope()
        val otherScope = newScope()
        val other = newStore(context, otherScope)
        try {
            other.transaction { other.initialize(IdentityKeyPair.generate(), 505) }

            seedLegacyFiles(context)
            newStore(context, emptyScope).reset(emptyScope)
            assertLegacyFilesEmpty(context)

            seedLegacyFiles(context)
            BarkFluffSignalStore(context) { null }.reset(null)
            assertLegacyFilesEmpty(context)

            assertTrue(newStore(context, otherScope).isInitialized())
        } finally {
            other.reset()
            deleteScope(context, emptyScope)
            deleteScope(context, otherScope)
        }
    }

    @Test
    fun retryPendingCleanupAttemptsEveryJournaledScopeAndKeepsOnlyFailedOnes() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val failing = newScope()
        val cleanable = newScope()
        try {
            listOf(failing, cleanable).forEach { scope ->
                val store = newStore(context, scope)
                store.transaction { store.initialize(IdentityKeyPair.generate(), 606) }
                val rejecting = BarkFluffSignalStore(RejectCommitContext(context, scopedFileName(scope))) { scope }
                assertThrows(DurableCryptoCommitException::class.java) { rejecting.reset(rejecting.invalidateForLogout()) }
            }

            val stillFailing = BarkFluffSignalStore(RejectCommitContext(context, scopedFileName(failing))) { null }
            assertThrows(DurableCryptoCommitException::class.java) { stillFailing.retryPendingCleanup() }

            assertFalse(stillFailing.hasPendingCleanup(cleanable))
            assertFalse(newStore(context, cleanable).isInitialized())
            assertTrue(stillFailing.hasPendingCleanup(failing))

            BarkFluffSignalStore(context) { null }.retryPendingCleanup()
            assertFalse(stillFailing.hasPendingCleanup(failing))
        } finally {
            deleteScope(context, failing)
            deleteScope(context, cleanable)
        }
    }

    private fun seedLegacyFiles(context: Context) {
        assertTrue(encryptedPreferences(context, LEGACY_SIGNAL_FILE).edit().putString("self::identity_key", "legacy-private").commit())
        assertTrue(encryptedPreferences(context, LEGACY_SECRET_CHATS_FILE).edit().putString("chat", "legacy-chat").commit())
        assertTrue(context.getSharedPreferences(LEGACY_PREKEY_STATE_FILE, Context.MODE_PRIVATE).edit().putInt("next", 7).commit())
    }

    private fun assertLegacyFilesEmpty(context: Context) {
        assertTrue(encryptedPreferences(context, LEGACY_SIGNAL_FILE).all.isEmpty())
        assertTrue(encryptedPreferences(context, LEGACY_SECRET_CHATS_FILE).all.isEmpty())
        assertTrue(context.getSharedPreferences(LEGACY_PREKEY_STATE_FILE, Context.MODE_PRIVATE).all.isEmpty())
    }

    private fun newStore(context: Context, scope: CryptoStoreScope) = BarkFluffSignalStore(context) { scope }

    private fun newScope() = CryptoStoreScope(
        server = "instrumentation://store-${UUID.randomUUID()}",
        userId = UUID.randomUUID().mostSignificantBits.ushr(1).coerceAtLeast(1),
        deviceId = UUID.randomUUID().toString(),
    )

    private fun scopedFileName(scope: CryptoStoreScope): String =
        "barkfluff_crypto_" + MessageDigest.getInstance("SHA-256")
            .digest(scope.scopeId.toByteArray(StandardCharsets.UTF_8))
            .joinToString("") { "%02x".format(it) }

    private fun encryptedPreferences(context: Context, name: String): SharedPreferences =
        EncryptedSharedPreferences.create(
            name,
            MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC),
            context,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )

    private fun deleteScope(context: Context, scope: CryptoStoreScope) {
        context.deleteSharedPreferences(scopedFileName(scope))
    }

    private class RejectCommitContext(
        base: Context,
        private val rejectedFileName: String,
    ) : ContextWrapper(base) {
        override fun getApplicationContext(): Context = this

        override fun getSharedPreferences(name: String, mode: Int): SharedPreferences {
            val preferences = super.getSharedPreferences(name, mode)
            return if (name == rejectedFileName) RejectCommitPreferences(preferences) else preferences
        }
    }

    private class RejectCommitPreferences(
        private val delegate: SharedPreferences,
    ) : SharedPreferences by delegate {
        override fun edit(): SharedPreferences.Editor = RejectCommitEditor(delegate.edit())
    }

    private class RejectCommitEditor(
        private val delegate: SharedPreferences.Editor,
    ) : SharedPreferences.Editor {
        override fun putString(key: String, value: String?): SharedPreferences.Editor =
            applyToDelegate { putString(key, value) }

        override fun putStringSet(key: String, values: MutableSet<String>?): SharedPreferences.Editor =
            applyToDelegate { putStringSet(key, values) }

        override fun putInt(key: String, value: Int): SharedPreferences.Editor =
            applyToDelegate { putInt(key, value) }

        override fun putLong(key: String, value: Long): SharedPreferences.Editor =
            applyToDelegate { putLong(key, value) }

        override fun putFloat(key: String, value: Float): SharedPreferences.Editor =
            applyToDelegate { putFloat(key, value) }

        override fun putBoolean(key: String, value: Boolean): SharedPreferences.Editor =
            applyToDelegate { putBoolean(key, value) }

        override fun remove(key: String): SharedPreferences.Editor = applyToDelegate { remove(key) }

        override fun clear(): SharedPreferences.Editor = applyToDelegate { clear() }

        override fun commit(): Boolean = false

        override fun apply() = Unit

        private inline fun applyToDelegate(block: SharedPreferences.Editor.() -> SharedPreferences.Editor): SharedPreferences.Editor {
            delegate.block()
            return this
        }
    }

    private companion object {
        const val STATE_BLOB_KEY = "crypto_state"
        const val LEGACY_SIGNAL_FILE = "barkfluff_signal_store"
        const val LEGACY_SECRET_CHATS_FILE = "barkfluff_secret_chats"
        const val LEGACY_PREKEY_STATE_FILE = "barkfluff_prekey_state"
    }
}
