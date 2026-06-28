package com.barkfluff.client.crypto

import android.content.Context
import android.content.SharedPreferences
import org.signal.libsignal.protocol.IdentityKeyPair
import org.signal.libsignal.protocol.ecc.ECKeyPair
import org.signal.libsignal.protocol.state.PreKeyRecord
import org.signal.libsignal.protocol.state.SignedPreKeyRecord
import org.signal.libsignal.protocol.util.KeyHelper

/**
 * Управление prekey-bundle: при первом логине генерирует и публикует на сервер
 * identity-key, registration_id, signed prekey и 100 one-time prekeys.
 *
 * libsignal 0.86+ убрал helper'ы из KeyHelper — генерация делается вручную через
 * `IdentityKeyPair.generate()` и `ECKeyPair.generate()`.
 *
 * Регистрация запускается один раз — флаг хранится в локальных preferences.
 * Сами ключи хранятся в [BarkFluffSignalStore] (EncryptedSharedPreferences).
 */
class PrekeyManager(
    context: Context,
    private val store: BarkFluffSignalStore
) {

    private val prefs: SharedPreferences = context.getSharedPreferences("barkfluff_prekey_state", Context.MODE_PRIVATE)

    val isRegistered: Boolean
        get() = prefs.getBoolean(KEY_REGISTERED, false) && store.isInitialized()

    /**
     * Сгенерировать новый набор: identity + registration_id + signed prekey + N one-time prekeys.
     * Возвращает все артефакты для последующей отправки на сервер; локально не сохраняет до markRegistered().
     */
    fun generateBundle(initialOneTimeCount: Int = INITIAL_ONE_TIME_PREKEYS): GeneratedBundle {
        val identityKeyPair = IdentityKeyPair.generate()
        val registrationId = KeyHelper.generateRegistrationId(false)
        val signedPreKey = generateSignedPreKey(identityKeyPair, INITIAL_SIGNED_PREKEY_ID)
        val oneTimePreKeys = (1..initialOneTimeCount).map { id ->
            PreKeyRecord(id, ECKeyPair.generate())
        }
        return GeneratedBundle(identityKeyPair, registrationId, signedPreKey, oneTimePreKeys)
    }

    /** Сгенерировать дополнительные one-time prekeys (для пополнения пула). */
    fun generateAdditionalOneTimePrekeys(count: Int): List<PreKeyRecord> {
        val nextStartId = prefs.getInt(KEY_NEXT_ONE_TIME_ID, INITIAL_ONE_TIME_PREKEYS + 1)
        val keys = (0 until count).map { offset ->
            PreKeyRecord(nextStartId + offset, ECKeyPair.generate())
        }
        prefs.edit().putInt(KEY_NEXT_ONE_TIME_ID, nextStartId + count).apply()
        keys.forEach { store.storePreKey(it.id, it) }
        return keys
    }

    /** Сгенерировать новый signed prekey (для ротации). */
    fun rotateSignedPrekey(): SignedPreKeyRecord {
        val nextId = prefs.getInt(KEY_NEXT_SIGNED_ID, INITIAL_SIGNED_PREKEY_ID + 1)
        val identity = store.identityKeyPair
        val signed = generateSignedPreKey(identity, nextId)
        store.storeSignedPreKey(signed.id, signed)
        prefs.edit().putInt(KEY_NEXT_SIGNED_ID, nextId + 1).apply()
        return signed
    }

    /**
     * Сохранить сгенерированный bundle в локальное хранилище и пометить устройство как зарегистрированное.
     * Вызывать ПОСЛЕ успешного RegisterPrekeyBundle на сервере.
     */
    fun persistBundle(bundle: GeneratedBundle) {
        store.initialize(bundle.identityKeyPair, bundle.registrationId)
        store.storeSignedPreKey(bundle.signedPreKey.id, bundle.signedPreKey)
        bundle.oneTimePreKeys.forEach { store.storePreKey(it.id, it) }
        prefs.edit()
            .putBoolean(KEY_REGISTERED, true)
            .putInt(KEY_NEXT_ONE_TIME_ID, INITIAL_ONE_TIME_PREKEYS + 1)
            .putInt(KEY_NEXT_SIGNED_ID, INITIAL_SIGNED_PREKEY_ID + 1)
            .apply()
    }

    fun reset() {
        store.clear()
        prefs.edit().clear().apply()
    }

    private fun generateSignedPreKey(identityKeyPair: IdentityKeyPair, signedPreKeyId: Int): SignedPreKeyRecord {
        val keyPair = ECKeyPair.generate()
        val signature = identityKeyPair.privateKey.calculateSignature(keyPair.publicKey.serialize())
        val timestamp = System.currentTimeMillis()
        return SignedPreKeyRecord(signedPreKeyId, timestamp, keyPair, signature)
    }

    data class GeneratedBundle(
        val identityKeyPair: IdentityKeyPair,
        val registrationId: Int,
        val signedPreKey: SignedPreKeyRecord,
        val oneTimePreKeys: List<PreKeyRecord>
    )

    companion object {
        const val INITIAL_ONE_TIME_PREKEYS = 100
        const val INITIAL_SIGNED_PREKEY_ID = 1
        const val MIN_ONE_TIME_PREKEYS_THRESHOLD = 10

        private const val KEY_REGISTERED = "prekey_registered"
        private const val KEY_NEXT_ONE_TIME_ID = "prekey_next_one_time_id"
        private const val KEY_NEXT_SIGNED_ID = "prekey_next_signed_id"
    }
}
