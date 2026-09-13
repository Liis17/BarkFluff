package com.barkfluff.client.grpc

import android.content.Context
import android.content.ContextWrapper
import android.content.SharedPreferences
import com.barkfluff.client.data.GlobalParam
import io.grpc.CallOptions
import io.grpc.Channel
import io.grpc.ClientCall
import io.grpc.Metadata
import io.grpc.MethodDescriptor
import org.junit.Assert.assertEquals
import org.junit.Test
import java.lang.reflect.Proxy
import java.util.Base64

class DeviceInfoInterceptorTest {

    @Test
    fun `adds the device name to outgoing metadata`() {
        val recordingCall = RecordingClientCall()
        val channel = object : Channel() {
            @Suppress("UNCHECKED_CAST")
            override fun <RequestT, ResponseT> newCall(
                methodDescriptor: MethodDescriptor<RequestT, ResponseT>,
                callOptions: CallOptions,
            ): ClientCall<RequestT, ResponseT> = recordingCall as ClientCall<RequestT, ResponseT>

            override fun authority(): String = "test"
        }
        val method = MethodDescriptor.newBuilder<Unit, Unit>()
            .setType(MethodDescriptor.MethodType.UNARY)
            .setFullMethodName("test.Service/CreateAccount")
            .setRequestMarshaller(UnitMarshaller)
            .setResponseMarshaller(UnitMarshaller)
            .build()

        DeviceInfoInterceptor(TestContext()).interceptCall(method, CallOptions.DEFAULT, channel)
            .start(object : ClientCall.Listener<Unit>() {}, Metadata())

        val deviceNameKey = Metadata.Key.of("x-device-name", Metadata.ASCII_STRING_MARSHALLER)
        val expected = Base64.getEncoder().encodeToString(
            GlobalParam.getDeviceName().toByteArray(Charsets.UTF_8),
        )

        assertEquals(expected, recordingCall.headers.get(deviceNameKey))
    }

    private class RecordingClientCall : ClientCall<Any, Any>() {
        lateinit var headers: Metadata

        override fun start(responseListener: Listener<Any>, headers: Metadata) {
            this.headers = headers
        }

        override fun request(numMessages: Int) = Unit

        override fun cancel(message: String?, cause: Throwable?) = Unit

        override fun halfClose() = Unit

        override fun sendMessage(message: Any) = Unit
    }

    private object UnitMarshaller : MethodDescriptor.Marshaller<Unit> {
        override fun stream(value: Unit) = java.io.ByteArrayInputStream(ByteArray(0))

        override fun parse(stream: java.io.InputStream): Unit = Unit
    }

    private inner class TestContext : ContextWrapper(null) {
        private val preferences = memoryPreferences()

        override fun getApplicationContext(): Context = this

        override fun getSharedPreferences(name: String, mode: Int): SharedPreferences = preferences
    }

    @Suppress("UNCHECKED_CAST")
    private fun memoryPreferences(): SharedPreferences {
        lateinit var editor: SharedPreferences.Editor
        editor = Proxy.newProxyInstance(
            SharedPreferences.Editor::class.java.classLoader,
            arrayOf(SharedPreferences.Editor::class.java),
        ) { proxy, method, _ ->
            when (method.name) {
                "apply" -> Unit
                "commit" -> true
                "clear", "remove", "putBoolean", "putFloat", "putInt", "putLong", "putString", "putStringSet" -> proxy
                else -> null
            }
        } as SharedPreferences.Editor

        return Proxy.newProxyInstance(
            SharedPreferences::class.java.classLoader,
            arrayOf(SharedPreferences::class.java),
        ) { _, method, _ ->
            when (method.name) {
                "edit" -> editor
                "getString", "getStringSet" -> null
                "getAll" -> emptyMap<String, Any>()
                "contains" -> false
                "getBoolean" -> false
                "getFloat" -> 0f
                "getInt" -> 0
                "getLong" -> 0L
                else -> Unit
            }
        } as SharedPreferences
    }
}
