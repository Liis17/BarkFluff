package com.barkfluff.client.crypto

import android.content.Context
import android.util.Log
import barkfluff.users.UsersApiOuterClass
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.grpc.GrpcManager
import com.google.protobuf.ByteString
import org.signal.libsignal.protocol.state.PreKeyRecord
import org.signal.libsignal.protocol.state.SignedPreKeyRecord

/**
 * Регистрация и пополнение prekey-bundle устройства. Вызывается из SplashActivity / LoginActivity
 * (после успешного логина и инициализации gRPC-клиентов). Идемпотентно — если устройство уже
 * зарегистрировано, ничего не делает.
 */
object E2EBootstrap {

    private const val TAG = "E2EBootstrap"

    /** Сгенерировать identity+prekeys и зарегистрировать на сервере (если ещё не сделано). */
    suspend fun ensurePrekeyBundleRegistered(context: Context): Boolean {
        val app = context.applicationContext as BarkFluffApplication
        val manager = app.prekeyManager
        if (manager.isRegistered) return true

        return try {
            val bundle = manager.generateBundle()
            val signedProto = bundle.signedPreKey.toProto()
            val oneTimeProtos = bundle.oneTimePreKeys.map { it.toProto() }

            // Сериализация identity-key: используем .serialize() (33 байта, с type-prefix)
            val identityPubBytes = bundle.identityKeyPair.publicKey.serialize()

            val result = app.grpcManager.registerPrekeyBundle(
                registrationId = bundle.registrationId,
                identityPubkey = identityPubBytes,
                signedPreKey = signedProto,
                oneTimePreKeys = oneTimeProtos
            )
            if (result.isFailure) {
                Log.w(TAG, "RegisterPrekeyBundle failed: ${result.exceptionOrNull()?.message}")
                return false
            }
            manager.persistBundle(bundle)
            Log.i(TAG, "Prekey-bundle зарегистрирован: ${bundle.oneTimePreKeys.size} one-time prekeys")
            true
        } catch (e: Exception) {
            Log.e(TAG, "ensurePrekeyBundleRegistered failed", e)
            false
        }
    }

    /**
     * Если на сервере осталось мало one-time prekeys — догенерировать и отправить.
     * Вызывать после успешного FetchPrekeyBundle если remaining_one_time_prekeys мало.
     */
    suspend fun replenishIfNeeded(context: Context, currentRemaining: Int) {
        val app = context.applicationContext as BarkFluffApplication
        val manager = app.prekeyManager
        if (currentRemaining >= PrekeyManager.MIN_ONE_TIME_PREKEYS_THRESHOLD) return
        try {
            val refill = PrekeyManager.INITIAL_ONE_TIME_PREKEYS - currentRemaining
            val newKeys = manager.generateAdditionalOneTimePrekeys(refill)
            val protos = newKeys.map { it.toProto() }
            val r = app.grpcManager.replenishOneTimePrekeys(protos)
            if (r.isSuccess) Log.i(TAG, "Replenished one-time prekeys: total=${r.getOrNull()}")
        } catch (e: Exception) {
            Log.w(TAG, "replenishIfNeeded failed", e)
        }
    }
}

private fun SignedPreKeyRecord.toProto(): UsersApiOuterClass.SignedPreKey =
    UsersApiOuterClass.SignedPreKey.newBuilder()
        .setPrekeyId(id)
        .setPublicKey(ByteString.copyFrom(keyPair.publicKey.serialize()))
        .setSignature(ByteString.copyFrom(signature))
        .build()

private fun PreKeyRecord.toProto(): UsersApiOuterClass.OneTimePreKey =
    UsersApiOuterClass.OneTimePreKey.newBuilder()
        .setPrekeyId(id)
        .setPublicKey(ByteString.copyFrom(keyPair.publicKey.serialize()))
        .build()
