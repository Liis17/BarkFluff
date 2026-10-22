package com.barkfluff.client.crypto

import android.content.Context
import android.content.SharedPreferences
import android.util.Base64
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import com.barkfluff.client.data.GlobalParam
import org.signal.libsignal.protocol.IdentityKey
import org.signal.libsignal.protocol.IdentityKeyPair
import org.signal.libsignal.protocol.InvalidKeyIdException
import org.signal.libsignal.protocol.NoSessionException
import org.signal.libsignal.protocol.ReusedBaseKeyException
import org.signal.libsignal.protocol.SignalProtocolAddress
import org.signal.libsignal.protocol.ecc.ECPublicKey
import org.signal.libsignal.protocol.groups.state.SenderKeyRecord
import org.signal.libsignal.protocol.groups.state.SenderKeyStore
import org.signal.libsignal.protocol.state.IdentityKeyStore
import org.signal.libsignal.protocol.state.KyberPreKeyRecord
import org.signal.libsignal.protocol.state.KyberPreKeyStore
import org.signal.libsignal.protocol.state.PreKeyRecord
import org.signal.libsignal.protocol.state.PreKeyStore
import org.signal.libsignal.protocol.state.SessionRecord
import org.signal.libsignal.protocol.state.SessionStore
import org.signal.libsignal.protocol.state.SignalProtocolStore
import org.signal.libsignal.protocol.state.SignedPreKeyRecord
import org.signal.libsignal.protocol.state.SignedPreKeyStore
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

/** Account, server, and device identity used to isolate all local Signal state. */
data class CryptoStoreScope(
    val server: String,
    val userId: Long,
    val deviceId: String,
) {
    init {
        require(server.isNotBlank() && userId > 0 && deviceId.isNotBlank())
    }

    val scopeId: String get() = "$server|$userId|$deviceId"
    internal val id: String get() = scopeId

    companion object {
        fun from(globalParam: GlobalParam): CryptoStoreScope? {
            val server = globalParam.socketUsers.trim().trimEnd('/')
            val userId = globalParam.userId
            if (server.isBlank() || userId <= 0) return null
            val deviceId = globalParam.deviceId.trim()
            if (deviceId.isBlank()) return null
            return CryptoStoreScope(server, userId, deviceId)
        }
    }
}

class CryptoOperation internal constructor(
    internal val scope: CryptoStoreScope,
    internal val generation: Long,
    internal val scopeGeneration: Long,
)

class CryptoOperationInvalidatedException : IllegalStateException("Crypto operation belongs to a previous account or device")

class DurableCryptoCommitException(cause: Throwable? = null) : IllegalStateException("Could not durably save encrypted crypto state", cause)

/** Logout cleanup could not be journaled, so credentials must be retained for a retry. */
class CryptoCleanupJournalException(cause: Throwable? = null) : IllegalStateException("Could not record crypto cleanup", cause)

/**
 * One encrypted snapshot contains libsignal keys and sessions, prekey publication state, secret
 * chat metadata, and replay records. Libsignal calls are staged in [transaction] and saved by one
 * checked synchronous preferences commit only after the complete operation succeeds.
 */
class BarkFluffSignalStore(
    context: Context,
    private val scopeProvider: () -> CryptoStoreScope? = { CryptoStoreScope.from(GlobalParam(context)) },
) : SignalProtocolStore {

    private val appContext = context.applicationContext
    private val lock = PROCESS_LOCK
    private val transactionState = ThreadLocal<State?>()
    private val preferenceFiles = HashMap<String, SharedPreferences>()
    private var generation = 0L
    private var invalidatedScope: CryptoStoreScope? = null
    private val masterKeyAlias by lazy { MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC) }

    fun captureOperation(): CryptoOperation = lock.withLock {
        val scope = currentScope()
        CryptoOperation(scope, generation, SCOPE_GENERATIONS[scope] ?: 0)
    }

    fun assertCurrent(operation: CryptoOperation) {
        lock.withLock {
            if (generation != operation.generation || SCOPE_GENERATIONS.getOrDefault(operation.scope, 0) != operation.scopeGeneration ||
                currentScope() != operation.scope) {
                throw CryptoOperationInvalidatedException()
            }
        }
    }

    fun <T> transaction(operation: CryptoOperation = captureOperation(), block: () -> T): T = lock.withLock {
        assertCurrent(operation)
        check(transactionState.get() == null) { "Nested crypto store transactions are not supported" }
        val state = readState(operation.scope)
        val before = state.entries.toMap()
        transactionState.set(state)
        try {
            val result = block()
            assertCurrent(operation)
            if (state.entries != before) writeState(operation.scope, state)
            result
        } finally {
            transactionState.remove()
        }
    }

    fun isInitialized(): Boolean = readOnly { it.entries.containsKey(KEY_IDENTITY_KEY) && it.entries.containsKey(KEY_REGISTRATION_ID) }

    fun initialize(identityKeyPair: IdentityKeyPair, registrationId: Int) = mutate { state ->
        state.entries[KEY_IDENTITY_KEY] = encode(identityKeyPair.serialize())
        state.entries[KEY_REGISTRATION_ID] = registrationId.toString()
    }

    /** Capture before credentials/device id are cleared; this does no encrypted-store I/O. */
    fun invalidateForLogout(): CryptoStoreScope? = lock.withLock {
        val scope = scopeProvider() ?: invalidatedScope
        generation++
        invalidatedScope = scope
        scope?.let { SCOPE_GENERATIONS[it] = (SCOPE_GENERATIONS[it] ?: 0) + 1 }
        scope
    }

    fun reset() = reset(invalidateForLogout())

    /**
     * Journals the exact scope and the unscoped legacy files, then clears both. Legacy files
     * belong to no account scope, so every logout removes them even when they were never migrated.
     * Throws [CryptoCleanupJournalException] when nothing was journaled.
     */
    fun reset(scope: CryptoStoreScope?) {
        lock.withLock {
            // Keep the exact scope retryable after clearUserData generates a new device id.
            val journal = cleanupPreferences().edit().putBoolean(KEY_LEGACY_CLEANUP, true)
            if (scope != null) {
                val bytes = ByteArrayOutputStream()
                DataOutputStream(bytes).use { it.writeUtf8(scope.server); it.writeLong(scope.userId); it.writeUtf8(scope.deviceId) }
                journal.putString(sha256(scope.id), encode(bytes.toByteArray()))
            }
            val journaled = try { journal.commit() } catch (e: Exception) { throw CryptoCleanupJournalException(e) }
            if (!journaled) throw CryptoCleanupJournalException()
            val failures = mutableListOf<Exception>()
            if (scope != null) {
                try {
                    clearScope(scope)
                } catch (e: DurableCryptoCommitException) {
                    failures += e
                } catch (e: Exception) {
                    FAILED_SCOPES += scope
                    failures += DurableCryptoCommitException(e)
                }
            }
            runCleanupStep(failures) { clearLegacyFiles() }
            throwAggregated(failures)
        }
    }

    fun hasPendingCleanup(scope: CryptoStoreScope?): Boolean = scope != null &&
        cleanupPreferences().contains(sha256(scope.id))

    /** Attempts every journaled cleanup independently; failed entries remain journaled. */
    fun retryPendingCleanup() = lock.withLock {
        val failures = mutableListOf<Exception>()
        val prefs = cleanupPreferences()
        prefs.all.forEach { (key, value) ->
            if (key == KEY_LEGACY_CLEANUP || value !is String) return@forEach
            runCleanupStep(failures) {
                val scope = DataInputStream(ByteArrayInputStream(decode(value))).use {
                    CryptoStoreScope(it.readUtf8(), it.readLong(), it.readUtf8())
                }
                clearScope(scope)
            }
        }
        if (prefs.getBoolean(KEY_LEGACY_CLEANUP, false)) runCleanupStep(failures) { clearLegacyFiles() }
        throwAggregated(failures)
    }

    private inline fun runCleanupStep(failures: MutableList<Exception>, step: () -> Unit) {
        try {
            step()
        } catch (e: Exception) {
            failures += e
        }
    }

    private fun throwAggregated(failures: List<Exception>) {
        val first = failures.firstOrNull() ?: return
        failures.drop(1).forEach(first::addSuppressed)
        throw first as? DurableCryptoCommitException ?: DurableCryptoCommitException(first)
    }

    /**
     * Records a user history clear outside the encrypted snapshot, so a scrub that failed in the
     * snapshot is repeated before pending replay plaintext can be handed to history again.
     */
    fun markHistoryClearPending(): CryptoStoreScope? = lock.withLock {
        val scope = scopeProvider() ?: return null
        if (!historyClearPreferences().edit().putBoolean(sha256(scope.id), true).commit()) throw DurableCryptoCommitException()
        scope
    }

    fun hasHistoryClearPending(scope: CryptoStoreScope): Boolean =
        historyClearPreferences().getBoolean(sha256(scope.id), false)

    fun completeHistoryClear(scope: CryptoStoreScope) {
        if (!historyClearPreferences().edit().remove(sha256(scope.id)).commit()) throw DurableCryptoCommitException()
    }

    private fun historyClearPreferences() =
        appContext.getSharedPreferences("barkfluff_secret_history_clear", Context.MODE_PRIVATE)

    private fun cleanupPreferences() = appContext.getSharedPreferences("barkfluff_crypto_cleanup", Context.MODE_PRIVATE)

    private fun clearLegacyFiles() {
        listOf(LEGACY_SECRET_CHATS_FILE, LEGACY_SIGNAL_FILE).forEach { name ->
            if (!legacyEncryptedPreferences(name).edit().clear().commit()) throw DurableCryptoCommitException()
        }
        if (!appContext.getSharedPreferences(LEGACY_PREKEY_STATE_FILE, Context.MODE_PRIVATE).edit().clear().commit()) {
            throw DurableCryptoCommitException()
        }
        if (!cleanupPreferences().edit().remove(KEY_LEGACY_CLEANUP).commit()) throw DurableCryptoCommitException()
    }

    private fun clearScope(scope: CryptoStoreScope) {
        if (!preferences(scope).edit().clear().commit()) {
            FAILED_SCOPES += scope
            throw DurableCryptoCommitException()
        }
        historyClearPreferences().edit().remove(sha256(scope.id)).commit()
        if (!cleanupPreferences().edit().remove(sha256(scope.id)).commit()) throw DurableCryptoCommitException()
        FAILED_SCOPES -= scope
    }

    /** Remove only EC one-time prekeys. Signed and Kyber last-resort keys are retained until reset. */
    fun clearOneTimePreKeys() = mutate { state ->
        state.entries.keys.filter { it.startsWith(PREFIX_PREKEY) }.forEach(state.entries::remove)
    }

    fun migrateLegacyIfMatching(serverIdentity: ByteArray, serverRegistrationId: Int): Boolean = lock.withLock {
        val scope = currentScope()
        val current = readState(scope)
        if (isInitialized(current)) return true
        if (cleanupPreferences().getBoolean(KEY_LEGACY_CLEANUP, false)) return false

        val legacy = legacyEncryptedPreferences(LEGACY_SIGNAL_FILE)
        val encodedIdentity = legacy.getString(KEY_IDENTITY_KEY, null) ?: return false
        val legacyRegistration = legacy.getInt(KEY_REGISTRATION_ID, -1)
        if (legacyRegistration != serverRegistrationId || legacyRegistration < 0) return false

        val legacyIdentity = runCatching { IdentityKeyPair(decode(encodedIdentity)).publicKey.serialize() }.getOrNull()
            ?: return false
        if (!legacyIdentity.contentEquals(serverIdentity)) return false

        legacy.all.forEach { (key, value) ->
            when (value) {
                is String -> current.entries[key] = value
                is Int, is Long, is Boolean -> current.entries[key] = value.toString()
            }
        }
        val oldPrekeyPrefs = appContext.getSharedPreferences(LEGACY_PREKEY_STATE_FILE, Context.MODE_PRIVATE)
        oldPrekeyPrefs.all.forEach { (key, value) ->
            when (value) {
                is String, is Int, is Long, is Boolean -> current.entries["$PREFIX_MANAGER$key"] = value.toString()
            }
        }
        legacyEncryptedPreferences(LEGACY_SECRET_CHATS_FILE).all.forEach { (key, value) ->
            if (value is String) current.entries[secretChatKey(key)] = value
        }
        current.entries["$PREFIX_MANAGER${KEY_LEGACY_MIGRATED}"] = "1"
        writeState(scope, current)
        true
    }

    fun readMetadata(key: String): String? = readOnly { it.entries[metadataKey(key)] }

    fun writeMetadata(key: String, value: String?) = mutate { state ->
        val storageKey = metadataKey(key)
        if (value == null) state.entries.remove(storageKey) else state.entries[storageKey] = value
    }

    fun allMetadata(prefix: String): Map<String, String> = readOnly { state ->
        state.entries.entries.asSequence()
            .filter { it.key.startsWith(metadataKey(prefix)) }
            .associate { it.key.removePrefix(METADATA_PREFIX) to it.value }
    }

    fun readManagerValue(key: String): String? = readOnly { it.entries[managerKey(key)] }

    fun writeManagerValue(key: String, value: String?) = mutate { state ->
        val storageKey = managerKey(key)
        if (value == null) state.entries.remove(storageKey) else state.entries[storageKey] = value
    }

    fun forgetSecretChats() = mutate { state ->
        state.entries.keys.filter {
            it.startsWith("$METADATA_PREFIX$SECRET_CHAT_NAMESPACE")
        }
            .forEach(state.entries::remove)
    }

    fun addLastResortKyberPreKeyId(id: Int) = mutate { state ->
        state.entries["$PREFIX_LAST_RESORT$id"] = "1"
    }

    fun hasLastResortKyberPreKeyId(id: Int): Boolean = readOnly { it.entries.containsKey("$PREFIX_LAST_RESORT$id") }

    fun recordReplay(key: String, value: String) = mutate { state ->
        val existing = state.entries["$REPLAY_PREFIX$key"]
        if (existing != null && existing != value) throw IllegalStateException("Replay record collision")
        state.entries["$REPLAY_PREFIX$key"] = value
    }

    fun readReplay(key: String): String? = readOnly { it.entries["$REPLAY_PREFIX$key"] }

    fun allReplay(): Map<String, String> = readOnly { state ->
        state.entries.filterKeys { it.startsWith("${REPLAY_PREFIX}invite:") || it.startsWith("${REPLAY_PREFIX}message:") }
            .mapKeys { it.key.removePrefix(REPLAY_PREFIX) }
    }

    /** Replace delivery payload with its tombstone; never remove the consumed-ratchet marker. */
    fun updateReplay(key: String, value: String) = mutate { state ->
        check(state.entries.containsKey("$REPLAY_PREFIX$key")) { "Missing replay record" }
        state.entries["$REPLAY_PREFIX$key"] = value
    }

    // --- IdentityKeyStore ----------------------------------------------------

    override fun getIdentityKeyPair(): IdentityKeyPair = readOnly { state ->
        val encoded = state.entries[KEY_IDENTITY_KEY] ?: throw IllegalStateException("Identity key not initialized")
        IdentityKeyPair(decode(encoded))
    }

    override fun getLocalRegistrationId(): Int = readOnly { state ->
        state.entries[KEY_REGISTRATION_ID]?.toIntOrNull()
            ?: throw IllegalStateException("Registration id not initialized")
    }

    override fun saveIdentity(address: SignalProtocolAddress, identityKey: IdentityKey): IdentityKeyStore.IdentityChange = mutateResult { state ->
        val key = identityKeyPref(address)
        val previousEncoded = state.entries[key]
        state.entries[key] = encode(identityKey.serialize())
        if (previousEncoded == null) IdentityKeyStore.IdentityChange.NEW_OR_UNCHANGED
        else if (IdentityKey(decode(previousEncoded), 0) == identityKey) IdentityKeyStore.IdentityChange.NEW_OR_UNCHANGED
        else IdentityKeyStore.IdentityChange.REPLACED_EXISTING
    }

    override fun isTrustedIdentity(
        address: SignalProtocolAddress,
        identityKey: IdentityKey,
        direction: IdentityKeyStore.Direction,
    ): Boolean = readOnly { state ->
        val stored = state.entries[identityKeyPref(address)] ?: return@readOnly true
        IdentityKey(decode(stored), 0) == identityKey
    }

    override fun getIdentity(address: SignalProtocolAddress): IdentityKey? = readOnly { state ->
        state.entries[identityKeyPref(address)]?.let { IdentityKey(decode(it), 0) }
    }

    // --- PreKeyStore ---------------------------------------------------------

    override fun loadPreKey(preKeyId: Int): PreKeyRecord = readOnly { state ->
        val encoded = state.entries[prekeyPref(preKeyId)] ?: throw InvalidKeyIdException("No PreKey with id $preKeyId")
        PreKeyRecord(decode(encoded))
    }

    override fun storePreKey(preKeyId: Int, record: PreKeyRecord) = mutate { state ->
        state.entries[prekeyPref(preKeyId)] = encode(record.serialize())
    }

    override fun containsPreKey(preKeyId: Int): Boolean = readOnly { it.entries.containsKey(prekeyPref(preKeyId)) }

    fun loadPreKeys(): List<PreKeyRecord> = readOnly { state ->
        state.entries.entries.filter { it.key.startsWith(PREFIX_PREKEY) }
            .map { PreKeyRecord(decode(it.value)) }
    }

    override fun removePreKey(preKeyId: Int) = mutate { state -> state.entries.remove(prekeyPref(preKeyId)) }

    // --- SignedPreKeyStore ---------------------------------------------------

    override fun loadSignedPreKey(signedPreKeyId: Int): SignedPreKeyRecord = readOnly { state ->
        val encoded = state.entries[signedPrekeyPref(signedPreKeyId)]
            ?: throw InvalidKeyIdException("No SignedPreKey with id $signedPreKeyId")
        SignedPreKeyRecord(decode(encoded))
    }

    override fun loadSignedPreKeys(): List<SignedPreKeyRecord> = readOnly { state ->
        state.entries.entries.filter { it.key.startsWith(PREFIX_SIGNED_PREKEY) }
            .map { SignedPreKeyRecord(decode(it.value)) }
    }

    override fun storeSignedPreKey(signedPreKeyId: Int, record: SignedPreKeyRecord) = mutate { state ->
        state.entries[signedPrekeyPref(signedPreKeyId)] = encode(record.serialize())
    }

    override fun containsSignedPreKey(signedPreKeyId: Int): Boolean = readOnly { it.entries.containsKey(signedPrekeyPref(signedPreKeyId)) }

    override fun removeSignedPreKey(signedPreKeyId: Int) = Unit // delayed bundle delivery has no bounded retention period

    // --- SessionStore --------------------------------------------------------

    override fun loadSession(address: SignalProtocolAddress): SessionRecord = readOnly { state ->
        state.entries[sessionPref(address)]?.let { SessionRecord(decode(it)) } ?: SessionRecord()
    }

    override fun loadExistingSessions(addresses: List<SignalProtocolAddress>): List<SessionRecord> = readOnly { state ->
        addresses.map { address ->
            val encoded = state.entries[sessionPref(address)] ?: throw NoSessionException("No session for $address")
            SessionRecord(decode(encoded))
        }
    }

    override fun getSubDeviceSessions(name: String): List<Int> = readOnly { state ->
        val prefix = "$PREFIX_SESSION$name::"
        state.entries.keys.filter { it.startsWith(prefix) }.mapNotNull { it.removePrefix(prefix).toIntOrNull() }
    }

    override fun storeSession(address: SignalProtocolAddress, record: SessionRecord) = mutate { state ->
        state.entries[sessionPref(address)] = encode(record.serialize())
    }

    override fun containsSession(address: SignalProtocolAddress): Boolean = readOnly { it.entries.containsKey(sessionPref(address)) }

    override fun deleteSession(address: SignalProtocolAddress) = mutate { state -> state.entries.remove(sessionPref(address)) }

    override fun deleteAllSessions(name: String) = mutate { state ->
        val prefix = "$PREFIX_SESSION$name::"
        state.entries.keys.filter { it.startsWith(prefix) }.forEach(state.entries::remove)
    }

    // --- KyberPreKeyStore ----------------------------------------------------

    override fun loadKyberPreKey(kyberPreKeyId: Int): KyberPreKeyRecord = readOnly { state ->
        val encoded = state.entries[kyberPrekeyPref(kyberPreKeyId)]
            ?: throw InvalidKeyIdException("No KyberPreKey with id $kyberPreKeyId")
        KyberPreKeyRecord(decode(encoded))
    }

    override fun loadKyberPreKeys(): List<KyberPreKeyRecord> = readOnly { state ->
        state.entries.entries.filter { it.key.startsWith(PREFIX_KYBER_PREKEY) }
            .map { KyberPreKeyRecord(decode(it.value)) }
    }

    override fun storeKyberPreKey(kyberPreKeyId: Int, record: KyberPreKeyRecord) = mutate { state ->
        state.entries[kyberPrekeyPref(kyberPreKeyId)] = encode(record.serialize())
    }

    override fun containsKyberPreKey(kyberPreKeyId: Int): Boolean = readOnly { it.entries.containsKey(kyberPrekeyPref(kyberPreKeyId)) }

    override fun markKyberPreKeyUsed(
        kyberPreKeyId: Int,
        signedPreKeyId: Int,
        baseKey: ECPublicKey,
    ) = mutate { state ->
        val key = kyberPrekeyPref(kyberPreKeyId)
        if (!state.entries.containsKey(key)) throw InvalidKeyIdException("No KyberPreKey with id $kyberPreKeyId")
        if (state.entries.containsKey("$PREFIX_LAST_RESORT$kyberPreKeyId")) {
            val replay = "$REPLAY_PREFIX${kyberPreKeyId}::$signedPreKeyId::${encode(baseKey.serialize())}"
            if (state.entries.containsKey(replay)) throw ReusedBaseKeyException("Kyber last-resort base key was already used")
            state.entries[replay] = "1"
        } else {
            state.entries.remove(key)
        }
    }

    // --- SenderKeyStore (group chats are not supported) ---------------------

    override fun storeSenderKey(sender: SignalProtocolAddress, distributionId: UUID, record: SenderKeyRecord) = mutate { state ->
        state.entries[senderKeyPref(sender, distributionId)] = encode(record.serialize())
    }

    override fun loadSenderKey(sender: SignalProtocolAddress, distributionId: UUID): SenderKeyRecord? = readOnly { state ->
        state.entries[senderKeyPref(sender, distributionId)]?.let { SenderKeyRecord(decode(it)) }
    }

    // --- scoped snapshot persistence ----------------------------------------

    private fun currentScope(): CryptoStoreScope {
        val scope = scopeProvider()
            ?: throw IllegalStateException("Crypto state requires an authenticated server, user, and device scope")
        if (invalidatedScope != null && invalidatedScope != scope) invalidatedScope = null
        check(invalidatedScope != scope) { "Crypto state was invalidated during logout" }
        check(!hasPendingCleanup(scope)) { "Crypto state is awaiting logout cleanup" }
        check(scope !in FAILED_SCOPES) { "Crypto state is unavailable after a durable commit failure" }
        return scope
    }

    private fun <T> readOnly(block: (State) -> T): T {
        val staged = transactionState.get()
        if (staged != null) return block(staged)
        return lock.withLock {
            val scope = currentScope()
            block(readState(scope))
        }
    }

    private fun mutate(block: (State) -> Unit) {
        mutateResult { state -> block(state); Unit }
    }

    private fun <T> mutateResult(block: (State) -> T): T {
        val staged = transactionState.get()
        if (staged != null) return block(staged)
        return lock.withLock {
            val scope = currentScope()
            val state = readState(scope)
            val result = block(state)
            writeState(scope, state)
            result
        }
    }

    private fun readState(scope: CryptoStoreScope): State {
        val encoded = preferences(scope).getString(KEY_STATE_BLOB, null) ?: return State(scope.id, mutableMapOf())
        val state = decodeState(encoded)
        check(state.scopeId == scope.id) { "Encrypted crypto store scope mismatch" }
        return state
    }

    private fun writeState(scope: CryptoStoreScope, state: State) {
        check(state.scopeId == scope.id) { "Refusing to persist crypto state into a different account scope" }
        val committed = try {
            preferences(scope).edit().clear().putString(KEY_STATE_BLOB, encodeState(state)).commit()
        } catch (e: Exception) {
            FAILED_SCOPES += scope
            throw DurableCryptoCommitException(e)
        }
        if (!committed) {
            FAILED_SCOPES += scope
            throw DurableCryptoCommitException()
        }
    }

    private fun preferences(scope: CryptoStoreScope): SharedPreferences {
        val name = "${SCOPED_STORE_FILE_PREFIX}${sha256(scope.id)}"
        return preferenceFiles.getOrPut(name) {
            EncryptedSharedPreferences.create(
                name,
                masterKeyAlias,
                appContext,
                EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
            )
        }
    }

    private fun legacyEncryptedPreferences(name: String): SharedPreferences = EncryptedSharedPreferences.create(
        name,
        masterKeyAlias,
        appContext,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
    )

    private fun encodeState(state: State): String {
        val bytes = ByteArrayOutputStream()
        DataOutputStream(bytes).use { output ->
            output.writeInt(STORE_VERSION)
            output.writeUtf8(state.scopeId)
            output.writeInt(state.entries.size)
            state.entries.toSortedMap().forEach { (key, value) ->
                output.writeUtf8(key)
                output.writeUtf8(value)
            }
        }
        return Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP)
    }

    private fun decodeState(encoded: String): State {
        DataInputStream(ByteArrayInputStream(Base64.decode(encoded, Base64.NO_WRAP))).use { input ->
            check(input.readInt() == STORE_VERSION) { "Unsupported encrypted crypto store version" }
            val scopeId = input.readUtf8()
            val count = input.readInt()
            check(count in 0..MAX_STATE_ENTRIES) { "Invalid encrypted crypto store entry count" }
            val entries = mutableMapOf<String, String>()
            repeat(count) { entries[input.readUtf8()] = input.readUtf8() }
            check(input.available() == 0) { "Trailing bytes in encrypted crypto store" }
            return State(scopeId, entries)
        }
    }

    private fun DataOutputStream.writeUtf8(value: String) {
        val bytes = value.toByteArray(StandardCharsets.UTF_8)
        writeInt(bytes.size)
        write(bytes)
    }

    private fun DataInputStream.readUtf8(): String {
        val size = readInt()
        check(size in 0..MAX_STATE_STRING_BYTES) { "Invalid encrypted crypto store string size" }
        val bytes = ByteArray(size)
        readFully(bytes)
        return String(bytes, StandardCharsets.UTF_8)
    }

    private fun encode(bytes: ByteArray): String = Base64.encodeToString(bytes, Base64.NO_WRAP)
    private fun decode(value: String): ByteArray = Base64.decode(value, Base64.NO_WRAP)
    private fun identityKeyPref(address: SignalProtocolAddress): String = "$PREFIX_IDENTITY${address.name}::${address.deviceId}"
    private fun prekeyPref(id: Int): String = "$PREFIX_PREKEY$id"
    private fun signedPrekeyPref(id: Int): String = "$PREFIX_SIGNED_PREKEY$id"
    private fun kyberPrekeyPref(id: Int): String = "$PREFIX_KYBER_PREKEY$id"
    private fun sessionPref(address: SignalProtocolAddress): String = "$PREFIX_SESSION${address.name}::${address.deviceId}"
    private fun senderKeyPref(sender: SignalProtocolAddress, distributionId: UUID): String = "$PREFIX_SENDER_KEY${sender.name}::${sender.deviceId}::$distributionId"
    private fun metadataKey(key: String): String = "$METADATA_PREFIX$key"
    private fun managerKey(key: String): String = "$PREFIX_MANAGER$key"
    private fun secretChatKey(key: String): String = "$METADATA_PREFIX$SECRET_CHAT_NAMESPACE$key"

    private fun isInitialized(state: State): Boolean = state.entries.containsKey(KEY_IDENTITY_KEY) && state.entries.containsKey(KEY_REGISTRATION_ID)

    private data class State(val scopeId: String, val entries: MutableMap<String, String>)

    companion object {
        private val PROCESS_LOCK = ReentrantLock()
        private val FAILED_SCOPES = mutableSetOf<CryptoStoreScope>()
        private val SCOPE_GENERATIONS = mutableMapOf<CryptoStoreScope, Long>()
        private const val STORE_VERSION = 1
        private const val MAX_STATE_ENTRIES = 1_000_000
        private const val MAX_STATE_STRING_BYTES = 64 * 1024 * 1024
        private const val KEY_STATE_BLOB = "crypto_state"
        private const val KEY_IDENTITY_KEY = "self::identity_key"
        private const val KEY_REGISTRATION_ID = "self::registration_id"
        private const val PREFIX_IDENTITY = "identity::"
        private const val PREFIX_PREKEY = "prekey::"
        private const val PREFIX_SIGNED_PREKEY = "signed_prekey::"
        private const val PREFIX_KYBER_PREKEY = "kyber_prekey::"
        private const val PREFIX_SESSION = "session::"
        private const val PREFIX_SENDER_KEY = "sender_key::"
        private const val PREFIX_LAST_RESORT = "last_resort_kyber::"
        private const val REPLAY_PREFIX = "replay::"
        private const val METADATA_PREFIX = "metadata::"
        private const val SECRET_CHAT_NAMESPACE = "secret_chat::"
        private const val PREFIX_MANAGER = "manager::"
        private const val KEY_LEGACY_MIGRATED = "legacy_migrated"
        private const val KEY_LEGACY_CLEANUP = "legacy_unscoped_files"
        private const val LEGACY_SIGNAL_FILE = "barkfluff_signal_store"
        private const val LEGACY_PREKEY_STATE_FILE = "barkfluff_prekey_state"
        private const val LEGACY_SECRET_CHATS_FILE = "barkfluff_secret_chats"
        private const val SCOPED_STORE_FILE_PREFIX = "barkfluff_crypto_"

        private fun sha256(value: String): String = MessageDigest.getInstance("SHA-256")
            .digest(value.toByteArray(StandardCharsets.UTF_8))
            .joinToString("") { "%02x".format(it) }
    }
}
