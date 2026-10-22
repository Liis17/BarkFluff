package com.barkfluff.client.repository

import android.content.Context
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.crypto.BarkFluffSignalStore
import com.barkfluff.client.crypto.CryptoStoreScope
import com.barkfluff.client.crypto.PrekeyServerUpdateRequiredException
import com.barkfluff.client.grpc.GrpcApiTransport
import com.google.protobuf.ByteString
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.signal.libsignal.protocol.IdentityKeyPair
import org.signal.libsignal.protocol.ReusedBaseKeyException
import org.signal.libsignal.protocol.SessionBuilder
import org.signal.libsignal.protocol.SessionCipher
import org.signal.libsignal.protocol.SignalProtocolAddress
import org.signal.libsignal.protocol.ecc.ECKeyPair
import org.signal.libsignal.protocol.kem.KEMKeyPair
import org.signal.libsignal.protocol.kem.KEMKeyType
import org.signal.libsignal.protocol.message.CiphertextMessage
import org.signal.libsignal.protocol.message.PreKeySignalMessage
import org.signal.libsignal.protocol.message.SignalMessage
import org.signal.libsignal.protocol.state.KyberPreKeyRecord
import org.signal.libsignal.protocol.state.PreKeyRecord
import org.signal.libsignal.protocol.state.SignedPreKeyRecord
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class PqxdhHandshakeInstrumentedTest {

    @Test
    fun pqxdhWithEcOneTimeKeyPersistsSessionAndConsumesBothOneTimeKeys() {
        verifyHandshake(includeEcOneTimePrekey = true, useLastResortKyber = false)
    }

    @Test
    fun pqxdhWithoutEcOneTimeKeyUsesKyberOneTimeKey() {
        verifyHandshake(includeEcOneTimePrekey = false, useLastResortKyber = false)
    }

    @Test
    fun pqxdhWithoutEcOneTimeKeyUsesLastResortAndPersistsReplayTuple() {
        verifyHandshake(includeEcOneTimePrekey = false, useLastResortKyber = true)
    }

    @Test
    fun missingPqBundleRequiresServerUpdateAndTransactionFailureRollsBack() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val scope = newScope("missing-pq")
        val store = newStore(context, scope)
        try {
            val identity = IdentityKeyPair.generate()
            assertThrows(PrekeyServerUpdateRequiredException::class.java) {
                UsersApiOuterClass.PrekeyBundle.getDefaultInstance().toLibsignal("peer-device")
            }

            assertThrows(IllegalArgumentException::class.java) {
                store.transaction {
                    store.initialize(identity, 71)
                    throw IllegalArgumentException("simulated libsignal operation failure")
                }
            }
            assertFalse(BarkFluffSignalStore(context) { scope }.isInitialized())
        } finally {
            deleteScope(context, scope)
        }
    }

    @Test
    fun invalidKyberSignatureAndMalformedRequiredFieldsAreRejected() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val aliceScope = newScope("invalid-signature-alice")
        val alice = newStore(context, aliceScope)
        try {
            val bobIdentity = IdentityKeyPair.generate()
            val signedPair = ECKeyPair.generate()
            val signed = SignedPreKeyRecord(
                21,
                System.currentTimeMillis(),
                signedPair,
                bobIdentity.privateKey.calculateSignature(signedPair.publicKey.serialize()),
            )
            val kyberPair = KEMKeyPair.generate(KEMKeyType.KYBER_1024)
            val kyber = KyberPreKeyRecord(
                22,
                System.currentTimeMillis(),
                kyberPair,
                bobIdentity.privateKey.calculateSignature(kyberPair.publicKey.serialize()),
            )
            val bundle = createBundle("bob-device", bobIdentity, signed, null, kyber, lastResort = false)
            val corruptedSignature = bundle.kyberPrekey.signature.toByteArray().also {
                it[it.lastIndex] = (it.last().toInt() xor 1).toByte()
            }
            val invalidSignatureBundle = bundle.toBuilder()
                .setKyberPrekey(bundle.kyberPrekey.toBuilder().setSignature(ByteString.copyFrom(corruptedSignature)))
                .build()
            val address = SignalProtocolAddress("bob-device", 1)

            alice.transaction { alice.initialize(IdentityKeyPair.generate(), 303) }
            assertThrows(Exception::class.java) {
                alice.transaction { SessionBuilder(alice, address).process(invalidSignatureBundle.toLibsignal("bob-device")) }
            }
            assertFalse(alice.containsSession(address))
            assertThrows(InvalidPrekeyBundleException::class.java) {
                bundle.toBuilder().clearSignedPrekey().build().toLibsignal("bob-device")
            }
            assertThrows(InvalidPrekeyBundleException::class.java) {
                bundle.toBuilder()
                    .setKyberPrekey(bundle.kyberPrekey.toBuilder().setPublicKey(ByteString.copyFrom(byteArrayOf(1, 2, 3))))
                    .build()
                    .toLibsignal("bob-device")
            }
        } finally {
            deleteScope(context, aliceScope)
        }
    }

    @Test
    fun crossedInvitesMergeAndRepeatedCreateOrOrphanSessionDoesNotCreateDuplicateChat() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().context
        val aliceScope = newScope("crossed-alice")
        val bobScope = newScope("crossed-bob")
        val orphanScope = newScope("orphan-session")
        val alice = newStore(context, aliceScope)
        val bob = newStore(context, bobScope)
        val orphan = newStore(context, orphanScope)
        try {
            val aliceKeys = initializeDevice(alice, registrationId = 101, keyIdBase = 31)
            val bobKeys = initializeDevice(bob, registrationId = 202, keyIdBase = 41)
            val aliceBundle = createBundle("alice-device", aliceKeys.identity, aliceKeys.signed, aliceKeys.ec, aliceKeys.kyber, true)
            val bobBundle = createBundle("bob-device", bobKeys.identity, bobKeys.signed, bobKeys.ec, bobKeys.kyber, true)
            val aliceTransport = FakeSecretChatTransport(context, bobBundle, "alice")
            val bobTransport = FakeSecretChatTransport(context, aliceBundle, "bob")
            val aliceRepository = SecretChatRepository(aliceTransport, alice)
            val bobRepository = SecretChatRepository(bobTransport, bob)

            val aliceChat = aliceRepository.createSecretChat(202, "bob-device", "hello from Alice").getOrThrow()
            val bobChat = bobRepository.createSecretChat(101, "alice-device", "hello from Bob").getOrThrow()

            rotateFixtureKeys(alice, aliceKeys.identity, 61)
            rotateFixtureKeys(bob, bobKeys.identity, 71)

            val bobAccepted = bobRepository.acceptIncomingInvite(
                inviteId = "alice-crossed-invite",
                senderUserId = 101,
                senderDeviceId = "alice-device",
                initialEnvelope = aliceTransport.sentEnvelopes.single(),
            ).getOrThrow()
            val aliceAccepted = aliceRepository.acceptIncomingInvite(
                inviteId = "bob-crossed-invite",
                senderUserId = 202,
                senderDeviceId = "bob-device",
                initialEnvelope = bobTransport.sentEnvelopes.single(),
            ).getOrThrow()

            assertEquals(bobChat.id, bobAccepted.first.id)
            assertEquals(aliceChat.id, aliceAccepted.first.id)
            assertEquals(SecretChatRepository.SecretChatRole.BOTH, bobRepository.getChat(bobChat.id)?.role)
            assertEquals(SecretChatRepository.SecretChatRole.BOTH, aliceRepository.getChat(aliceChat.id)?.role)
            assertTrue(bobRepository.getChat(bobChat.id)!!.inviteIds.containsAll(setOf("bob-1", "alice-crossed-invite")))
            assertTrue(aliceRepository.getChat(aliceChat.id)!!.inviteIds.containsAll(setOf("alice-1", "bob-crossed-invite")))

            val duplicate = bobRepository.acceptIncomingInvite(
                inviteId = "alice-resend-new-server-id",
                senderUserId = 101,
                senderDeviceId = "alice-device",
                initialEnvelope = aliceTransport.sentEnvelopes.single(),
            ).getOrThrow()
            assertEquals(bobChat.id, duplicate.first.id)
            assertTrue(duplicate.first.inviteIds.contains("alice-resend-new-server-id"))
            assertTrue(bobTransport.acceptedInviteIds.contains("alice-resend-new-server-id"))

            val repeatedCreate = aliceRepository.createSecretChat(202, "bob-device", "ignored after first create").getOrThrow()
            assertEquals(aliceChat.id, repeatedCreate.id)
            assertEquals(1, aliceTransport.fetchCount)
            assertEquals(1, aliceTransport.sentEnvelopes.size)

            val orphanKeys = initializeDevice(orphan, registrationId = 303, keyIdBase = 51)
            val orphanAddress = SignalProtocolAddress("bob-device", 1)
            orphan.transaction {
                SessionBuilder(orphan, orphanAddress).process(bobBundle.toLibsignal("bob-device"))
                SessionCipher(orphan, orphanAddress).encrypt("orphaned session".toByteArray(StandardCharsets.UTF_8))
            }
            val orphanTransport = FakeSecretChatTransport(context, bobBundle, "orphan")
            val orphanRepository = SecretChatRepository(orphanTransport, orphan)
            assertTrue(orphanRepository.listChats().isEmpty())
            val recoveredChat = orphanRepository.createSecretChat(202, "bob-device", "new handshake").getOrThrow()
            assertEquals(1, orphanTransport.fetchCount)
            assertEquals(1, orphanTransport.sentEnvelopes.size)
            assertEquals("new handshake", orphanRepository.getChat(recoveredChat.id)?.initialPlaintext)
        } finally {
            deleteScope(context, aliceScope)
            deleteScope(context, bobScope)
            deleteScope(context, orphanScope)
        }
    }

    @Test
    fun failedInviteSendRetriesTheSameDurableEnvelope() = runBlocking {
        val context = InstrumentationRegistry.getInstrumentation().context
        val aliceScope = newScope("invite-retry-alice")
        val bobScope = newScope("invite-retry-bob")
        val alice = newStore(context, aliceScope)
        val bob = newStore(context, bobScope)
        try {
            initializeDevice(alice, registrationId = 101, keyIdBase = 81)
            val bobKeys = initializeDevice(bob, registrationId = 202, keyIdBase = 91)
            val bobBundle = createBundle("bob-device", bobKeys.identity, bobKeys.signed, bobKeys.ec, bobKeys.kyber, true)
            val transport = FakeSecretChatTransport(context, bobBundle, "retry").apply { failNextInvite = true }
            val repository = SecretChatRepository(transport, alice)

            assertTrue(repository.createSecretChat(202, "bob-device", "first text").isFailure)
            val pending = repository.listChats().single()
            assertTrue(pending.pendingSend)
            val firstEnvelope = pending.initialEnvelope!!.clone()

            val retried = repository.createSecretChat(202, "bob-device", "ignored retry text").getOrThrow()

            assertEquals(pending.id, retried.id)
            assertFalse(retried.pendingSend)
            assertEquals(1, transport.fetchCount)
            assertEquals(2, transport.sentEnvelopes.size)
            assertArrayEquals(firstEnvelope, transport.sentEnvelopes[0])
            assertArrayEquals(firstEnvelope, transport.sentEnvelopes[1])
        } finally {
            deleteScope(context, aliceScope)
            deleteScope(context, bobScope)
        }
    }

    private fun verifyHandshake(includeEcOneTimePrekey: Boolean, useLastResortKyber: Boolean) {
        val context = InstrumentationRegistry.getInstrumentation().context
        val aliceScope = newScope("alice")
        val bobScope = newScope("bob")
        val alice = newStore(context, aliceScope)
        val bob = newStore(context, bobScope)
        val peerDeviceId = "bob-device"
        val aliceAddress = SignalProtocolAddress(peerDeviceId, 1)
        val bobAddress = SignalProtocolAddress("alice-device", 1)

        try {
            val aliceIdentity = IdentityKeyPair.generate()
            val bobIdentity = IdentityKeyPair.generate()
            val signedPrekeyPair = ECKeyPair.generate()
            val signedPrekey = SignedPreKeyRecord(
                11,
                System.currentTimeMillis(),
                signedPrekeyPair,
                bobIdentity.privateKey.calculateSignature(signedPrekeyPair.publicKey.serialize()),
            )
            val ecOneTimePrekey = PreKeyRecord(12, ECKeyPair.generate())
            val kyberPair = KEMKeyPair.generate(KEMKeyType.KYBER_1024)
            val kyberSignature = bobIdentity.privateKey.calculateSignature(kyberPair.publicKey.serialize())
            val kyberPrekey = KyberPreKeyRecord(13, System.currentTimeMillis(), kyberPair, kyberSignature)

            alice.transaction { alice.initialize(aliceIdentity, 101) }
            bob.transaction {
                bob.initialize(bobIdentity, 202)
                bob.storeSignedPreKey(signedPrekey.id, signedPrekey)
                bob.storeKyberPreKey(kyberPrekey.id, kyberPrekey)
                if (useLastResortKyber) bob.addLastResortKyberPreKeyId(kyberPrekey.id)
                if (includeEcOneTimePrekey) bob.storePreKey(ecOneTimePrekey.id, ecOneTimePrekey)
            }

            val protoBundle = createBundle(
                peerDeviceId = peerDeviceId,
                identity = bobIdentity,
                signedPrekey = signedPrekey,
                ecOneTimePrekey = ecOneTimePrekey.takeIf { includeEcOneTimePrekey },
                kyberPrekey = kyberPrekey,
                lastResort = useLastResortKyber,
            )
            val firstCiphertext = alice.transaction {
                SessionBuilder(alice, aliceAddress).process(protoBundle.toLibsignal(peerDeviceId))
                SessionCipher(alice, aliceAddress).encrypt("hello PQXDH".toByteArray(StandardCharsets.UTF_8))
            }
            assertEquals(CiphertextMessage.PREKEY_TYPE, firstCiphertext.type)

            val corrupted = firstCiphertext.serialize().clone().also { it[it.lastIndex] = (it.last().toInt() xor 1).toByte() }
            assertThrows(Exception::class.java) {
                bob.transaction {
                    SessionCipher(bob, bobAddress).decrypt(PreKeySignalMessage(corrupted))
                }
            }
            assertTrue(bob.containsKyberPreKey(kyberPrekey.id))
            assertEquals(includeEcOneTimePrekey, bob.containsPreKey(ecOneTimePrekey.id))
            assertFalse(bob.containsSession(bobAddress))

            val bobAfterRestart = newStore(context, bobScope)
            val received = bobAfterRestart.transaction {
                SessionCipher(bobAfterRestart, bobAddress).decrypt(PreKeySignalMessage(firstCiphertext.serialize()))
            }
            assertArrayEquals("hello PQXDH".toByteArray(StandardCharsets.UTF_8), received)
            assertEquals(includeEcOneTimePrekey, bobAfterRestart.containsPreKey(ecOneTimePrekey.id))
            assertEquals(useLastResortKyber, bobAfterRestart.containsKyberPreKey(kyberPrekey.id))

            if (useLastResortKyber) {
                val secondAliceScope = newScope("alice-second-sender")
                try {
                    val secondAlice = newStore(context, secondAliceScope)
                    val secondAliceDeviceId = "alice-two-device"
                    val secondAliceIdentity = IdentityKeyPair.generate()
                    secondAlice.transaction { secondAlice.initialize(secondAliceIdentity, 404) }
                    val secondAliceAddress = SignalProtocolAddress(peerDeviceId, 1)
                    val secondBobAddress = SignalProtocolAddress(secondAliceDeviceId, 1)
                    val secondCiphertext = secondAlice.transaction {
                        SessionBuilder(secondAlice, secondAliceAddress).process(protoBundle.toLibsignal(peerDeviceId))
                        SessionCipher(secondAlice, secondAliceAddress).encrypt(
                            "second sender".toByteArray(StandardCharsets.UTF_8),
                        )
                    }
                    assertEquals(CiphertextMessage.PREKEY_TYPE, secondCiphertext.type)
                    val secondReceived = bobAfterRestart.transaction {
                        SessionCipher(bobAfterRestart, secondBobAddress)
                            .decrypt(PreKeySignalMessage(secondCiphertext.serialize()))
                    }
                    assertArrayEquals("second sender".toByteArray(StandardCharsets.UTF_8), secondReceived)
                    assertTrue(bobAfterRestart.containsKyberPreKey(kyberPrekey.id))
                } finally {
                    deleteScope(context, secondAliceScope)
                }

                val baseKey = ECKeyPair.generate().publicKey
                bobAfterRestart.transaction {
                    bobAfterRestart.markKyberPreKeyUsed(kyberPrekey.id, signedPrekey.id, baseKey)
                }
                assertThrows(ReusedBaseKeyException::class.java) {
                    bobAfterRestart.transaction {
                        bobAfterRestart.markKyberPreKeyUsed(kyberPrekey.id, signedPrekey.id, baseKey)
                    }
                }
                bobAfterRestart.forgetSecretChats()
                assertThrows(ReusedBaseKeyException::class.java) {
                    bobAfterRestart.transaction {
                        bobAfterRestart.markKyberPreKeyUsed(kyberPrekey.id, signedPrekey.id, baseKey)
                    }
                }
            }

            val reply = bobAfterRestart.transaction {
                SessionCipher(bobAfterRestart, bobAddress).encrypt("reply".toByteArray(StandardCharsets.UTF_8))
            }
            assertEquals(CiphertextMessage.WHISPER_TYPE, reply.type)
            val aliceAfterRestart = newStore(context, aliceScope)
            val replyPlaintext = aliceAfterRestart.transaction {
                SessionCipher(aliceAfterRestart, aliceAddress).decrypt(SignalMessage(reply.serialize()))
            }
            assertArrayEquals("reply".toByteArray(StandardCharsets.UTF_8), replyPlaintext)
        } finally {
            deleteScope(context, aliceScope)
            deleteScope(context, bobScope)
        }
    }

    private fun createBundle(
        peerDeviceId: String,
        identity: IdentityKeyPair,
        signedPrekey: SignedPreKeyRecord,
        ecOneTimePrekey: PreKeyRecord?,
        kyberPrekey: KyberPreKeyRecord,
        lastResort: Boolean,
    ): UsersApiOuterClass.PrekeyBundle {
        val signed = UsersApiOuterClass.SignedPreKey.newBuilder()
            .setPrekeyId(signedPrekey.id)
            .setPublicKey(ByteString.copyFrom(signedPrekey.keyPair.publicKey.serialize()))
            .setSignature(ByteString.copyFrom(signedPrekey.signature))
            .build()
        val kyber = UsersApiOuterClass.SignedKyberPreKey.newBuilder()
            .setPrekeyId(kyberPrekey.id)
            .setPublicKey(ByteString.copyFrom(kyberPrekey.keyPair.publicKey.serialize()))
            .setSignature(ByteString.copyFrom(kyberPrekey.signature))
            .build()
        val builder = UsersApiOuterClass.PrekeyBundle.newBuilder()
            .setDeviceId(peerDeviceId)
            .setRegistrationId(202)
            .setIdentityPubkey(ByteString.copyFrom(identity.publicKey.serialize()))
            .setSignedPrekey(signed)
            .setKyberPrekey(kyber)
            .setKyberPrekeyIsLastResort(lastResort)
        if (ecOneTimePrekey != null) {
            builder.setHasOneTimePrekey(true)
                .setOneTimePrekey(
                    UsersApiOuterClass.OneTimePreKey.newBuilder()
                        .setPrekeyId(ecOneTimePrekey.id)
                        .setPublicKey(ByteString.copyFrom(ecOneTimePrekey.keyPair.publicKey.serialize()))
                        .build(),
                )
        }
        return builder.build()
    }

    private fun initializeDevice(
        store: BarkFluffSignalStore,
        registrationId: Int,
        keyIdBase: Int,
    ): DeviceKeys {
        val identity = IdentityKeyPair.generate()
        val signedPair = ECKeyPair.generate()
        val signed = SignedPreKeyRecord(
            keyIdBase,
            System.currentTimeMillis(),
            signedPair,
            identity.privateKey.calculateSignature(signedPair.publicKey.serialize()),
        )
        val ec = PreKeyRecord(keyIdBase + 1, ECKeyPair.generate())
        val kyberPair = KEMKeyPair.generate(KEMKeyType.KYBER_1024)
        val kyber = KyberPreKeyRecord(
            keyIdBase + 2,
            System.currentTimeMillis(),
            kyberPair,
            identity.privateKey.calculateSignature(kyberPair.publicKey.serialize()),
        )
        store.transaction {
            store.initialize(identity, registrationId)
            store.storeSignedPreKey(signed.id, signed)
            store.storePreKey(ec.id, ec)
            store.storeKyberPreKey(kyber.id, kyber)
            store.addLastResortKyberPreKeyId(kyber.id)
        }
        return DeviceKeys(identity, signed, ec, kyber)
    }

    private fun rotateFixtureKeys(store: BarkFluffSignalStore, identity: IdentityKeyPair, keyIdBase: Int) {
        val signedPair = ECKeyPair.generate()
        val signed = SignedPreKeyRecord(
            keyIdBase,
            System.currentTimeMillis(),
            signedPair,
            identity.privateKey.calculateSignature(signedPair.publicKey.serialize()),
        )
        val kyberPair = KEMKeyPair.generate(KEMKeyType.KYBER_1024)
        val kyber = KyberPreKeyRecord(
            keyIdBase + 1,
            System.currentTimeMillis(),
            kyberPair,
            identity.privateKey.calculateSignature(kyberPair.publicKey.serialize()),
        )
        store.transaction {
            store.storeSignedPreKey(signed.id, signed)
            store.storeKyberPreKey(kyber.id, kyber)
            store.addLastResortKyberPreKeyId(kyber.id)
        }
    }

    private fun newStore(context: Context, scope: CryptoStoreScope) = BarkFluffSignalStore(context) { scope }

    private data class DeviceKeys(
        val identity: IdentityKeyPair,
        val signed: SignedPreKeyRecord,
        val ec: PreKeyRecord,
        val kyber: KyberPreKeyRecord,
    )

    private class FakeSecretChatTransport(
        context: Context,
        private val bundle: UsersApiOuterClass.PrekeyBundle,
        private val invitePrefix: String,
    ) : GrpcApiTransport(context) {
        val sentEnvelopes = mutableListOf<ByteArray>()
        val acceptedInviteIds = mutableListOf<String>()
        var failNextInvite = false
        var fetchCount = 0
            private set

        override suspend fun fetchPrekeyBundle(
            userId: Long,
            deviceId: String,
        ): Result<UsersApiOuterClass.FetchPrekeyBundleResponse> {
            fetchCount++
            return Result.success(
                UsersApiOuterClass.FetchPrekeyBundleResponse.newBuilder().setBundle(bundle).build(),
            )
        }

        override suspend fun sendSecretChatInvite(
            recipientUserId: Long,
            recipientDeviceId: String,
            initialEnvelope: ByteArray,
        ): Result<GrpcApiTransport.SecretInviteSent> {
            sentEnvelopes += initialEnvelope.clone()
            if (failNextInvite) {
                failNextInvite = false
                return Result.failure(IllegalStateException("simulated lost invite acknowledgement"))
            }
            return Result.success(GrpcApiTransport.SecretInviteSent("$invitePrefix-${sentEnvelopes.size}", 0))
        }

        override suspend fun acceptSecretChatInvite(
            inviteId: String,
            responseEnvelope: ByteArray,
        ): Result<Unit> {
            acceptedInviteIds += inviteId
            return Result.success(Unit)
        }
    }

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
}
