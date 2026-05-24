package com.barkfluff.client.repository

import android.content.Context
import android.content.SharedPreferences
import android.util.Base64
import android.util.Log
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import barkfluff.shared.Shared
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.crypto.BarkFluffSignalStore
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.signal.libsignal.protocol.IdentityKey
import org.signal.libsignal.protocol.SessionBuilder
import org.signal.libsignal.protocol.SessionCipher
import org.signal.libsignal.protocol.SignalProtocolAddress
import org.signal.libsignal.protocol.ecc.ECPublicKey
import org.signal.libsignal.protocol.message.CiphertextMessage
import org.signal.libsignal.protocol.message.PreKeySignalMessage
import org.signal.libsignal.protocol.message.SignalMessage
import org.signal.libsignal.protocol.state.PreKeyBundle
import java.util.UUID

/**
 * Репозиторий секретных чатов: использует libsignal Double Ratchet для E2E.
 * Сервер хранит только envelope как opaque blob в Redis (24ч TTL); сессии и метаданные
 * чатов хранятся ЛОКАЛЬНО на устройстве-инициаторе.
 *
 * SignalProtocolAddress: name = peerDeviceId (GUID), deviceId = 1 (peerDeviceId уже уникален).
 *
 * Локальное хранение метаданных чата в EncryptedSharedPreferences:
 * `secretChatId` (UUID, генерируется клиентом) → `peerUserId|peerDeviceId|inviteId|inviteSent|accepted`.
 */
class SecretChatRepository(
    context: Context,
    private val grpc: GrpcManager,
    private val signalStore: BarkFluffSignalStore
) {
    private val tag = "SecretChatRepo"

    private val metaPrefs: SharedPreferences = run {
        val masterKeyAlias = MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC)
        EncryptedSharedPreferences.create(
            "barkfluff_secret_chats",
            masterKeyAlias,
            context,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
        )
    }

    /**
     * Создать секретный чат с устройством [peerDeviceId] пользователя [peerUserId]:
     *  1. Запросить prekey-bundle с сервера
     *  2. Установить libsignal-сессию (X3DH)
     *  3. Зашифровать первое сообщение [initialPlaintext] (получится PreKeySignalMessage)
     *  4. Отправить через SendSecretChatInvite
     *  5. Сохранить метаданные локально
     */
    suspend fun createSecretChat(
        peerUserId: Long,
        peerDeviceId: String,
        initialPlaintext: String
    ): Result<SecretChat> = withContext(Dispatchers.IO) {
        try {
            val fetched = grpc.fetchPrekeyBundle(peerUserId, peerDeviceId).getOrElse {
                return@withContext Result.failure(it)
            }
            val address = addressFor(peerDeviceId)
            val bundle = fetched.bundle.toLibsignal()
            SessionBuilder(signalStore, address).process(bundle)

            val cipher = SessionCipher(signalStore, address)
            val encrypted: CiphertextMessage = cipher.encrypt(initialPlaintext.toByteArray(Charsets.UTF_8))
            require(encrypted.type == CiphertextMessage.PREKEY_TYPE) {
                "Первый секретный envelope должен быть PreKeySignalMessage, получен type=${encrypted.type}"
            }

            val sent = grpc.sendSecretChatInvite(peerUserId, peerDeviceId, encrypted.serialize()).getOrElse {
                return@withContext Result.failure(it)
            }

            val secretChatId = UUID.randomUUID().toString()
            val chat = SecretChat(
                id = secretChatId,
                peerUserId = peerUserId,
                peerDeviceId = peerDeviceId,
                inviteId = sent.inviteId,
                role = SecretChatRole.INITIATOR,
                accepted = false
            )
            saveMeta(chat)
            Result.success(chat)
        } catch (e: Exception) {
            Log.e(tag, "createSecretChat failed", e)
            Result.failure(e)
        }
    }

    /**
     * Принять входящий инвайт: расшифровать PreKeySignalMessage → запомнить чат → подтвердить серверу.
     * Возвращает (SecretChat, расшифрованное приветственное сообщение).
     */
    suspend fun acceptIncomingInvite(
        inviteId: String,
        senderUserId: Long,
        senderDeviceId: String,
        initialEnvelope: ByteArray
    ): Result<Pair<SecretChat, String>> = withContext(Dispatchers.IO) {
        try {
            val address = addressFor(senderDeviceId)
            val cipher = SessionCipher(signalStore, address)
            val plaintextBytes = cipher.decrypt(PreKeySignalMessage(initialEnvelope))
            val plaintext = String(plaintextBytes, Charsets.UTF_8)

            val secretChatId = UUID.randomUUID().toString()
            val chat = SecretChat(
                id = secretChatId,
                peerUserId = senderUserId,
                peerDeviceId = senderDeviceId,
                inviteId = inviteId,
                role = SecretChatRole.RECIPIENT,
                accepted = true
            )
            saveMeta(chat)

            grpc.acceptSecretChatInvite(inviteId).onFailure {
                Log.w(tag, "AcceptSecretChatInvite gRPC failed (chat saved locally): ${it.message}")
            }
            Result.success(chat to plaintext)
        } catch (e: Exception) {
            Log.e(tag, "acceptIncomingInvite failed", e)
            Result.failure(e)
        }
    }

    suspend fun rejectIncomingInvite(inviteId: String): Result<Unit> = grpc.rejectSecretChatInvite(inviteId)

    /** Помечает локально, что инвайт принят собеседником. Вызывается из подписки на резолюции. */
    fun markInitiatorChatAccepted(inviteId: String) {
        val chat = findByInviteId(inviteId) ?: return
        if (chat.accepted) return
        saveMeta(chat.copy(accepted = true))
    }

    /** Удалить чат локально (например, после отказа собеседника). */
    fun forgetChat(chatId: String) {
        metaPrefs.edit().remove(chatId).apply()
    }

    suspend fun sendMessage(chat: SecretChat, plaintext: String): Result<GrpcManager.SecretMessageSent> =
        withContext(Dispatchers.IO) {
            try {
                val address = addressFor(chat.peerDeviceId)
                val cipher = SessionCipher(signalStore, address)
                val encrypted = cipher.encrypt(plaintext.toByteArray(Charsets.UTF_8))
                grpc.sendSecretMessage(chat.peerUserId, chat.peerDeviceId, encrypted.serialize())
            } catch (e: Exception) {
                Log.e(tag, "sendMessage failed for ${chat.id}", e)
                Result.failure(e)
            }
        }

    /**
     * Расшифровать входящий envelope. Если он PreKeySignalMessage и сессии ещё нет — установит её
     * (используется когда мы получаем первый ответ собеседника после своего инвайта).
     * Не делает Ack — это обязанность вызывающего кода после успешного UI-применения.
     */
    suspend fun decryptIncoming(envelope: Shared.SecretEnvelope): Result<DecryptedSecret> = withContext(Dispatchers.IO) {
        try {
            val address = addressFor(envelope.senderDeviceId)
            val cipher = SessionCipher(signalStore, address)
            val raw = envelope.envelope.toByteArray()
            val plaintext = try {
                cipher.decrypt(SignalMessage(raw))
            } catch (e: Exception) {
                cipher.decrypt(PreKeySignalMessage(raw))
            }
            val chat = findByPeer(envelope.senderUserId, envelope.senderDeviceId)
            Result.success(
                DecryptedSecret(
                    messageId = envelope.messageId,
                    chat = chat,
                    senderUserId = envelope.senderUserId,
                    senderDeviceId = envelope.senderDeviceId,
                    plaintext = String(plaintext, Charsets.UTF_8),
                    sentAtSeconds = envelope.sentAt.seconds
                )
            )
        } catch (e: Exception) {
            Log.e(tag, "decryptIncoming failed for ${envelope.messageId}", e)
            Result.failure(e)
        }
    }

    suspend fun ack(messageId: String): Result<Unit> = grpc.ackSecretMessage(messageId)

    fun listChats(): List<SecretChat> = metaPrefs.all.entries.mapNotNull {
        val value = it.value as? String ?: return@mapNotNull null
        decodeMeta(it.key, value)
    }

    fun getChat(chatId: String): SecretChat? {
        val value = metaPrefs.getString(chatId, null) ?: return null
        return decodeMeta(chatId, value)
    }

    fun findByPeer(peerUserId: Long, peerDeviceId: String): SecretChat? =
        listChats().firstOrNull { it.peerUserId == peerUserId && it.peerDeviceId == peerDeviceId }

    fun findByInviteId(inviteId: String): SecretChat? =
        listChats().firstOrNull { it.inviteId == inviteId }

    fun forgetAll() {
        metaPrefs.edit().clear().apply()
    }

    // --- helpers -------------------------------------------------------------

    private fun addressFor(peerDeviceId: String): SignalProtocolAddress = SignalProtocolAddress(peerDeviceId, 1)

    private fun saveMeta(chat: SecretChat) {
        val encoded = listOf(
            chat.peerUserId.toString(),
            chat.peerDeviceId,
            chat.inviteId,
            chat.role.name,
            if (chat.accepted) "1" else "0"
        ).joinToString("|")
        metaPrefs.edit().putString(chat.id, encoded).apply()
    }

    private fun decodeMeta(chatId: String, encoded: String): SecretChat? {
        val parts = encoded.split("|")
        if (parts.size < 5) return null
        return try {
            SecretChat(
                id = chatId,
                peerUserId = parts[0].toLong(),
                peerDeviceId = parts[1],
                inviteId = parts[2],
                role = SecretChatRole.valueOf(parts[3]),
                accepted = parts[4] == "1"
            )
        } catch (e: Exception) {
            Log.w(tag, "Failed to decode secret chat meta $chatId: ${e.message}")
            null
        }
    }

    /**
     * TODO: libsignal-android 0.86+ требует Kyber prekey в [PreKeyBundle] (PQXDH).
     * Backend proto в [UsersApiOuterClass.PrekeyBundle] пока не содержит kyber полей —
     * необходимо расширить proto (добавить kyber_prekey_id / kyber_pubkey / kyber_signature)
     * и backend/Users + Android prekey-генерацию (KEMKeyPair).
     *
     * До этого момента секретные чаты не могут быть созданы — функция бросает
     * [UnsupportedOperationException]. Приватные (passphrase) чаты работают независимо.
     */
    private fun UsersApiOuterClass.PrekeyBundle.toLibsignal(): PreKeyBundle {
        throw UnsupportedOperationException(
            "Секретные чаты пока не поддерживаются: libsignal требует Kyber prekey, " +
                "которого нет в proto. Нужно расширить UsersApi prekey-bundle Kyber-полями."
        )
    }

    enum class SecretChatRole { INITIATOR, RECIPIENT }

    data class SecretChat(
        val id: String,
        val peerUserId: Long,
        val peerDeviceId: String,
        val inviteId: String,
        val role: SecretChatRole,
        val accepted: Boolean
    )

    data class DecryptedSecret(
        val messageId: String,
        val chat: SecretChat?,
        val senderUserId: Long,
        val senderDeviceId: String,
        val plaintext: String,
        val sentAtSeconds: Long
    )
}
