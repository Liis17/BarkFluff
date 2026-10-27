package com.barkfluff.client.crypto

import android.content.Context
import android.util.Log
import com.barkfluff.client.domain.gateway.PrekeyGateway
import com.barkfluff.client.data.GlobalParam
import dagger.hilt.EntryPoint
import dagger.hilt.InstallIn
import dagger.hilt.android.EntryPointAccessors
import dagger.hilt.components.SingletonComponent
import kotlinx.coroutines.CancellationException

/** Reconciles this authenticated device's durable PQXDH keys with the Users service. */
object E2EBootstrap {

    @EntryPoint
    @InstallIn(SingletonComponent::class)
    interface Dependencies {
        fun prekeyManager(): PrekeyManager
        fun prekeyGateway(): PrekeyGateway
    }

    private const val TAG = "E2EBootstrap"

    suspend fun ensurePrekeyBundleRegistered(context: Context): Boolean {
        if (!GlobalParam(context).secretChatsEnabled) return false
        val dependencies = EntryPointAccessors.fromApplication(
            context.applicationContext,
            Dependencies::class.java,
        )
        return try {
            dependencies.prekeyManager().synchronize(dependencies.prekeyGateway())
            true
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "PQXDH key synchronization failed: ${e.message}")
            false
        }
    }
}
