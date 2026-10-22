package com.barkfluff.client.crypto

import android.content.Context
import android.util.Base64
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKeys
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.domain.gateway.PrekeyGateway
import io.grpc.Status
import io.grpc.StatusRuntimeException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.signal.libsignal.protocol.IdentityKey
import org.signal.libsignal.protocol.IdentityKeyPair
import org.signal.libsignal.protocol.ecc.ECKeyPair
import org.signal.libsignal.protocol.state.PreKeyRecord
import org.signal.libsignal.protocol.state.SessionRecord
import org.signal.libsignal.protocol.state.SignedPreKeyRecord
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.CopyOnWriteArrayList

@RunWith(AndroidJUnit4::class)
class PrekeyManagerInstrumentedTest {

    @Test
    fun registrationRetryReusesDurableBytesAndSetsMarkerOnlyAfterConfirmation() = runBlocking {
        val context = testContext()
        val scope = newScope("registration-retry")
        val store = newStore(context, scope)
        val gateway = InMemoryPrekeyGateway().apply { failNextRegistration = true }
        try {
            val first = runCatching { PrekeyManager(store).synchronize(gateway) }
            assertTrue(first.isFailure)
            assertFalse(PrekeyManager(store).isRegistered)
            val firstBytes = gateway.registrationRequests.single()
            assertNotNull(store.readManagerValue("pending_register"))

            val reopened = newStore(context, scope)
            val manager = PrekeyManager(reopened)
            manager.synchronize(gateway)

            assertTrue(manager.isRegistered)
            assertEquals(2, gateway.registrationRequests.size)
            assertArrayEquals(firstBytes, gateway.registrationRequests.last())
            assertNull(reopened.readManagerValue("pending_register"))
        } finally {
            deleteScope(context, scope)
        }
    }

    @Test
    fun replenishmentRetryReusesKeysAfterLostAcknowledgement() = runBlocking {
        val context = testContext()
        val scope = newScope("replenishment-retry")
        val store = newStore(context, scope)
        val gateway = InMemoryPrekeyGateway()
        try {
            val manager = PrekeyManager(store)
            manager.synchronize(gateway)
            assertTrue(manager.isRegistered)
            gateway.consumeAllOneTimeKeys()
            gateway.failNextReplenishmentAfterApply = true

            assertTrue(runCatching { manager.synchronize(gateway) }.isFailure)
            val firstBatch = gateway.replenishmentRequests.single()
            assertNotNull(store.readManagerValue("pending_replenish"))

            val reopened = newStore(context, scope)
            PrekeyManager(reopened).synchronize(gateway)

            assertEquals(2, gateway.replenishmentRequests.size)
            assertArrayEquals(firstBatch, gateway.replenishmentRequests.last())
            assertEquals(PrekeyManager.INITIAL_ONE_TIME_PREKEYS, gateway.remainingEcPrekeys)
            assertEquals(PrekeyManager.INITIAL_ONE_TIME_KYBER_PREKEYS, gateway.remainingKyberPrekeys)
            assertNull(reopened.readManagerValue("pending_replenish"))
        } finally {
            deleteScope(context, scope)
        }
    }

    @Test
    fun oldServerStopsBeforeGeneratingOrPublishingKeys() = runBlocking {
        val context = testContext()
        val scope = newScope("old-server")
        val store = newStore(context, scope)
        val gateway = InMemoryPrekeyGateway().apply {
            statusFailure = StatusRuntimeException(Status.UNIMPLEMENTED)
        }
        try {
            val result = runCatching { PrekeyManager(store).synchronize(gateway) }
            assertTrue(result.exceptionOrNull() is PrekeyServerUpdateRequiredException)
            assertFalse(store.isInitialized())
            assertTrue(gateway.registrationRequests.isEmpty())
        } finally {
            deleteScope(context, scope)
        }
    }

    @Test
    fun concurrentBootstrapIsSerialized() = runBlocking {
        val context = testContext()
        val scope = newScope("parallel-bootstrap")
        val store = newStore(context, scope)
        val gateway = InMemoryPrekeyGateway().apply { statusDelayMillis = 10 }
        try {
            val manager = PrekeyManager(store)
            awaitAll(
                async { manager.synchronize(gateway) },
                async { manager.synchronize(gateway) },
            )
            assertEquals(1, gateway.registrationRequests.size)
            assertEquals(1, gateway.maxConcurrentStatusRequests)
        } finally {
            deleteScope(context, scope)
        }
    }

    @Test
    fun weeklyRotationRetainsRetiredPrivateKeysForDelayedBundles() = runBlocking {
        val context = testContext()
        val scope = newScope("weekly-rotation")
        val store = newStore(context, scope)
        val gateway = InMemoryPrekeyGateway()
        try {
            val manager = PrekeyManager(store)
            manager.synchronize(gateway)
            val oldSignedId = store.readManagerValue("prekey_current_signed_id")!!.toInt()
            val oldKyberId = store.readManagerValue("prekey_current_last_resort_id")!!.toInt()
            val oldSigned = store.loadSignedPreKey(oldSignedId)
            val expiredSigned = SignedPreKeyRecord(
                oldSigned.id,
                System.currentTimeMillis() - PrekeyManager.ROTATION_INTERVAL_MILLIS - 1,
                oldSigned.keyPair,
                oldSigned.signature,
            )
            store.storeSignedPreKey(expiredSigned.id, expiredSigned)

            manager.synchronize(gateway)

            assertEquals(1, gateway.rotationRequests.size)
            assertTrue(store.containsSignedPreKey(oldSignedId))
            assertTrue(store.containsKyberPreKey(oldKyberId))
            assertTrue(store.readManagerValue("prekey_current_signed_id")!!.toInt() > oldSignedId)
            assertTrue(store.readManagerValue("prekey_current_last_resort_id")!!.toInt() > oldKyberId)
            assertTrue(manager.isRegistered)
        } finally {
            deleteScope(context, scope)
        }
    }

    @Test
    fun logoutDuringPublishInvalidatesOldScopeAndNextDeviceStartsEmpty() = runBlocking {
        val context = testContext()
        val oldScope = newScope("logout-old")
        val newScope = newScope("logout-new")
        var activeScope = oldScope
        val store = BarkFluffSignalStore(context) { activeScope }
        val gateway = InMemoryPrekeyGateway().apply {
            registerStarted = CompletableDeferred()
            registerGate = CompletableDeferred()
        }
        try {
            val pending = async { runCatching { PrekeyManager(store).synchronize(gateway) } }
            gateway.registerStarted!!.await()
            store.reset()
            activeScope = newScope
            gateway.registerGate!!.complete(Unit)

            assertTrue(pending.await().isFailure)
            assertFalse(store.isInitialized())
            assertFalse(newStore(context, oldScope).isInitialized())
            assertFalse(newStore(context, newScope).isInitialized())
        } finally {
            gateway.registerGate?.complete(Unit)
            deleteScope(context, oldScope)
            deleteScope(context, newScope)
        }
    }

    @Test
    fun legacyKeysSessionsAndMetadataMigrateOnlyForMatchingServerIdentityAndRegistration() {
        val context = testContext()
        val matchingScope = newScope("legacy-match")
        val mismatchScope = newScope("legacy-mismatch")
        val legacyStore = legacyEncryptedPreferences(context, "barkfluff_signal_store")
        val legacyChats = legacyEncryptedPreferences(context, "barkfluff_secret_chats")
        val oldManagerState = context.getSharedPreferences("barkfluff_prekey_state", Context.MODE_PRIVATE)
        val identity = IdentityKeyPair.generate()
        val peerIdentity = IdentityKeyPair.generate()
        val signedPair = ECKeyPair.generate()
        val signedPrekey = SignedPreKeyRecord(
            51,
            System.currentTimeMillis(),
            signedPair,
            identity.privateKey.calculateSignature(signedPair.publicKey.serialize()),
        )
        val ecPrekey = PreKeyRecord(52, ECKeyPair.generate())
        val addressName = "legacy-peer"
        val session = SessionRecord()
        val chatId = "legacy-chat-id"
        val legacyChat = "900|legacy-device|invite-1|INITIATOR|1"
        try {
            legacyStore.edit().clear().commit()
            legacyChats.edit().clear().commit()
            oldManagerState.edit().clear().commit()
            legacyStore.edit()
                .putString("self::identity_key", encode(identity.serialize()))
                .putInt("self::registration_id", 303)
                .putString("signed_prekey::${signedPrekey.id}", encode(signedPrekey.serialize()))
                .putString("prekey::${ecPrekey.id}", encode(ecPrekey.serialize()))
                .putString("identity::$addressName::1", encode(peerIdentity.publicKey.serialize()))
                .putString("session::$addressName::1", encode(session.serialize()))
                .commit()
            oldManagerState.edit().putInt("prekey_next_one_time_id", 53).commit()
            legacyChats.edit().putString(chatId, legacyChat).commit()

            val migrated = newStore(context, matchingScope)
            assertTrue(migrated.migrateLegacyIfMatching(identity.publicKey.serialize(), 303))
            assertArrayEquals(identity.serialize(), migrated.getIdentityKeyPair().serialize())
            assertEquals(303, migrated.getLocalRegistrationId())
            assertTrue(migrated.containsSignedPreKey(signedPrekey.id))
            assertTrue(migrated.containsPreKey(ecPrekey.id))
            assertTrue(migrated.containsSession(org.signal.libsignal.protocol.SignalProtocolAddress(addressName, 1)))
            assertEquals(
                IdentityKey(peerIdentity.publicKey.serialize(), 0),
                migrated.getIdentity(org.signal.libsignal.protocol.SignalProtocolAddress(addressName, 1)),
            )
            assertEquals("53", migrated.readManagerValue("prekey_next_one_time_id"))
            assertEquals(legacyChat, migrated.readMetadata("secret_chat::$chatId"))

            val mismatch = newStore(context, mismatchScope)
            assertFalse(mismatch.migrateLegacyIfMatching(IdentityKeyPair.generate().publicKey.serialize(), 303))
            assertFalse(mismatch.isInitialized())
        } finally {
            legacyStore.edit().clear().commit()
            legacyChats.edit().clear().commit()
            oldManagerState.edit().clear().commit()
            deleteScope(context, matchingScope)
            deleteScope(context, mismatchScope)
        }
    }

    private class InMemoryPrekeyGateway : PrekeyGateway {
        val registrationRequests = CopyOnWriteArrayList<ByteArray>()
        val replenishmentRequests = CopyOnWriteArrayList<ByteArray>()
        val rotationRequests = CopyOnWriteArrayList<ByteArray>()
        var failNextRegistration = false
        var failNextReplenishmentAfterApply = false
        var statusFailure: Throwable? = null
        var statusDelayMillis = 0L
        var registerStarted: CompletableDeferred<Unit>? = null
        var registerGate: CompletableDeferred<Unit>? = null
        @Volatile var maxConcurrentStatusRequests = 0
            private set
        @Volatile private var activeStatusRequests = 0
        private var registeredRequest: ByteArray? = null
        private var registration: UsersApiOuterClass.RegisterPrekeyBundleRequest? = null
        private var rotatedLastResort: UsersApiOuterClass.SignedKyberPreKey? = null
        private val availableEcPrekeys = mutableSetOf<Int>()
        private val availableKyberPrekeys = mutableSetOf<Int>()

        val remainingEcPrekeys: Int get() = availableEcPrekeys.size
        val remainingKyberPrekeys: Int get() = availableKyberPrekeys.size

        override suspend fun register(request: UsersApiOuterClass.RegisterPrekeyBundleRequest): Result<Unit> {
            val bytes = request.toByteArray()
            registrationRequests += bytes
            registerStarted?.complete(Unit)
            registerGate?.await()
            if (failNextRegistration) {
                failNextRegistration = false
                return Result.failure(IllegalStateException("simulated lost register acknowledgement"))
            }
            val prior = registeredRequest
            if (prior != null && !prior.contentEquals(bytes)) {
                return Result.failure(IllegalStateException("conflicting registration retry"))
            }
            if (prior == null) {
                registeredRequest = bytes
                registration = request
                availableEcPrekeys += request.oneTimePrekeysList.map { it.prekeyId }
                availableKyberPrekeys += request.oneTimeKyberPrekeysList.map { it.prekeyId }
            }
            return Result.success(Unit)
        }

        override suspend fun replenish(
            request: UsersApiOuterClass.ReplenishOneTimePrekeysRequest,
        ): Result<UsersApiOuterClass.ReplenishOneTimePrekeysResponse> {
            replenishmentRequests += request.toByteArray()
            if (registeredRequest == null) return Result.failure(IllegalStateException("not registered"))
            availableEcPrekeys += request.prekeysList.map { it.prekeyId }
            availableKyberPrekeys += request.kyberPrekeysList.map { it.prekeyId }
            val response = UsersApiOuterClass.ReplenishOneTimePrekeysResponse.newBuilder()
                .setTotalOneTimePrekeys(availableEcPrekeys.size)
                .setTotalOneTimeKyberPrekeys(availableKyberPrekeys.size)
                .build()
            if (failNextReplenishmentAfterApply) {
                failNextReplenishmentAfterApply = false
                return Result.failure(IllegalStateException("simulated lost replenish acknowledgement"))
            }
            return Result.success(response)
        }

        override suspend fun rotate(request: UsersApiOuterClass.RotateSignedPrekeyRequest): Result<Unit> {
            if (registeredRequest == null) return Result.failure(IllegalStateException("not registered"))
            rotationRequests += request.toByteArray()
            rotatedLastResort = request.lastResortKyberPrekey
            return Result.success(Unit)
        }

        override suspend fun ownStatus(): Result<UsersApiOuterClass.GetOwnPrekeyStatusResponse> {
            synchronized(this) {
                activeStatusRequests++
                maxConcurrentStatusRequests = maxOf(maxConcurrentStatusRequests, activeStatusRequests)
            }
            try {
                if (statusDelayMillis > 0) delay(statusDelayMillis)
                statusFailure?.let { return Result.failure(it) }
                val request = registration ?: return Result.success(
                    UsersApiOuterClass.GetOwnPrekeyStatusResponse.getDefaultInstance(),
                )
                return Result.success(
                    UsersApiOuterClass.GetOwnPrekeyStatusResponse.newBuilder()
                        .setHasBundle(true)
                        .setSupportsPqxdh(true)
                        .setIdentityPubkey(request.identityPubkey)
                        .setRegistrationId(request.registrationId)
                        .setLastResortKyberPrekey(rotatedLastResort ?: request.lastResortKyberPrekey)
                        .setRemainingOneTimePrekeys(availableEcPrekeys.size)
                        .setRemainingOneTimeKyberPrekeys(availableKyberPrekeys.size)
                        .build(),
                )
            } finally {
                synchronized(this) { activeStatusRequests-- }
            }
        }

        fun consumeAllOneTimeKeys() {
            availableEcPrekeys.clear()
            availableKyberPrekeys.clear()
        }
    }

    private fun testContext() = InstrumentationRegistry.getInstrumentation().context

    private fun newStore(context: Context, scope: CryptoStoreScope) = BarkFluffSignalStore(context) { scope }

    private fun newScope(label: String) = CryptoStoreScope(
        server = "instrumentation://$label",
        userId = UUID.randomUUID().mostSignificantBits.ushr(1).coerceAtLeast(1),
        deviceId = UUID.randomUUID().toString(),
    )

    private fun deleteScope(context: Context, scope: CryptoStoreScope) {
        val hash = MessageDigest.getInstance("SHA-256")
            .digest(scope.scopeId.toByteArray(StandardCharsets.UTF_8))
            .joinToString("") { "%02x".format(it) }
        context.deleteSharedPreferences("barkfluff_crypto_$hash")
    }

    private fun legacyEncryptedPreferences(context: Context, fileName: String) =
        EncryptedSharedPreferences.create(
            fileName,
            MasterKeys.getOrCreate(MasterKeys.AES256_GCM_SPEC),
            context,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )

    private fun encode(bytes: ByteArray): String = Base64.encodeToString(bytes, Base64.NO_WRAP)
}
