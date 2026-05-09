package com.barkfluff.client.crypto

import com.lambdapioneer.argon2kt.Argon2Kt
import com.lambdapioneer.argon2kt.Argon2Mode
import com.lambdapioneer.argon2kt.Argon2Version
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * E2E для приватных чатов: Argon2id(passphrase, salt) → 32-байтный AES-ключ →
 * AES-256-GCM шифрование/расшифровка. Сервер ключ никогда не видит.
 *
 * Verifier — HMAC-SHA256(key, VERIFIER_CONSTANT). Позволяет клиенту-получателю
 * проверить корректность введённого passphrase ДО присоединения к чату,
 * не раскрывая ключ серверу.
 */
object PrivateChatCrypto {

    const val KEY_BYTES = 32
    const val SALT_BYTES = 32
    const val NONCE_BYTES = 12
    const val GCM_TAG_BITS = 128

    private const val ARGON2_T_COST = 3
    private const val ARGON2_M_COST_KIB = 64 * 1024
    private const val ARGON2_PARALLELISM = 4

    private const val VERIFIER_HMAC_ALGO = "HmacSHA256"
    private val VERIFIER_CONSTANT = "BARKFLUFF_PRIVATE_CHAT_VERIFIER".toByteArray()

    private val argon2 by lazy { Argon2Kt() }
    private val secureRandom = SecureRandom()

    /** Сгенерировать криптостойкий salt для Argon2id. */
    fun generateSalt(): ByteArray = ByteArray(SALT_BYTES).also(secureRandom::nextBytes)

    /** Сгенерировать криптостойкий nonce для AES-GCM. */
    fun generateNonce(): ByteArray = ByteArray(NONCE_BYTES).also(secureRandom::nextBytes)

    /**
     * Вывести 32-байтный AES-ключ из passphrase и salt через Argon2id.
     * Тяжёлая операция (~1с), вызывать вне UI потока.
     */
    fun deriveKey(passphrase: String, salt: ByteArray): ByteArray {
        require(salt.size >= 16) { "Argon2 salt must be ≥16 bytes (got ${salt.size})" }
        val passphraseBytes = passphrase.toByteArray(Charsets.UTF_8)
        return argon2.hash(
            mode = Argon2Mode.ARGON2_ID,
            password = passphraseBytes,
            salt = salt,
            tCostInIterations = ARGON2_T_COST,
            mCostInKibibyte = ARGON2_M_COST_KIB,
            parallelism = ARGON2_PARALLELISM,
            hashLengthInBytes = KEY_BYTES,
            version = Argon2Version.V13
        ).rawHashAsByteArray()
    }

    /** HMAC-SHA256(key, "BARKFLUFF_PRIVATE_CHAT_VERIFIER"). Отправляется на сервер при создании чата. */
    fun computeVerifier(key: ByteArray): ByteArray {
        val mac = Mac.getInstance(VERIFIER_HMAC_ALGO)
        mac.init(SecretKeySpec(key, VERIFIER_HMAC_ALGO))
        return mac.doFinal(VERIFIER_CONSTANT)
    }

    /** Constant-time проверка verifier'а на стороне приглашённого. */
    fun validateVerifier(key: ByteArray, expectedVerifier: ByteArray): Boolean {
        val actual = computeVerifier(key)
        if (actual.size != expectedVerifier.size) return false
        var diff = 0
        for (i in actual.indices) diff = diff or (actual[i].toInt() xor expectedVerifier[i].toInt())
        return diff == 0
    }

    /**
     * AES-256-GCM шифрование. Возвращает (ciphertext+tag, nonce). Caller должен передать AAD,
     * привязывающий шифротекст к контексту (например chatId UTF-8 байтами).
     */
    fun encrypt(plaintext: ByteArray, key: ByteArray, aad: ByteArray): Pair<ByteArray, ByteArray> {
        require(key.size == KEY_BYTES) { "AES-256 key must be $KEY_BYTES bytes (got ${key.size})" }
        val nonce = generateNonce()
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(GCM_TAG_BITS, nonce))
        cipher.updateAAD(aad)
        val ciphertext = cipher.doFinal(plaintext)
        return ciphertext to nonce
    }

    /**
     * AES-256-GCM расшифровка. Бросает javax.crypto.AEADBadTagException при несовпадении tag/AAD/nonce
     * или повреждённом ciphertext.
     */
    fun decrypt(ciphertext: ByteArray, nonce: ByteArray, key: ByteArray, aad: ByteArray): ByteArray {
        require(key.size == KEY_BYTES) { "AES-256 key must be $KEY_BYTES bytes (got ${key.size})" }
        require(nonce.size == NONCE_BYTES) { "AES-GCM nonce must be $NONCE_BYTES bytes (got ${nonce.size})" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(GCM_TAG_BITS, nonce))
        cipher.updateAAD(aad)
        return cipher.doFinal(ciphertext)
    }

    /**
     * Стандартный AAD для приватных сообщений: "barkfluff:private:{chatId}". Гарантирует, что
     * шифротекст одного чата нельзя «переподложить» в другой.
     */
    fun privateChatAad(chatId: String): ByteArray = "barkfluff:private:$chatId".toByteArray(Charsets.UTF_8)
}
