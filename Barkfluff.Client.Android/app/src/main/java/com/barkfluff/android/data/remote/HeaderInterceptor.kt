package com.barkfluff.android.data.remote

import io.grpc.CallOptions
import io.grpc.Channel
import io.grpc.ClientCall
import io.grpc.ClientInterceptor
import io.grpc.ForwardingClientCall
import io.grpc.Metadata
import io.grpc.MethodDescriptor

class HeaderInterceptor(
    private val tokenProvider: () -> String?,
    private val deviceIdProvider: () -> String,
    private val appVersion: String = "1.0",
) : ClientInterceptor {

    override fun <ReqT, RespT> interceptCall(
        method: MethodDescriptor<ReqT, RespT>,
        callOptions: CallOptions,
        next: Channel,
    ): ClientCall<ReqT, RespT> {
        return object : ForwardingClientCall.SimpleForwardingClientCall<ReqT, RespT>(
            next.newCall(method, callOptions)
        ) {
            override fun start(responseListener: Listener<RespT>, headers: Metadata) {
                tokenProvider()?.takeIf { it.isNotEmpty() }?.let { token ->
                    headers.put(AUTH_TOKEN_KEY, token)
                }
                val deviceId = deviceIdProvider()
                if (deviceId.isNotEmpty()) headers.put(DEVICE_ID_KEY, deviceId)
                headers.put(DEVICE_NAME_KEY, "Android Device")
                headers.put(APP_NAME_KEY, "BarkFluff Android")
                headers.put(APP_VERSION_KEY, appVersion)
                headers.put(OS_NAME_KEY, "Android")
                headers.put(IP_ADDRESS_KEY, "0.0.0.0")
                super.start(responseListener, headers)
            }
        }
    }

    companion object {
        val AUTH_TOKEN_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-auth-token", Metadata.ASCII_STRING_MARSHALLER)
        val DEVICE_ID_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-device-id", Metadata.ASCII_STRING_MARSHALLER)
        val DEVICE_NAME_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-device-name", Metadata.ASCII_STRING_MARSHALLER)
        val APP_NAME_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-app-name", Metadata.ASCII_STRING_MARSHALLER)
        val APP_VERSION_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-app-version", Metadata.ASCII_STRING_MARSHALLER)
        val OS_NAME_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-os-name", Metadata.ASCII_STRING_MARSHALLER)
        val IP_ADDRESS_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-ip-address", Metadata.ASCII_STRING_MARSHALLER)
        val ERROR_CODE_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER)
    }
}
