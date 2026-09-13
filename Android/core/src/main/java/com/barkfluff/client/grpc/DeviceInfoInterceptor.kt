package com.barkfluff.client.grpc

import android.content.Context
import android.util.Base64
import com.barkfluff.client.data.GlobalParam
import io.grpc.*

/**
 * Interceptor для добавления заголовков устройства к gRPC запросам
 * Аналог AddInterceptor в WebApiClientManager
 */
class DeviceInfoInterceptor(
    private val context: Context
) : ClientInterceptor {

    private val globalParam = GlobalParam(context)

    override fun <ReqT, RespT> interceptCall(
        method: MethodDescriptor<ReqT, RespT>,
        callOptions: CallOptions,
        next: Channel
    ): ClientCall<ReqT, RespT> {
        return object : ForwardingClientCall.SimpleForwardingClientCall<ReqT, RespT>(
            next.newCall(method, callOptions)
        ) {
            override fun start(responseListener: Listener<RespT>, headers: Metadata) {
                // Добавляем заголовки устройства
                headers.put(key("x-device-id"), toBase64(globalParam.deviceId))
                headers.put(key("x-device-name"), toBase64(GlobalParam.getDeviceName()))
                headers.put(key("x-os-name"), toBase64(GlobalParam.getOsVersion()))
                headers.put(key("x-app-name"), toBase64(GlobalParam.getAppName()))
                headers.put(key("x-app-version"), toBase64(GlobalParam.getAppVersion(context)))
                headers.put(key("x-ip-address"), toBase64(globalParam.ipAddress))

                super.start(responseListener, headers)
            }
        }
    }

    private fun toBase64(value: String): String {
        return Base64.encodeToString(value.toByteArray(Charsets.UTF_8), Base64.NO_WRAP)
    }

    private fun key(name: String): Metadata.Key<String> {
        return Metadata.Key.of(name, Metadata.ASCII_STRING_MARSHALLER)
    }
}
