package com.barkfluff.client.crypto

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import org.signal.libsignal.protocol.IdentityKey
import org.signal.libsignal.protocol.IdentityKeyPair
import org.signal.libsignal.protocol.InvalidKeyIdException
import org.signal.libsignal.protocol.NoSessionException
import org.signal.libsignal.protocol.SignalProtocolAddress
import org.signal.libsignal.protocol.groups.state.SenderKeyRecord
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
import org.signal.libsignal.protocol.groups.state.SenderKeyStore
import android.util.Base64
import java.util.UUID

/**
 * Реализация libsignal SignalProtocolStore поверх EncryptedSharedPreferences.
 * Хранит identity-key, registration_id, prekeys, signed prekeys, sessions per device address.
 *
 * Записи libsignal сериализуются через .serialize() → ByteArray → Base64 string.
 *
 * Один экземпляр на пользователя/устройство; пересоздаётся при разлогине.
 */
class BarkFluffSignalStore(context: Context) : SignalProtocolStore {

    private val prefs: SharedPreferences = run {
        val masterKeyAlias = MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC)
        EncryptedSharedPreferences.create(
            "barkfluff_signal_store",
            masterKeyAlias,
            context,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    fun isInitialized(): Boolean = prefs.contains(KEY_IDENTITY_KEY) && prefs.contains(KEY_REGISTRATION_ID)

    fun initialize(identityKeyPair: IdentityKeyPair, registrationId: Int) {
        prefs.edit()
            .putString(KEY_IDENTITY_KEY, encode(identityKeyPair.serialize()))
            .putInt(KEY_REGISTRATION_ID, registrationId)
            .apply()
    }

    fun clear() {
        prefs.edit().clear().apply()
    }

    /** Удалить все записи pre-key (например, после массовой ротации). Сессии не трогает. */
    fun clearOneTimePreKeys() {
        prefs.edit().apply {
            for (key in prefs.all.keys.toList()) {
                if (key.startsWith(PREFIX_PREKEY)) remove(key)
            }
        }.apply()
    }

    // --- IdentityKeyStore ----------------------------------------------------

    override fun getIdentityKeyPair(): IdentityKeyPair {
        val encoded = prefs.getString(KEY_IDENTITY_KEY, null)
            ?: throw IllegalStateException("Identity key not initialized — call initialize() first")
        return IdentityKeyPair(decode(encoded))
    }

    override fun getLocalRegistrationId(): Int {
        val id = prefs.getInt(KEY_REGISTRATION_ID, -1)
        check(id != -1) { "Registration id not initialized" }
        return id
    }

    override fun saveIdentity(address: SignalProtocolAddress, identityKey: IdentityKey): IdentityKeyStore.IdentityChange {
        val key = identityKeyPref(address)
        val previousEncoded = prefs.getString(key, null)
        prefs.edit().putString(key, encode(identityKey.serialize())).apply()
        if (previousEncoded == null) return IdentityKeyStore.IdentityChange.NEW_OR_UNCHANGED
        val previous = IdentityKey(decode(previousEncoded), 0)
        return if (previous == identityKey) IdentityKeyStore.IdentityChange.NEW_OR_UNCHANGED
        else IdentityKeyStore.IdentityChange.REPLACED_EXISTING
    }

    override fun isTrustedIdentity(
        address: SignalProtocolAddress,
        identityKey: IdentityKey,
        direction: IdentityKeyStore.Direction
    ): Boolean {
        val stored = prefs.getString(identityKeyPref(address), null) ?: return true
        return IdentityKey(decode(stored), 0) == identityKey
    }

    override fun getIdentity(address: SignalProtocolAddress): IdentityKey? {
        val stored = prefs.getString(identityKeyPref(address), null) ?: return null
        return IdentityKey(decode(stored), 0)
    }

    // --- PreKeyStore ---------------------------------------------------------

    override fun loadPreKey(preKeyId: Int): PreKeyRecord {
        val encoded = prefs.getString(prekeyPref(preKeyId), null)
            ?: throw InvalidKeyIdException("No PreKey with id $preKeyId")
        return PreKeyRecord(decode(encoded))
    }

    override fun storePreKey(preKeyId: Int, record: PreKeyRecord) {
        prefs.edit().putString(prekeyPref(preKeyId), encode(record.serialize())).apply()
    }

    override fun containsPreKey(preKeyId: Int): Boolean = prefs.contains(prekeyPref(preKeyId))

    override fun removePreKey(preKeyId: Int) {
        prefs.edit().remove(prekeyPref(preKeyId)).apply()
    }

    // --- SignedPreKeyStore ---------------------------------------------------

    override fun loadSignedPreKey(signedPreKeyId: Int): SignedPreKeyRecord {
        val encoded = prefs.getString(signedPrekeyPref(signedPreKeyId), null)
            ?: throw InvalidKeyIdException("No SignedPreKey with id $signedPreKeyId")
        return SignedPreKeyRecord(decode(encoded))
    }

    override fun loadSignedPreKeys(): List<SignedPreKeyRecord> =
        prefs.all.entries
            .filter { it.key.startsWith(PREFIX_SIGNED_PREKEY) && it.value is String }
            .map { SignedPreKeyRecord(decode(it.value as String)) }

    override fun storeSignedPreKey(signedPreKeyId: Int, record: SignedPreKeyRecord) {
        prefs.edit().putString(signedPrekeyPref(signedPreKeyId), encode(record.serialize())).apply()
    }

    override fun containsSignedPreKey(signedPreKeyId: Int): Boolean =
        prefs.contains(signedPrekeyPref(signedPreKeyId))

    override fun removeSignedPreKey(signedPreKeyId: Int) {
        prefs.edit().remove(signedPrekeyPref(signedPreKeyId)).apply()
    }

    // --- SessionStore --------------------------------------------------------

    override fun loadSession(address: SignalProtocolAddress): SessionRecord {
        val encoded = prefs.getString(sessionPref(address), null) ?: return SessionRecord()
        return SessionRecord(decode(encoded))
    }

    override fun loadExistingSessions(addresses: List<SignalProtocolAddress>): List<SessionRecord> {
        return addresses.map { addr ->
            val encoded = prefs.getString(sessionPref(addr), null)
                ?: throw NoSessionException("No session for $addr")
            SessionRecord(decode(encoded))
        }
    }

    override fun getSubDeviceSessions(name: String): List<Int> {
        val prefix = "$PREFIX_SESSION$name::"
        return prefs.all.keys.filter { it.startsWith(prefix) }.mapNotNull {
            it.removePrefix(prefix).toIntOrNull()
        }
    }

    override fun storeSession(address: SignalProtocolAddress, record: SessionRecord) {
        prefs.edit().putString(sessionPref(address), encode(record.serialize())).apply()
    }

    override fun containsSession(address: SignalProtocolAddress): Boolean =
        prefs.contains(sessionPref(address))

    override fun deleteSession(address: SignalProtocolAddress) {
        prefs.edit().remove(sessionPref(address)).apply()
    }

    override fun deleteAllSessions(name: String) {
        val prefix = "$PREFIX_SESSION$name::"
        prefs.edit().apply {
            for (key in prefs.all.keys.toList()) if (key.startsWith(prefix)) remove(key)
        }.apply()
    }

    // --- KyberPreKeyStore (не используется; X3DH без PQ) ---------------------

    override fun loadKyberPreKey(kyberPreKeyId: Int): KyberPreKeyRecord {
        val encoded = prefs.getString(kyberPrekeyPref(kyberPreKeyId), null)
            ?: throw InvalidKeyIdException("No KyberPreKey with id $kyberPreKeyId")
        return KyberPreKeyRecord(decode(encoded))
    }

    override fun loadKyberPreKeys(): List<KyberPreKeyRecord> =
        prefs.all.entries
            .filter { it.key.startsWith(PREFIX_KYBER_PREKEY) && it.value is String }
            .map { KyberPreKeyRecord(decode(it.value as String)) }

    override fun storeKyberPreKey(kyberPreKeyId: Int, record: KyberPreKeyRecord) {
        prefs.edit().putString(kyberPrekeyPref(kyberPreKeyId), encode(record.serialize())).apply()
    }

    override fun containsKyberPreKey(kyberPreKeyId: Int): Boolean =
        prefs.contains(kyberPrekeyPref(kyberPreKeyId))

    override fun markKyberPreKeyUsed(
        kyberPreKeyId: Int,
        signedPreKeyId: Int,
        baseKey: org.signal.libsignal.protocol.ecc.ECPublicKey
    ) {
        // Не используем Kyber prekeys — оставляем no-op. Сигнатура соответствует интерфейсу
        // libsignal 0.86+. ReusedBaseKeyException не бросаем (Kyber не задействован).
        prefs.edit().remove(kyberPrekeyPref(kyberPreKeyId)).apply()
    }

    // --- SenderKeyStore (групповые чаты не поддерживаются) -------------------

    override fun storeSenderKey(sender: SignalProtocolAddress, distributionId: UUID, record: SenderKeyRecord) {
        prefs.edit().putString(senderKeyPref(sender, distributionId), encode(record.serialize())).apply()
    }

    override fun loadSenderKey(sender: SignalProtocolAddress, distributionId: UUID): SenderKeyRecord? {
        val encoded = prefs.getString(senderKeyPref(sender, distributionId), null) ?: return null
        return SenderKeyRecord(decode(encoded))
    }

    // --- helpers -------------------------------------------------------------

    private fun encode(bytes: ByteArray): String = Base64.encodeToString(bytes, Base64.NO_WRAP)
    private fun decode(s: String): ByteArray = Base64.decode(s, Base64.NO_WRAP)

    private fun identityKeyPref(address: SignalProtocolAddress): String =
        "$PREFIX_IDENTITY${address.name}::${address.deviceId}"

    private fun prekeyPref(id: Int): String = "$PREFIX_PREKEY$id"
    private fun signedPrekeyPref(id: Int): String = "$PREFIX_SIGNED_PREKEY$id"
    private fun kyberPrekeyPref(id: Int): String = "$PREFIX_KYBER_PREKEY$id"
    private fun sessionPref(address: SignalProtocolAddress): String =
        "$PREFIX_SESSION${address.name}::${address.deviceId}"
    private fun senderKeyPref(sender: SignalProtocolAddress, distributionId: UUID): String =
        "$PREFIX_SENDER_KEY${sender.name}::${sender.deviceId}::$distributionId"

    companion object {
        private const val KEY_IDENTITY_KEY = "self::identity_key"
        private const val KEY_REGISTRATION_ID = "self::registration_id"
        private const val PREFIX_IDENTITY = "identity::"
        private const val PREFIX_PREKEY = "prekey::"
        private const val PREFIX_SIGNED_PREKEY = "signed_prekey::"
        private const val PREFIX_KYBER_PREKEY = "kyber_prekey::"
        private const val PREFIX_SESSION = "session::"
        private const val PREFIX_SENDER_KEY = "sender_key::"
    }
}
