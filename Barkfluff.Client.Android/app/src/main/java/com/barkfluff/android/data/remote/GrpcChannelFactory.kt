package com.barkfluff.android.data.remote

import com.barkfluff.android.data.local.AppDataStore
import io.grpc.ManagedChannel
import io.grpc.okhttp.OkHttpChannelBuilder
import java.util.concurrent.ConcurrentHashMap
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class GrpcChannelFactory @Inject constructor(
    private val dataStore: AppDataStore,
) {
    companion object {
        private const val NAVIGATOR_HOST = "navigator.barkfluff.com"
        private const val NAVIGATOR_PORT = 64646
    }

    private val channels = ConcurrentHashMap<String, ManagedChannel>()

    private val headerInterceptor = HeaderInterceptor(
        tokenProvider = { dataStore.getAccessTokenSync() },
        deviceIdProvider = { dataStore.getDeviceIdSync() },
    )

    fun getChannel(host: String, port: Int, tls: Boolean): ManagedChannel {
        val key = "$host:$port:$tls"
        return channels.getOrPut(key) {
            val builder = OkHttpChannelBuilder.forAddress(host, port)
                .intercept(headerInterceptor)
            if (!tls) builder.usePlaintext()
            builder.build()
        }
    }

    fun getNavigatorChannel(): ManagedChannel {
        val key = "$NAVIGATOR_HOST:$NAVIGATOR_PORT:false"
        return channels.getOrPut(key) {
            OkHttpChannelBuilder.forAddress(NAVIGATOR_HOST, NAVIGATOR_PORT)
                .usePlaintext()
                .build()
        }
    }

    fun shutdown() {
        channels.values.forEach { it.shutdownNow() }
        channels.clear()
    }
}
