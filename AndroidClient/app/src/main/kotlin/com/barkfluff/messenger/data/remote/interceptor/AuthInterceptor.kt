package com.barkfluff.messenger.data.remote.interceptor

import com.barkfluff.messenger.data.local.SessionManager
import io.grpc.CallOptions
import io.grpc.Channel
import io.grpc.ClientCall
import io.grpc.ClientInterceptor
import io.grpc.ForwardingClientCall
import io.grpc.Metadata
import io.grpc.MethodDescriptor
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import javax.inject.Inject

class AuthInterceptor @Inject constructor(
    private val sessionManager: SessionManager
) : ClientInterceptor {

    override fun <ReqT, RespT> interceptCall(
        method: MethodDescriptor<ReqT, RespT>,
        callOptions: CallOptions,
        next: Channel
    ): ClientCall<ReqT, RespT> {
        return object : ForwardingClientCall.SimpleForwardingClientCall<ReqT, RespT>(
            next.newCall(method, callOptions)
        ) {
            override fun start(responseListener: Listener<RespT>, headers: Metadata) {
                runBlocking {
                    val token = sessionManager.authToken.first()
                    token?.let {
                        headers.put(
                            Metadata.Key.of("Authorization", Metadata.ASCII_STRING_MARSHALLER),
                            "Bearer ${it.accessToken}"
                        )
                    }

                    headers.put(
                        Metadata.Key.of("X-Device-Id", Metadata.ASCII_STRING_MARSHALLER),
                        android.os.Build.ID
                    )

                    headers.put(
                        Metadata.Key.of("X-OS", Metadata.ASCII_STRING_MARSHALLER),
                        "Android ${android.os.Build.VERSION.RELEASE}"
                    )

                    headers.put(
                        Metadata.Key.of("X-App-Version", Metadata.ASCII_STRING_MARSHALLER),
                        "1.0.0"
                    )
                }

                super.start(responseListener, headers)
            }
        }
    }
}
