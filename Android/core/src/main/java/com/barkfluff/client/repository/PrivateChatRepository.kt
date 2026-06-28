package com.barkfluff.client.repository

import android.content.Context
import android.content.SharedPreferences
import android.util.Base64
import android.util.Log
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import barkfluff.messages.MessagesApiOuterClass
import barkfluff.shared.Shared
import com.barkfluff.client.crypto.PrivateChatCrypto
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Репозиторий приватных чатов: связывает [GrpcManager] и [PrivateChatCrypto].
 *
 * Ключ AES-256 каждого чата выводится из passphrase + Chat.kdf_salt через Argon2id;
 * хранится локально в EncryptedSharedPreferences (один раз после ввода passphrase).
 * Сервер ключ не видит — только шифротекст.
 */
class PrivateChatRepository(
    context: Context,
    private val grpc: GrpcManager
) {
    private val tag = "PrivateChatRepo"
    private val keyPrefs: SharedPreferences = run {
        val masterKeyAlias = MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC)
        EncryptedSharedPreferences.create(
            "barkfluff_private_chat_keys",
            masterKeyAlias,
            context,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }
    private val keyCache = HashMap<String, ByteArray>()

    /** Есть ли сохранённый ключ для этого приватного чата (passphrase введён ранее). */
    fun hasKey(chatId: String): Boolean = keyCache.containsKey(chatId) || keyPrefs.contains(chatId)

    /** Забыть сохранённый ключ (например, при logout или Forget). */
    fun forgetKey(chatId: String) {
        keyCache.remove(chatId)
        keyPrefs.edit().remove(chatId).apply()
    }

    fun forgetAll() {
        keyCache.clear()
        keyPrefs.edit().clear().apply()
    }

    /**
     * Создать приватный чат: сгенерировать salt, вывести ключ из [passphrase],
     * посчитать verifier и отправить на сервер. Ключ остаётся локально.
     */
    suspend fun createPrivateChat(peerUserId: Long, passphrase: String): Result<MessagesApiOuterClass.Chat> =
        withContext(Dispatchers.Default) {
            try {
                val salt = PrivateChatCrypto.generateSalt()
                val key = PrivateChatCrypto.deriveKey(passphrase, salt)
                val verifier = PrivateChatCrypto.computeVerifier(key)
                val result = grpc.createPrivateChat(peerUserId, salt, verifier)
                result.onSuccess { chat -> rememberKey(chat.id, key) }
                result
            } catch (e: Exception) {
                Log.e(tag, "createPrivateChat failed", e)
                Result.failure(e)
            }
        }

    /**
     * Проверить passphrase против verifier из инвайта. Если совпало — присоединиться к чату
     * через gRPC и запомнить ключ.
     */
    suspend fun acceptPrivateChatInvite(
        chatId: String,
        passphrase: String,
        kdfSalt: ByteArray,
        passphraseVerifier: ByteArray
    ): Result<MessagesApiOuterClass.Chat> = withContext(Dispatchers.Default) {
        try {
            val key = PrivateChatCrypto.deriveKey(passphrase, kdfSalt)
            if (!PrivateChatCrypto.validateVerifier(key, passphraseVerifier)) {
                return@withContext Result.failure(InvalidPassphraseException(chatId))
            }
            val result = grpc.acceptPrivateChat(chatId)
            result.onSuccess { chat -> rememberKey(chat.id, key) }
            result
        } catch (e: InvalidPassphraseException) {
            Result.failure(e)
        } catch (e: Exception) {
            Log.e(tag, "acceptPrivateChatInvite failed for $chatId", e)
            Result.failure(e)
        }
    }

    /**
     * Регистрация пользовательского passphrase для уже существующего чата (например, после
     * перезахода в приложение). Используется когда у клиента есть Chat (с salt+verifier) от сервера,
     * но локально ключ не сохранён.
     */
    fun unlockExistingChat(chat: MessagesApiOuterClass.Chat, passphrase: String): Boolean {
        val key = PrivateChatCrypto.deriveKey(passphrase, chat.kdfSalt.toByteArray())
        if (!PrivateChatCrypto.validateVerifier(key, chat.passphraseVerifier.toByteArray())) return false
        rememberKey(chat.id, key)
        return true
    }

    suspend fun rejectPrivateChat(chatId: String): Result<Unit> = grpc.rejectPrivateChat(chatId)

    /** Зашифровать text и отправить через gRPC. Возвращает сохранённый EncryptedMessage от сервера. */
    suspend fun sendText(chatId: String, plaintext: String): Result<DecryptedPrivateMessage> =
        withContext(Dispatchers.Default) {
            val key = loadKey(chatId) ?: return@withContext Result.failure(KeyNotAvailableException(chatId))
            try {
                val aad = PrivateChatCrypto.privateChatAad(chatId)
                val (ciphertext, nonce) = PrivateChatCrypto.encrypt(plaintext.toByteArray(Charsets.UTF_8), key, aad)
                val sent = grpc.sendPrivateMessage(chatId, ciphertext, nonce, aad).getOrElse {
                    return@withContext Result.failure(it)
                }
                Result.success(decryptMessage(chatId, sent, key) ?: DecryptedPrivateMessage(sent, plaintext))
            } catch (e: Exception) {
                Log.e(tag, "sendText failed for $chatId", e)
                Result.failure(e)
            }
        }

    suspend fun editText(chatId: String, messageId: Long, plaintext: String): Result<DecryptedPrivateMessage> =
        withContext(Dispatchers.Default) {
            val key = loadKey(chatId) ?: return@withContext Result.failure(KeyNotAvailableException(chatId))
            try {
                val aad = PrivateChatCrypto.privateChatAad(chatId)
                val (ciphertext, nonce) = PrivateChatCrypto.encrypt(plaintext.toByteArray(Charsets.UTF_8), key, aad)
                val edited = grpc.editPrivateMessage(messageId, ciphertext, nonce, aad).getOrElse {
                    return@withContext Result.failure(it)
                }
                Result.success(decryptMessage(chatId, edited, key) ?: DecryptedPrivateMessage(edited, plaintext))
            } catch (e: Exception) {
                Log.e(tag, "editText failed for $chatId/$messageId", e)
                Result.failure(e)
            }
        }

    suspend fun deleteMessage(chatId: String, messageId: Long): Result<Unit> = grpc.deletePrivateMessage(messageId)

    /** Получить страницу шифротекстов и расшифровать сразу. */
    suspend fun listMessages(
        chatId: String,
        fromMessageId: Long = 0,
        offsetBefore: Int = 50,
        offsetAfter: Int = 0
    ): Result<List<DecryptedPrivateMessage>> = withContext(Dispatchers.Default) {
        val key = loadKey(chatId) ?: return@withContext Result.failure(KeyNotAvailableException(chatId))
        val raw = grpc.listPrivateMessages(chatId, fromMessageId, offsetBefore, offsetAfter).getOrElse {
            return@withContext Result.failure(it)
        }
        val decrypted = raw.mapNotNull { decryptMessage(chatId, it, key) }
        Result.success(decrypted)
    }

    /** Расшифровать одно входящее сообщение (например из realtime-стрима). */
    fun decryptIncoming(chatId: String, message: Shared.EncryptedMessage): DecryptedPrivateMessage? {
        val key = loadKey(chatId) ?: return null
        return decryptMessage(chatId, message, key)
    }

    private fun decryptMessage(
        chatId: String,
        message: Shared.EncryptedMessage,
        key: ByteArray
    ): DecryptedPrivateMessage? {
        if (message.isDeleted) return DecryptedPrivateMessage(message, plaintext = null)
        return try {
            val aad = if (message.associatedData.isEmpty) PrivateChatCrypto.privateChatAad(chatId) else message.associatedData.toByteArray()
            val plaintextBytes = PrivateChatCrypto.decrypt(
                ciphertext = message.ciphertext.toByteArray(),
                nonce = message.nonce.toByteArray(),
                key = key,
                aad = aad
            )
            DecryptedPrivateMessage(message, String(plaintextBytes, Charsets.UTF_8))
        } catch (e: Exception) {
            Log.w(tag, "Failed to decrypt private message ${message.id} in $chatId: ${e.javaClass.simpleName}")
            DecryptedPrivateMessage(message, plaintext = null)
        }
    }

    private fun rememberKey(chatId: String, key: ByteArray) {
        keyCache[chatId] = key
        keyPrefs.edit().putString(chatId, Base64.encodeToString(key, Base64.NO_WRAP)).apply()
    }

    private fun loadKey(chatId: String): ByteArray? {
        keyCache[chatId]?.let { return it }
        val encoded = keyPrefs.getString(chatId, null) ?: return null
        val key = Base64.decode(encoded, Base64.NO_WRAP)
        keyCache[chatId] = key
        return key
    }

    data class DecryptedPrivateMessage(
        val raw: Shared.EncryptedMessage,
        /** Расшифрованный текст или null, если расшифровка не удалась / сообщение удалено. */
        val plaintext: String?
    ) {
        val isDecrypted: Boolean get() = plaintext != null
    }

    class InvalidPassphraseException(chatId: String) : Exception("Неверный passphrase для приватного чата $chatId")
    class KeyNotAvailableException(chatId: String) : Exception("Приватный чат $chatId заблокирован — введите passphrase")
}
