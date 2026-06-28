package com.barkfluff.client.grpc

import android.content.Context
import android.util.Log
import com.barkfluff.client.data.GlobalParam
import io.grpc.*

/**
 * Interceptor для добавления access токена к gRPC запросам.
 * Использует заголовок x-auth-token (аналогично WPF/Mac клиентам и серверному JwtClientInterceptor).
 */
class AuthInterceptor(
    private val context: Context
) : ClientInterceptor {

    companion object {
        private val AUTH_TOKEN_KEY: Metadata.Key<String> =
            Metadata.Key.of("x-auth-token", Metadata.ASCII_STRING_MARSHALLER)
        private const val TAG = "AuthInterceptor"
    }

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
                val accessToken = globalParam.accessToken

                if (!accessToken.isNullOrBlank()) {
                    headers.put(AUTH_TOKEN_KEY, accessToken)
                } else {
                    Log.w(TAG, "interceptCall: НЕТ токена для ${method.serviceName}/${method.bareMethodName}")
                }

                super.start(responseListener, headers)
            }
        }
    }
}
