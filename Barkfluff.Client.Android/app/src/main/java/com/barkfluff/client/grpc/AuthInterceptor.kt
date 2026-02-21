package com.barkfluff.client.grpc

import android.content.Context
import android.util.Base64
import com.barkfluff.client.data.GlobalParam
import io.grpc.*

/**
 * Interceptor для добавления access токена к gRPC запросам
 */
class AuthInterceptor(
    private val context: Context,
    private val grpcManager: GrpcManager
) : ClientInterceptor {

    companion object {
        private val AUTHORIZATION_KEY: Metadata.Key<String> =
            Metadata.Key.of("authorization", Metadata.ASCII_STRING_MARSHALLER)
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
                // Добавляем access токен к запросу
                val accessToken = globalParam.accessToken
                if (!accessToken.isNullOrBlank()) {
                    headers.put(AUTHORIZATION_KEY, "Bearer $accessToken")
                }

                super.start(responseListener, headers)
            }
        }
    }
}
