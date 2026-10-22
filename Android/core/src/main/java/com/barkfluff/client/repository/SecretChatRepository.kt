package com.barkfluff.client.repository

import android.util.Base64
import android.util.Log
import barkfluff.shared.Shared
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.crypto.BarkFluffSignalStore
import com.barkfluff.client.crypto.CryptoOperation
import com.barkfluff.client.crypto.CryptoOperationInvalidatedException
import com.barkfluff.client.crypto.DurableCryptoCommitException
import com.barkfluff.client.crypto.InvalidPrekeyStateException
import com.barkfluff.client.crypto.PrekeyServerUpdateRequiredException
import com.barkfluff.client.grpc.GrpcApiTransport
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.signal.libsignal.protocol.IdentityKey
import org.signal.libsignal.protocol.SessionBuilder
import org.signal.libsignal.protocol.SessionCipher
import org.signal.libsignal.protocol.SignalProtocolAddress
import org.signal.libsignal.protocol.ecc.ECPublicKey
import org.signal.libsignal.protocol.kem.KEMPublicKey
import org.signal.libsignal.protocol.message.CiphertextMessage
import org.signal.libsignal.protocol.message.PreKeySignalMessage
import org.signal.libsignal.protocol.message.SignalMessage
import org.signal.libsignal.protocol.state.PreKeyBundle
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

class InvalidPrekeyBundleException(message: String, cause: Throwable? = null) : IllegalArgumentException(message, cause)

/** Persistent PQXDH sessions, invite retries, chat metadata, and replay protection. */
class SecretChatRepository(
    private val grpc: GrpcApiTransport,
    private val signalStore: BarkFluffSignalStore,
) {
    private val tag = "SecretChatRepo"
    private val peerLocks = ConcurrentHashMap<String, Mutex>()

    suspend fun createSecretChat(
        peerUserId: Long,
        peerDeviceId: String,
        initialPlaintext: String,
    ): Result<SecretChat> = withContext(Dispatchers.IO) {
        val operation = signalStore.captureOperation()
        peerLock(peerUserId, peerDeviceId).withLock {
            try {
                signalStore.assertCurrent(operation)
                val existing = findByPeer(peerUserId, peerDeviceId)
                if (existing != null) {
                    if (existing.pendingSend && existing.initialEnvelope != null) {
                        return@withLock sendPendingInvite(existing, operation).fold(
                            onSuccess = { Result.success(it) },
                            onFailure = {
                                Log.w(tag, "Pending invite remains queued: ${it::class.java.simpleName}")
                                Result.success(existing)
                            },
                        )
                    }
                    return@withLock Result.success(existing)
                }

                val fetched = grpc.fetchPrekeyBundle(peerUserId, peerDeviceId).getOrElse {
                    return@withLock Result.failure(it)
                }
                signalStore.assertCurrent(operation)
                val protoBundle = fetched.bundle
                val bundle = protoBundle.toLibsignal(peerDeviceId)
                val address = addressFor(peerDeviceId)
                val chatId = UUID.randomUUID().toString()
                val chat = signalStore.transaction(operation) {
                    SessionBuilder(signalStore, address).process(bundle)
                    val encrypted = SessionCipher(signalStore, address)
                        .encrypt(initialPlaintext.toByteArray(Charsets.UTF_8))
                    require(encrypted.type == CiphertextMessage.PREKEY_TYPE) {
                        "First secret-chat envelope was not a PreKeySignalMessage."
                    }
                    val envelope = encrypted.serialize()
                    val created = SecretChat(
                        id = chatId,
                        peerUserId = peerUserId,
                        peerDeviceId = peerDeviceId,
                        inviteId = "",
                        role = SecretChatRole.INITIATOR,
                        accepted = false,
                        inviteIds = emptySet(),
                        pendingAcceptInviteIds = emptySet(),
                        initialPlaintext = initialPlaintext,
                        initialEnvelope = envelope,
                        pendingSend = true,
                        initialEnvelopeHash = sha256(envelope),
                    )
                    saveMeta(created)
                    created
                }
                signalStore.assertCurrent(operation)
                sendPendingInvite(chat, operation)
            } catch (e: Exception) {
                Log.w(tag, "createSecretChat failed: ${e::class.java.simpleName}")
                Result.failure(e)
            }
        }
    }

    /** Commits the decrypted initial message and chat before asking the server to accept it. */
    suspend fun acceptIncomingInvite(
        inviteId: String,
        senderUserId: Long,
        senderDeviceId: String,
        initialEnvelope: ByteArray,
    ): Result<Pair<SecretChat, String>> = withContext(Dispatchers.IO) {
        val operation = signalStore.captureOperation()
        peerLock(senderUserId, senderDeviceId).withLock {
            try {
                signalStore.assertCurrent(operation)
                require(inviteId.isNotBlank() && senderUserId > 0 && senderDeviceId.isNotBlank() && initialEnvelope.isNotEmpty()) {
                    "Secret-chat invite is incomplete."
                }
                val replayKey = inviteReplayKey(senderUserId, senderDeviceId, initialEnvelope)
                val accepted = signalStore.transaction(operation) {
                    val duplicate = signalStore.readReplay(replayKey)
                    if (duplicate != null) {
                        val replay = decodeReplay(duplicate)
                        val existing = getChat(replay.chatId)
                            ?: throw InvalidPrekeyStateException("Invite replay record has no matching chat metadata.")
                        val updated = existing.withInvite(inviteId, accepted = true)
                        saveMeta(updated)
                        updated to replay.plaintext
                    } else {
                        val address = addressFor(senderDeviceId)
                        val cipher = SessionCipher(signalStore, address)
                        val plaintext = String(cipher.decrypt(PreKeySignalMessage(initialEnvelope)), Charsets.UTF_8)
                        val existing = findByPeer(senderUserId, senderDeviceId)
                        val chat = if (existing == null) {
                            SecretChat(
                                id = UUID.randomUUID().toString(),
                                peerUserId = senderUserId,
                                peerDeviceId = senderDeviceId,
                                inviteId = inviteId,
                                role = SecretChatRole.RECIPIENT,
                                accepted = true,
                                inviteIds = setOf(inviteId),
                                pendingAcceptInviteIds = setOf(inviteId),
                                initialPlaintext = plaintext,
                                initialEnvelope = null,
                                pendingSend = false,
                                initialEnvelopeHash = sha256(initialEnvelope),
                            )
                        } else {
                            existing.withInvite(inviteId, accepted = true).copy(
                                role = if (existing.role == SecretChatRole.INITIATOR) SecretChatRole.BOTH else existing.role,
                                initialPlaintext = existing.initialPlaintext.ifBlank { plaintext },
                                initialEnvelopeHash = existing.initialEnvelopeHash ?: sha256(initialEnvelope),
                            )
                        }
                        saveMeta(chat)
                        signalStore.recordReplay(replayKey, encodeReplay(chat.id, plaintext))
                        chat to plaintext
                    }
                }
                signalStore.assertCurrent(operation)
                val chat = accepted.first
                acceptPendingInvites(chat, operation)
                Result.success(chat to accepted.second)
            } catch (e: Exception) {
                Log.w(tag, "acceptIncomingInvite failed: ${e::class.java.simpleName}")
                Result.failure(e)
            }
        }
    }

    suspend fun rejectIncomingInvite(inviteId: String): Result<Unit> {
        val operation = signalStore.captureOperation()
        signalStore.assertCurrent(operation)
        val result = grpc.rejectSecretChatInvite(inviteId)
        signalStore.assertCurrent(operation)
        return result
    }

    /** Mark a server resolution against any invite id created by a retry or crossed invite. */
    fun markInviteResolution(inviteId: String, accepted: Boolean) {
        val operation = signalStore.captureOperation()
        signalStore.transaction(operation) {
            val chat = findByInviteId(inviteId) ?: return@transaction
            if (accepted) {
                saveMeta(chat.copy(accepted = true, pendingSend = false))
            } else if (!chat.accepted) {
                val remaining = chat.inviteIds - inviteId
                if (remaining.isEmpty()) {
                    signalStore.writeMetadata(secretChatMetadataKey(chat.id), null)
                } else {
                    saveMeta(chat.copy(inviteId = remaining.first(), inviteIds = remaining))
                }
            }
        }
    }

    fun markInitiatorChatAccepted(inviteId: String) = markInviteResolution(inviteId, accepted = true)

    fun forgetChat(chatId: String) {
        signalStore.writeMetadata(secretChatMetadataKey(chatId), null)
    }

    suspend fun sendMessage(chat: SecretChat, plaintext: String): Result<GrpcApiTransport.SecretMessageSent> =
        withContext(Dispatchers.IO) {
            val operation = signalStore.captureOperation()
            peerLock(chat.peerUserId, chat.peerDeviceId).withLock {
                try {
                    signalStore.assertCurrent(operation)
                    val envelope = signalStore.transaction(operation) {
                        SessionCipher(signalStore, addressFor(chat.peerDeviceId))
                            .encrypt(plaintext.toByteArray(Charsets.UTF_8))
                            .serialize()
                    }
                    signalStore.assertCurrent(operation)
                    val result = grpc.sendSecretMessage(chat.peerUserId, chat.peerDeviceId, envelope)
                    signalStore.assertCurrent(operation)
                    result
                } catch (e: Exception) {
                    Log.w(tag, "sendMessage failed for ${chat.id}: ${e::class.java.simpleName}")
                    Result.failure(e)
                }
            }
        }

    /** Persists plaintext and all Signal mutations before the caller acknowledges delivery. */
    suspend fun decryptIncoming(envelope: Shared.SecretEnvelope): Result<DecryptedSecret> = withContext(Dispatchers.IO) {
        val operation = signalStore.captureOperation()
        val senderUserId = envelope.senderUserId
        val senderDeviceId = envelope.senderDeviceId
        peerLock(senderUserId, senderDeviceId).withLock {
            try {
                signalStore.assertCurrent(operation)
                val raw = envelope.envelope.toByteArray()
                val replayKey = messageReplayKey(senderUserId, senderDeviceId, envelope.messageId)
                val prior = signalStore.readReplay(replayKey)
                if (prior != null) {
                    val replay = decodeReplay(prior)
                    return@withLock Result.success(
                        DecryptedSecret(
                            envelope.messageId,
                            replay.chatId.takeIf(String::isNotBlank)?.let(::getChat),
                            senderUserId,
                            senderDeviceId,
                            replay.plaintext,
                            envelope.sentAt.seconds,
                            duplicate = true,
                        ),
                    )
                }

                val address = addressFor(senderDeviceId)
                fun decryptAndRecord(prekey: Boolean): DecryptedSecret = signalStore.transaction(operation) {
                    val cipher = SessionCipher(signalStore, address)
                    val plaintextBytes = if (prekey) {
                        cipher.decrypt(PreKeySignalMessage(raw))
                    } else {
                        cipher.decrypt(SignalMessage(raw))
                    }
                    val text = String(plaintextBytes, Charsets.UTF_8)
                    val chat = findByPeer(senderUserId, senderDeviceId)
                    signalStore.recordReplay(replayKey, encodeReplay(chat?.id.orEmpty(), text))
                    DecryptedSecret(
                        messageId = envelope.messageId,
                        chat = chat,
                        senderUserId = senderUserId,
                        senderDeviceId = senderDeviceId,
                        plaintext = text,
                        sentAtSeconds = envelope.sentAt.seconds,
                        isPrekeyMessage = prekey,
                    )
                }
                val decrypted = try {
                    decryptAndRecord(prekey = false)
                } catch (first: Exception) {
                    if (first is DurableCryptoCommitException || first is CryptoOperationInvalidatedException) throw first
                    decryptAndRecord(prekey = true)
                }
                signalStore.assertCurrent(operation)
                Result.success(decrypted)
            } catch (e: Exception) {
                Log.w(tag, "decryptIncoming failed: ${e::class.java.simpleName}")
                Result.failure(e)
            }
        }
    }

    suspend fun ack(messageId: String): Result<Unit> {
        val operation = signalStore.captureOperation()
        signalStore.assertCurrent(operation)
        val result = grpc.ackSecretMessage(messageId)
        signalStore.assertCurrent(operation)
        return result
    }

    /** Retries durable invite sends and accepts after process restart or a returned network. */
    suspend fun retryPendingOperations() = withContext(Dispatchers.IO) {
        val operation = runCatching { signalStore.captureOperation() }.getOrNull() ?: return@withContext
        val chats = runCatching { listChats() }.getOrNull() ?: return@withContext
        for (chat in chats) {
            runCatching {
                peerLock(chat.peerUserId, chat.peerDeviceId).withLock {
                    signalStore.assertCurrent(operation)
                    if (chat.pendingSend && chat.initialEnvelope != null) sendPendingInvite(chat, operation)
                    val current = getChat(chat.id) ?: return@withLock
                    acceptPendingInvites(current, operation)
                }
            }
        }
    }

    fun listChats(): List<SecretChat> = signalStore.allMetadata(SECRET_CHAT_NAMESPACE)
        .mapNotNull { (key, value) ->
            val id = key.removePrefix(SECRET_CHAT_NAMESPACE)
            if (id == key) null else decodeMeta(id, value)
        }

    fun getChat(chatId: String): SecretChat? =
        signalStore.readMetadata(secretChatMetadataKey(chatId))?.let { decodeMeta(chatId, it) }

    fun findByPeer(peerUserId: Long, peerDeviceId: String): SecretChat? =
        listChats().firstOrNull { it.peerUserId == peerUserId && it.peerDeviceId == peerDeviceId }

    fun findByInviteId(inviteId: String): SecretChat? =
        listChats().firstOrNull { inviteId == it.inviteId || inviteId in it.inviteIds }

    fun forgetAll() = signalStore.forgetSecretChats()

    private suspend fun sendPendingInvite(chat: SecretChat, operation: CryptoOperation): Result<SecretChat> {
        val envelope = chat.initialEnvelope
            ?: return Result.failure(InvalidPrekeyStateException("Pending secret-chat invite has no encrypted envelope."))
        signalStore.assertCurrent(operation)
        val sent = grpc.sendSecretChatInvite(chat.peerUserId, chat.peerDeviceId, envelope).getOrElse {
            return Result.failure(it)
        }
        signalStore.assertCurrent(operation)
        val updated = signalStore.transaction(operation) {
            val current = getChat(chat.id)
                ?: throw InvalidPrekeyStateException("Pending secret-chat metadata disappeared.")
            val inviteIds = current.inviteIds + sent.inviteId
            current.copy(
                inviteId = current.inviteId.ifBlank { sent.inviteId },
                inviteIds = inviteIds,
                pendingSend = false,
            ).also(::saveMeta)
        }
        return Result.success(updated)
    }

    private suspend fun acceptPendingInvites(chat: SecretChat, operation: CryptoOperation) {
        for (inviteId in chat.pendingAcceptInviteIds) {
            signalStore.assertCurrent(operation)
            val result = grpc.acceptSecretChatInvite(inviteId)
            signalStore.assertCurrent(operation)
            if (result.isSuccess) {
                signalStore.transaction(operation) {
                    val current = getChat(chat.id) ?: return@transaction
                    saveMeta(current.copy(pendingAcceptInviteIds = current.pendingAcceptInviteIds - inviteId))
                }
            } else {
                Log.w(tag, "AcceptSecretChatInvite remains pending: ${result.exceptionOrNull()?.javaClass?.simpleName}")
            }
        }
    }

    private fun peerLock(userId: Long, deviceId: String): Mutex =
        peerLocks.getOrPut("$userId::$deviceId") { Mutex() }

    private fun addressFor(peerDeviceId: String): SignalProtocolAddress = SignalProtocolAddress(peerDeviceId, 1)

    private fun saveMeta(chat: SecretChat) {
        signalStore.writeMetadata(secretChatMetadataKey(chat.id), encodeChat(chat))
    }

    private fun decodeMeta(chatId: String, encoded: String): SecretChat? = try {
        if (encoded.contains('|')) decodeLegacyMeta(chatId, encoded) else decodeChat(chatId, encoded)
    } catch (e: Exception) {
        Log.w(tag, "Could not decode secret-chat metadata: ${e::class.java.simpleName}")
        null
    }

    private fun decodeLegacyMeta(chatId: String, encoded: String): SecretChat? {
        val parts = encoded.split('|')
        if (parts.size < 5) return null
        return SecretChat(
            id = chatId,
            peerUserId = parts[0].toLong(),
            peerDeviceId = parts[1],
            inviteId = parts[2],
            role = SecretChatRole.valueOf(parts[3]),
            accepted = parts[4] == "1",
            inviteIds = setOf(parts[2]).filter(String::isNotBlank).toSet(),
        )
    }

    private fun encodeChat(chat: SecretChat): String {
        val bytes = ByteArrayOutputStream()
        DataOutputStream(bytes).use { output ->
            output.writeInt(CHAT_METADATA_VERSION)
            output.writeLong(chat.peerUserId)
            output.writeString(chat.peerDeviceId)
            output.writeString(chat.inviteId)
            output.writeString(chat.role.name)
            output.writeBoolean(chat.accepted)
            output.writeStrings(chat.inviteIds)
            output.writeStrings(chat.pendingAcceptInviteIds)
            output.writeString(chat.initialPlaintext)
            output.writeByteArray(chat.initialEnvelope)
            output.writeBoolean(chat.pendingSend)
            output.writeString(chat.initialEnvelopeHash.orEmpty())
        }
        return Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP)
    }

    private fun decodeChat(chatId: String, encoded: String): SecretChat {
        DataInputStream(ByteArrayInputStream(Base64.decode(encoded, Base64.NO_WRAP))).use { input ->
            require(input.readInt() == CHAT_METADATA_VERSION) { "Unknown secret-chat metadata version" }
            return SecretChat(
                id = chatId,
                peerUserId = input.readLong(),
                peerDeviceId = input.readString(),
                inviteId = input.readString(),
                role = SecretChatRole.valueOf(input.readString()),
                accepted = input.readBoolean(),
                inviteIds = input.readStrings(),
                pendingAcceptInviteIds = input.readStrings(),
                initialPlaintext = input.readString(),
                initialEnvelope = input.readByteArray(),
                pendingSend = input.readBoolean(),
                initialEnvelopeHash = input.readString().ifBlank { null },
            )
        }
    }

    private fun SecretChat.withInvite(inviteId: String, accepted: Boolean): SecretChat {
        val ids = inviteIds + inviteId
        return copy(
            inviteId = this.inviteId.ifBlank { inviteId },
            inviteIds = ids,
            pendingAcceptInviteIds = if (inviteId in inviteIds) pendingAcceptInviteIds else pendingAcceptInviteIds + inviteId,
            accepted = this.accepted || accepted,
        )
    }

    private fun inviteReplayKey(senderUserId: Long, senderDeviceId: String, envelope: ByteArray): String =
        "invite:${sha256("$senderUserId|$senderDeviceId|${sha256(envelope)}")}"

    private fun messageReplayKey(senderUserId: Long, senderDeviceId: String, messageId: String): String =
        "message:${sha256("$senderUserId|$senderDeviceId|$messageId")}"

    private fun encodeReplay(chatId: String, plaintext: String): String {
        val bytes = ByteArrayOutputStream()
        DataOutputStream(bytes).use {
            it.writeString(chatId)
            it.writeString(plaintext)
        }
        return Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP)
    }

    private fun decodeReplay(encoded: String): ReplayRecord =
        DataInputStream(ByteArrayInputStream(Base64.decode(encoded, Base64.NO_WRAP))).use {
            ReplayRecord(it.readString(), it.readString())
        }

    private fun DataOutputStream.writeStrings(values: Set<String>) {
        writeInt(values.size)
        values.sorted().forEach { writeString(it) }
    }

    private fun DataInputStream.readStrings(): Set<String> {
        val size = readInt()
        require(size in 0..MAX_INVITE_IDS) { "Invalid invite id count" }
        return buildSet { repeat(size) { add(readString()) } }
    }

    private fun DataOutputStream.writeString(value: String) {
        val bytes = value.toByteArray(StandardCharsets.UTF_8)
        writeInt(bytes.size)
        write(bytes)
    }

    private fun DataInputStream.readString(): String {
        val size = readInt()
        require(size in 0..MAX_METADATA_BYTES) { "Invalid secret-chat metadata field size" }
        return String(ByteArray(size).also(::readFully), StandardCharsets.UTF_8)
    }

    private fun DataOutputStream.writeByteArray(value: ByteArray?) {
        if (value == null) writeInt(-1) else {
            writeInt(value.size)
            write(value)
        }
    }

    private fun DataInputStream.readByteArray(): ByteArray? {
        val size = readInt()
        if (size == -1) return null
        require(size in 0..MAX_METADATA_BYTES) { "Invalid secret-chat envelope size" }
        return ByteArray(size).also(::readFully)
    }

    data class SecretChat(
        val id: String,
        val peerUserId: Long,
        val peerDeviceId: String,
        val inviteId: String,
        val role: SecretChatRole,
        val accepted: Boolean,
        val inviteIds: Set<String> = inviteId.takeIf(String::isNotBlank)?.let(::setOf).orEmpty(),
        val pendingAcceptInviteIds: Set<String> = emptySet(),
        val initialPlaintext: String = "",
        val initialEnvelope: ByteArray? = null,
        val pendingSend: Boolean = false,
        val initialEnvelopeHash: String? = null,
    )

    enum class SecretChatRole { INITIATOR, RECIPIENT, BOTH }

    data class DecryptedSecret(
        val messageId: String,
        val chat: SecretChat?,
        val senderUserId: Long,
        val senderDeviceId: String,
        val plaintext: String,
        val sentAtSeconds: Long,
        val duplicate: Boolean = false,
        val isPrekeyMessage: Boolean = false,
    )

    private data class ReplayRecord(val chatId: String, val plaintext: String)

    companion object {
        private const val SECRET_CHAT_NAMESPACE = "secret_chat::"
        private const val CHAT_METADATA_VERSION = 2
        private const val MAX_INVITE_IDS = 1000
        private const val MAX_METADATA_BYTES = 16 * 1024 * 1024

        private fun secretChatMetadataKey(chatId: String) = "$SECRET_CHAT_NAMESPACE$chatId"

        private fun sha256(bytes: ByteArray): String = MessageDigest.getInstance("SHA-256")
            .digest(bytes)
            .joinToString("") { "%02x".format(it) }

        private fun sha256(value: String): String = sha256(value.toByteArray(StandardCharsets.UTF_8))
    }
}

internal fun UsersApiOuterClass.PrekeyBundle.toLibsignal(peerDeviceId: String): PreKeyBundle {
    if (!hasKyberPrekey()) {
        throw PrekeyServerUpdateRequiredException()
    }
    if (deviceId != peerDeviceId) {
        throw InvalidPrekeyBundleException("Users returned a prekey bundle for a different device.")
    }
    try {
        require(registrationId > 0) { "Missing registration id" }
        require(identityPubkey.size() > 0) { "Missing identity key" }
        require(hasSignedPrekey()) { "Missing signed EC prekey" }
        require(signedPrekey.prekeyId > 0 && signedPrekey.publicKey.size() > 0 && signedPrekey.signature.size() > 0) {
            "Incomplete signed EC prekey"
        }
        val hasOneTime = hasOneTimePrekey
        if (hasOneTime) {
            require(oneTimePrekey.prekeyId > 0 && oneTimePrekey.publicKey.size() > 0) { "Incomplete one-time EC prekey" }
        }
        val kyber = kyberPrekey
        require(kyber.prekeyId > 0 && kyber.publicKey.size() > 0 && kyber.signature.size() > 0) {
            "Incomplete Kyber prekey"
        }
        val identity = IdentityKey(identityPubkey.toByteArray())
        val ecPrekey = if (hasOneTime) ECPublicKey(oneTimePrekey.publicKey.toByteArray()) else null
        val signedEc = ECPublicKey(signedPrekey.publicKey.toByteArray())
        val kyberPublic = KEMPublicKey(kyber.publicKey.toByteArray())
        return PreKeyBundle(
            registrationId,
            1,
            if (hasOneTime) oneTimePrekey.prekeyId else PreKeyBundle.NULL_PRE_KEY_ID,
            ecPrekey,
            signedPrekey.prekeyId,
            signedEc,
            signedPrekey.signature.toByteArray(),
            identity,
            kyber.prekeyId,
            kyberPublic,
            kyber.signature.toByteArray(),
        )
    } catch (e: PrekeyServerUpdateRequiredException) {
        throw e
    } catch (e: Exception) {
        throw InvalidPrekeyBundleException("Users returned an invalid PQXDH prekey bundle.", e)
    }
}
