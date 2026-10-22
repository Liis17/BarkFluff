package com.barkfluff.client.crypto

import android.util.Base64
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.domain.gateway.PrekeyGateway
import com.google.protobuf.ByteString
import io.grpc.Status
import io.grpc.StatusException
import io.grpc.StatusRuntimeException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.signal.libsignal.protocol.IdentityKeyPair
import org.signal.libsignal.protocol.ecc.ECKeyPair
import org.signal.libsignal.protocol.kem.KEMKeyPair
import org.signal.libsignal.protocol.kem.KEMKeyType
import org.signal.libsignal.protocol.state.KyberPreKeyRecord
import org.signal.libsignal.protocol.state.PreKeyRecord
import org.signal.libsignal.protocol.state.SignedPreKeyRecord
import org.signal.libsignal.protocol.util.KeyHelper

class PrekeyServerUpdateRequiredException : IllegalStateException(
    "This server does not support PQXDH prekey status. Update the server before using secret chats.",
)

class InvalidPrekeyStateException(message: String) : IllegalStateException(message)

/** Publishes durable, scope-bound PQXDH state and retries the same pending bytes after failures. */
class PrekeyManager(
    private val store: BarkFluffSignalStore,
) {
    private val syncMutex = Mutex()

    val isRegistered: Boolean
        get() = store.isInitialized() && store.readManagerValue(KEY_PQ_REGISTRATION_MARKER)?.startsWith("1|") == true

    suspend fun synchronize(gateway: PrekeyGateway) = syncMutex.withLock {
        withContext(Dispatchers.IO) {
            val operation = store.captureOperation()
            var status = ownStatus(gateway, operation)

            if (status.hasBundle && !store.isInitialized()) {
                store.migrateLegacyIfMatching(status.identityPubkey.toByteArray(), status.registrationId)
            }

            if (!store.isInitialized()) {
                if (status.hasBundle) {
                    throw InvalidPrekeyStateException(
                        "The server has device keys but this installation has no matching private keys. Re-register this device.",
                    )
                }
                createInitialRegistration(operation)
            } else {
                requireServerIdentity(status)
            }

            // A rotation can have reached Users even when its reply never reached this process.
            // Only the exact durable pending publication may advance the local current key.
            store.readManagerValue(KEY_PENDING_ROTATION)?.let { encoded ->
                val pending = decodeRotation(encoded)
                if (pendingRotationMatches(status, pending)) finalizeRotation(operation, pending)
            }
            val pendingRegistration = store.readManagerValue(KEY_PENDING_REGISTER)
            if (status.hasBundle && status.supportsPqxdh && localLastResortMatches(status)) {
                finalizeRegistration(operation, status)
            } else {
                if (status.supportsPqxdh && !localLastResortMatches(status) && pendingRegistration == null) {
                    throw InvalidPrekeyStateException(
                        "The server's PQXDH last-resort key does not match this device's private key store.",
                    )
                }
                ensurePendingRegistration(operation)
                val request = decodeRegistration(store.readManagerValue(KEY_PENDING_REGISTER)!!)
                store.assertCurrent(operation)
                gateway.register(request).getOrThrow()
                store.assertCurrent(operation)
                status = ownStatus(gateway, operation)
                requireServerIdentity(status)
                if (!status.hasBundle || !status.supportsPqxdh || !localLastResortMatches(status)) {
                    throw InvalidPrekeyStateException("The server did not confirm this device's PQXDH registration.")
                }
                finalizeRegistration(operation, status)
            }

            status = rotateIfNeeded(gateway, operation, status)
            requireServerIdentity(status)
            if (!status.supportsPqxdh || !localLastResortMatches(status)) {
                throw InvalidPrekeyStateException("The server's current PQXDH key does not match local device keys.")
            }
            replenishIfNeeded(gateway, operation, status)
        }
    }

    fun reset() = store.reset()
    fun invalidateForLogout() = store.invalidateForLogout()
    fun reset(scope: CryptoStoreScope?) = store.reset(scope)
    fun hasPendingCleanup(scope: CryptoStoreScope?) = store.hasPendingCleanup(scope)
    fun retryPendingCleanup() = store.retryPendingCleanup()

    private suspend fun createInitialRegistration(operation: CryptoOperation) {
        store.transaction(operation) {
            val identity = IdentityKeyPair.generate()
            val registrationId = KeyHelper.generateRegistrationId(false)
            val signed = generateSignedPreKey(identity, INITIAL_SIGNED_PREKEY_ID)
            val oneTime = (1..INITIAL_ONE_TIME_PREKEYS).map { id -> PreKeyRecord(id, ECKeyPair.generate()) }
            val lastResort = generateKyberPreKey(identity, INITIAL_KYBER_PREKEY_ID)
            val kyberOneTime = (2..INITIAL_ONE_TIME_KYBER_PREKEYS + 1).map { id ->
                generateKyberPreKey(identity, id)
            }

            store.initialize(identity, registrationId)
            store.storeSignedPreKey(signed.id, signed)
            oneTime.forEach { store.storePreKey(it.id, it) }
            store.storeKyberPreKey(lastResort.id, lastResort)
            store.addLastResortKyberPreKeyId(lastResort.id)
            kyberOneTime.forEach { store.storeKyberPreKey(it.id, it) }
            store.writeManagerValue(KEY_CURRENT_SIGNED_ID, signed.id.toString())
            store.writeManagerValue(KEY_CURRENT_LAST_RESORT_ID, lastResort.id.toString())
            store.writeManagerValue(KEY_NEXT_ONE_TIME_ID, (INITIAL_ONE_TIME_PREKEYS + 1).toString())
            store.writeManagerValue(KEY_NEXT_SIGNED_ID, (INITIAL_SIGNED_PREKEY_ID + 1).toString())
            store.writeManagerValue(KEY_NEXT_KYBER_ID, (INITIAL_ONE_TIME_KYBER_PREKEYS + 2).toString())
            store.writeManagerValue(KEY_PQ_REGISTRATION_MARKER, null)
            store.writeManagerValue(KEY_PENDING_REGISTER, encodeRegistration(buildRegistrationRequest()))
        }
    }

    private suspend fun ensurePendingRegistration(operation: CryptoOperation) {
        if (store.readManagerValue(KEY_PENDING_REGISTER) != null) return
        store.transaction(operation) {
            ensureCounters()
            ensureRegistrationKeyPools()
            store.writeManagerValue(KEY_PENDING_REGISTER, encodeRegistration(buildRegistrationRequest()))
        }
    }

    private fun ensureRegistrationKeyPools() {
        val identity = store.getIdentityKeyPair()
        val existingEc = store.loadPreKeys()
        var nextEcId = nextId(KEY_NEXT_ONE_TIME_ID, existingEc.maxOfOrNull { it.id }?.plus(1) ?: 1)
        repeat((INITIAL_ONE_TIME_PREKEYS - existingEc.size).coerceAtLeast(0)) {
            val record = PreKeyRecord(nextEcId++, ECKeyPair.generate())
            store.storePreKey(record.id, record)
        }
        store.writeManagerValue(KEY_NEXT_ONE_TIME_ID, nextEcId.toString())

        val allKyber = store.loadKyberPreKeys()
        var nextKyberId = nextId(KEY_NEXT_KYBER_ID, allKyber.maxOfOrNull { it.id }?.plus(1) ?: INITIAL_KYBER_PREKEY_ID)
        val lastResortId = store.readManagerValue(KEY_CURRENT_LAST_RESORT_ID)?.toIntOrNull()
            ?.takeIf { store.containsKyberPreKey(it) }
            ?: allKyber.firstOrNull { store.hasLastResortKyberPreKeyId(it.id) }?.id
        if (lastResortId == null) {
            val record = generateKyberPreKey(identity, nextKyberId++)
            store.storeKyberPreKey(record.id, record)
            store.addLastResortKyberPreKeyId(record.id)
            store.writeManagerValue(KEY_CURRENT_LAST_RESORT_ID, record.id.toString())
        }
        val refreshedKyber = store.loadKyberPreKeys()
        val oneTimeCount = refreshedKyber.count { !store.hasLastResortKyberPreKeyId(it.id) }
        repeat((INITIAL_ONE_TIME_KYBER_PREKEYS - oneTimeCount).coerceAtLeast(0)) {
            val record = generateKyberPreKey(identity, nextKyberId++)
            store.storeKyberPreKey(record.id, record)
        }
        store.writeManagerValue(KEY_NEXT_KYBER_ID, nextKyberId.toString())
    }

    private suspend fun rotateIfNeeded(
        gateway: PrekeyGateway,
        operation: CryptoOperation,
        status: UsersApiOuterClass.GetOwnPrekeyStatusResponse,
    ): UsersApiOuterClass.GetOwnPrekeyStatusResponse {
        var pending = store.readManagerValue(KEY_PENDING_ROTATION)
        if (pending == null) {
            val signedId = store.readManagerValue(KEY_CURRENT_SIGNED_ID)?.toIntOrNull()
                ?: store.loadSignedPreKeys().maxOfOrNull { it.id }
                ?: throw InvalidPrekeyStateException("This device has no signed prekey.")
            val signed = store.loadSignedPreKey(signedId)
            val lastResortId = store.readManagerValue(KEY_CURRENT_LAST_RESORT_ID)?.toIntOrNull()
                ?: throw InvalidPrekeyStateException("This device has no PQXDH last-resort key.")
            val lastResort = store.loadKyberPreKey(lastResortId)
            val due = System.currentTimeMillis() - signed.timestamp >= ROTATION_INTERVAL_MILLIS ||
                System.currentTimeMillis() - lastResort.timestamp >= ROTATION_INTERVAL_MILLIS
            if (!due) return status

            store.transaction(operation) {
                ensureCounters()
                val identity = store.getIdentityKeyPair()
                val newSigned = generateSignedPreKey(identity, nextId(KEY_NEXT_SIGNED_ID, signedId + 1))
                val newLastResortId = nextId(KEY_NEXT_KYBER_ID, lastResortId + 1)
                val newLastResort = generateKyberPreKey(identity, newLastResortId)
                store.storeSignedPreKey(newSigned.id, newSigned)
                store.storeKyberPreKey(newLastResort.id, newLastResort)
                store.addLastResortKyberPreKeyId(newLastResort.id)
                store.writeManagerValue(KEY_NEXT_SIGNED_ID, (newSigned.id + 1).toString())
                store.writeManagerValue(KEY_NEXT_KYBER_ID, (newLastResort.id + 1).toString())
                val request = UsersApiOuterClass.RotateSignedPrekeyRequest.newBuilder()
                    .setSignedPrekey(newSigned.toProto())
                    .setLastResortKyberPrekey(newLastResort.toProto())
                    .build()
                store.writeManagerValue(KEY_PENDING_ROTATION, encode(request.toByteArray()))
            }
            pending = store.readManagerValue(KEY_PENDING_ROTATION)
        }

        val request = decodeRotation(pending!!)
        if (pendingRotationMatches(status, request)) {
            finalizeRotation(operation, request)
            return status
        }

        store.assertCurrent(operation)
        gateway.rotate(request).getOrThrow()
        store.assertCurrent(operation)
        val confirmed = ownStatus(gateway, operation)
        requireServerIdentity(confirmed)
        if (!confirmed.supportsPqxdh || !confirmed.hasLastResortKyberPrekey() ||
            request.lastResortKyberPrekey.prekeyId != confirmed.lastResortKyberPrekey.prekeyId ||
            request.lastResortKyberPrekey.publicKey != confirmed.lastResortKyberPrekey.publicKey ||
            request.lastResortKyberPrekey.signature != confirmed.lastResortKyberPrekey.signature
        ) {
            throw InvalidPrekeyStateException("The server did not confirm the signed-prekey rotation.")
        }
        finalizeRotation(operation, request)
        return confirmed
    }

    private fun pendingRotationMatches(
        status: UsersApiOuterClass.GetOwnPrekeyStatusResponse,
        request: UsersApiOuterClass.RotateSignedPrekeyRequest,
    ): Boolean = status.hasBundle && status.supportsPqxdh && status.hasLastResortKyberPrekey() &&
        request.lastResortKyberPrekey == status.lastResortKyberPrekey

    private suspend fun replenishIfNeeded(
        gateway: PrekeyGateway,
        operation: CryptoOperation,
        initialStatus: UsersApiOuterClass.GetOwnPrekeyStatusResponse,
    ) {
        var pending = store.readManagerValue(KEY_PENDING_REPLENISH)
        var status = initialStatus
        if (pending == null) {
            val ecCount = if (status.remainingOneTimePrekeys < MIN_ONE_TIME_PREKEYS_THRESHOLD) {
                (INITIAL_ONE_TIME_PREKEYS - status.remainingOneTimePrekeys).coerceAtLeast(0)
            } else 0
            val kyberCount = if (status.remainingOneTimeKyberPrekeys < MIN_ONE_TIME_PREKEYS_THRESHOLD) {
                (INITIAL_ONE_TIME_KYBER_PREKEYS - status.remainingOneTimeKyberPrekeys).coerceAtLeast(0)
            } else 0
            if (ecCount == 0 && kyberCount == 0) return

            store.transaction(operation) {
                ensureCounters()
                val newEc = (0 until ecCount).map { offset ->
                    PreKeyRecord(nextId(KEY_NEXT_ONE_TIME_ID, 1) + offset, ECKeyPair.generate())
                }
                val nextEcId = nextId(KEY_NEXT_ONE_TIME_ID, 1) + ecCount
                val identity = store.getIdentityKeyPair()
                var nextKyberId = nextId(KEY_NEXT_KYBER_ID, 1)
                val newKyber = (0 until kyberCount).map { offset ->
                    generateKyberPreKey(identity, nextKyberId + offset)
                }
                nextKyberId += kyberCount
                newEc.forEach { store.storePreKey(it.id, it) }
                newKyber.forEach { store.storeKyberPreKey(it.id, it) }
                store.writeManagerValue(KEY_NEXT_ONE_TIME_ID, nextEcId.toString())
                store.writeManagerValue(KEY_NEXT_KYBER_ID, nextKyberId.toString())
                val request = UsersApiOuterClass.ReplenishOneTimePrekeysRequest.newBuilder()
                    .addAllPrekeys(newEc.map { it.toProto() })
                    .addAllKyberPrekeys(newKyber.map { it.toProto() })
                    .build()
                store.writeManagerValue(KEY_PENDING_REPLENISH, encode(request.toByteArray()))
            }
            pending = store.readManagerValue(KEY_PENDING_REPLENISH)
        }

        val request = decodeReplenish(pending!!)
        store.assertCurrent(operation)
        gateway.replenish(request).getOrThrow()
        store.assertCurrent(operation)
        status = ownStatus(gateway, operation)
        requireServerIdentity(status)
        if (!status.hasBundle || !status.supportsPqxdh || !localLastResortMatches(status)) {
            throw InvalidPrekeyStateException("The server's PQXDH registration changed while replenishing keys.")
        }
        store.writeManagerValue(KEY_PENDING_REPLENISH, null)
    }

    private suspend fun ownStatus(
        gateway: PrekeyGateway,
        operation: CryptoOperation,
    ): UsersApiOuterClass.GetOwnPrekeyStatusResponse {
        store.assertCurrent(operation)
        val result = gateway.ownStatus()
        store.assertCurrent(operation)
        return result.getOrElse { error ->
            if (error.hasStatus(Status.Code.UNIMPLEMENTED)) throw PrekeyServerUpdateRequiredException()
            throw error
        }
    }

    private fun requireServerIdentity(status: UsersApiOuterClass.GetOwnPrekeyStatusResponse) {
        if (!status.hasBundle) return
        if (!store.isInitialized()) throw InvalidPrekeyStateException("Local device key state is unavailable.")
        val localIdentity = store.getIdentityKeyPair().publicKey.serialize()
        if (!localIdentity.contentEquals(status.identityPubkey.toByteArray()) ||
            store.getLocalRegistrationId() != status.registrationId
        ) {
            throw InvalidPrekeyStateException(
                "The server's device identity does not match local keys. Secret chats are paused until this device is re-registered.",
            )
        }
    }

    private fun localLastResortMatches(status: UsersApiOuterClass.GetOwnPrekeyStatusResponse): Boolean {
        if (!status.hasLastResortKyberPrekey()) return false
        val id = store.readManagerValue(KEY_CURRENT_LAST_RESORT_ID)?.toIntOrNull() ?: return false
        if (id != status.lastResortKyberPrekey.prekeyId || !store.containsKyberPreKey(id)) return false
        val local = runCatching { store.loadKyberPreKey(id) }.getOrNull() ?: return false
        return local.keyPair.publicKey.serialize().contentEquals(status.lastResortKyberPrekey.publicKey.toByteArray()) &&
            local.signature.contentEquals(status.lastResortKyberPrekey.signature.toByteArray())
    }

    private fun finalizeRegistration(operation: CryptoOperation, status: UsersApiOuterClass.GetOwnPrekeyStatusResponse) {
        val identity = encode(status.identityPubkey.toByteArray())
        val marker = "1|$identity|${status.registrationId}|${status.lastResortKyberPrekey.prekeyId}"
        if (store.readManagerValue(KEY_PQ_REGISTRATION_MARKER) == marker &&
            store.readManagerValue(KEY_PENDING_REGISTER) == null) return
        store.transaction(operation) {
            val id = status.lastResortKyberPrekey.prekeyId
            store.writeManagerValue(KEY_CURRENT_LAST_RESORT_ID, id.toString())
            store.writeManagerValue(KEY_PQ_REGISTRATION_MARKER, marker)
            store.writeManagerValue(KEY_PENDING_REGISTER, null)
        }
    }

    private fun finalizeRotation(operation: CryptoOperation, request: UsersApiOuterClass.RotateSignedPrekeyRequest) {
        store.transaction(operation) {
            store.writeManagerValue(KEY_CURRENT_SIGNED_ID, request.signedPrekey.prekeyId.toString())
            store.writeManagerValue(KEY_CURRENT_LAST_RESORT_ID, request.lastResortKyberPrekey.prekeyId.toString())
            val identity = encode(store.getIdentityKeyPair().publicKey.serialize())
            val registrationId = store.getLocalRegistrationId()
            store.writeManagerValue(
                KEY_PQ_REGISTRATION_MARKER,
                "1|$identity|$registrationId|${request.lastResortKyberPrekey.prekeyId}",
            )
            store.writeManagerValue(KEY_PENDING_ROTATION, null)
        }
    }

    private fun ensureCounters() {
        val ec = store.loadPreKeys()
        val signed = store.loadSignedPreKeys()
        val kyber = store.loadKyberPreKeys()
        reconcileNextId(KEY_NEXT_ONE_TIME_ID, (ec.maxOfOrNull { it.id } ?: 0) + 1)
        reconcileNextId(KEY_NEXT_SIGNED_ID, (signed.maxOfOrNull { it.id } ?: 0) + 1)
        reconcileNextId(KEY_NEXT_KYBER_ID, (kyber.maxOfOrNull { it.id } ?: 0) + 1)
        if (store.readManagerValue(KEY_CURRENT_SIGNED_ID) == null) {
            signed.maxByOrNull { it.timestamp }?.let { store.writeManagerValue(KEY_CURRENT_SIGNED_ID, it.id.toString()) }
        }
        if (store.readManagerValue(KEY_CURRENT_LAST_RESORT_ID) == null) {
            kyber.firstOrNull { store.hasLastResortKyberPreKeyId(it.id) }
                ?.let { store.writeManagerValue(KEY_CURRENT_LAST_RESORT_ID, it.id.toString()) }
        }
    }

    private fun reconcileNextId(key: String, minimum: Int) {
        val current = store.readManagerValue(key)?.toIntOrNull() ?: minimum
        if (current < minimum) store.writeManagerValue(key, minimum.toString())
    }

    private fun buildRegistrationRequest(): UsersApiOuterClass.RegisterPrekeyBundleRequest {
        val identity = store.getIdentityKeyPair()
        val signedId = store.readManagerValue(KEY_CURRENT_SIGNED_ID)?.toIntOrNull()
            ?: store.loadSignedPreKeys().maxByOrNull { it.timestamp }?.id
            ?: throw InvalidPrekeyStateException("This device has no signed prekey.")
        val kyberId = store.readManagerValue(KEY_CURRENT_LAST_RESORT_ID)?.toIntOrNull()
            ?: throw InvalidPrekeyStateException("This device has no Kyber last-resort key.")
        val signed = store.loadSignedPreKey(signedId)
        val lastResort = store.loadKyberPreKey(kyberId)
        val oneTimeKyber = store.loadKyberPreKeys().filterNot { store.hasLastResortKyberPreKeyId(it.id) }
        return UsersApiOuterClass.RegisterPrekeyBundleRequest.newBuilder()
            .setRegistrationId(store.getLocalRegistrationId())
            .setIdentityPubkey(ByteString.copyFrom(identity.publicKey.serialize()))
            .setSignedPrekey(signed.toProto())
            .addAllOneTimePrekeys(store.loadPreKeys().map { it.toProto() })
            .setLastResortKyberPrekey(lastResort.toProto())
            .addAllOneTimeKyberPrekeys(oneTimeKyber.map { it.toProto() })
            .build()
    }

    private fun generateSignedPreKey(identityKeyPair: IdentityKeyPair, id: Int): SignedPreKeyRecord {
        val keyPair = ECKeyPair.generate()
        val signature = identityKeyPair.privateKey.calculateSignature(keyPair.publicKey.serialize())
        return SignedPreKeyRecord(id, System.currentTimeMillis(), keyPair, signature)
    }

    private fun generateKyberPreKey(identityKeyPair: IdentityKeyPair, id: Int): KyberPreKeyRecord {
        val keyPair = KEMKeyPair.generate(KEMKeyType.KYBER_1024)
        val signature = identityKeyPair.privateKey.calculateSignature(keyPair.publicKey.serialize())
        return KyberPreKeyRecord(id, System.currentTimeMillis(), keyPair, signature)
    }

    private fun nextId(key: String, fallback: Int): Int {
        val id = store.readManagerValue(key)?.toIntOrNull() ?: fallback
        if (id <= 0 || id == Int.MAX_VALUE) throw InvalidPrekeyStateException("Prekey identifier space is exhausted.")
        return id
    }

    private fun encode(bytes: ByteArray): String = Base64.encodeToString(bytes, Base64.NO_WRAP)
    private fun encodeRegistration(request: UsersApiOuterClass.RegisterPrekeyBundleRequest): String = encode(request.toByteArray())
    private fun decodeRegistration(value: String) = UsersApiOuterClass.RegisterPrekeyBundleRequest.parseFrom(Base64.decode(value, Base64.NO_WRAP))
    private fun decodeRotation(value: String) = UsersApiOuterClass.RotateSignedPrekeyRequest.parseFrom(Base64.decode(value, Base64.NO_WRAP))
    private fun decodeReplenish(value: String) = UsersApiOuterClass.ReplenishOneTimePrekeysRequest.parseFrom(Base64.decode(value, Base64.NO_WRAP))

    private fun Throwable.hasStatus(code: Status.Code): Boolean {
        var current: Throwable? = this
        while (current != null) {
            if ((current as? StatusRuntimeException)?.status?.code == code ||
                (current as? StatusException)?.status?.code == code
            ) return true
            current = current.cause
        }
        return false
    }

    private fun PreKeyRecord.toProto(): UsersApiOuterClass.OneTimePreKey =
        UsersApiOuterClass.OneTimePreKey.newBuilder()
            .setPrekeyId(id)
            .setPublicKey(ByteString.copyFrom(keyPair.publicKey.serialize()))
            .build()

    private fun SignedPreKeyRecord.toProto(): UsersApiOuterClass.SignedPreKey =
        UsersApiOuterClass.SignedPreKey.newBuilder()
            .setPrekeyId(id)
            .setPublicKey(ByteString.copyFrom(keyPair.publicKey.serialize()))
            .setSignature(ByteString.copyFrom(signature))
            .build()

    private fun KyberPreKeyRecord.toProto(): UsersApiOuterClass.SignedKyberPreKey =
        UsersApiOuterClass.SignedKyberPreKey.newBuilder()
            .setPrekeyId(id)
            .setPublicKey(ByteString.copyFrom(keyPair.publicKey.serialize()))
            .setSignature(ByteString.copyFrom(signature))
            .build()

    companion object {
        const val INITIAL_ONE_TIME_PREKEYS = 100
        const val INITIAL_ONE_TIME_KYBER_PREKEYS = 100
        const val MIN_ONE_TIME_PREKEYS_THRESHOLD = 10
        const val INITIAL_SIGNED_PREKEY_ID = 1
        const val INITIAL_KYBER_PREKEY_ID = 1
        const val ROTATION_INTERVAL_MILLIS = 7L * 24 * 60 * 60 * 1000

        private const val KEY_NEXT_ONE_TIME_ID = "prekey_next_one_time_id"
        private const val KEY_NEXT_SIGNED_ID = "prekey_next_signed_id"
        private const val KEY_NEXT_KYBER_ID = "prekey_next_kyber_id"
        private const val KEY_CURRENT_SIGNED_ID = "prekey_current_signed_id"
        private const val KEY_CURRENT_LAST_RESORT_ID = "prekey_current_last_resort_id"
        private const val KEY_PQ_REGISTRATION_MARKER = "pq_registration_marker"
        private const val KEY_PENDING_REGISTER = "pending_register"
        private const val KEY_PENDING_REPLENISH = "pending_replenish"
        private const val KEY_PENDING_ROTATION = "pending_rotation"
    }
}
