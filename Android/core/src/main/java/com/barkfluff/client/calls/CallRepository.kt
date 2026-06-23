package com.barkfluff.client.calls

import barkfluff.calls.CallsApiOuterClass
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.withContext

class CallRepository(
    private val grpcManager: GrpcManager
) {
    suspend fun initiateDirect(
        calleeUserId: Long,
        mediaType: CallsApiOuterClass.CallMediaType
    ): Result<CallsApiOuterClass.InitiateCallResponse> = withContext(Dispatchers.IO) {
        runCatching {
            val client = requireClient()
            val request = CallsApiOuterClass.InitiateCallRequest.newBuilder()
                .setCalleeUserId(calleeUserId)
                .setMediaType(mediaType)
                .build()
            client.initiateCall(request)
        }
    }

    suspend fun initiateGroup(
        chatId: String,
        mediaType: CallsApiOuterClass.CallMediaType
    ): Result<CallsApiOuterClass.InitiateCallResponse> = withContext(Dispatchers.IO) {
        runCatching {
            val client = requireClient()
            val request = CallsApiOuterClass.InitiateCallRequest.newBuilder()
                .setChatId(chatId)
                .setMediaType(mediaType)
                .build()
            client.initiateCall(request)
        }
    }

    suspend fun accept(callId: String): Result<CallsApiOuterClass.AcceptCallResponse> = withContext(Dispatchers.IO) {
        runCatching {
            val request = CallsApiOuterClass.AcceptCallRequest.newBuilder()
                .setCallId(callId)
                .build()
            requireClient().acceptCall(request)
        }
    }

    suspend fun reject(callId: String): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val request = CallsApiOuterClass.RejectCallRequest.newBuilder()
                .setCallId(callId)
                .build()
            requireClient().rejectCall(request)
            Unit
        }
    }

    suspend fun join(callId: String): Result<CallsApiOuterClass.JoinCallResponse> = withContext(Dispatchers.IO) {
        runCatching {
            val request = CallsApiOuterClass.JoinCallRequest.newBuilder()
                .setCallId(callId)
                .build()
            requireClient().joinCall(request)
        }
    }

    suspend fun end(callId: String): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val request = CallsApiOuterClass.EndCallRequest.newBuilder()
                .setCallId(callId)
                .build()
            requireClient().endCall(request)
            Unit
        }
    }

    suspend fun setAudioQuality(
        callId: String,
        quality: CallsApiOuterClass.CallAudioQuality
    ): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val request = CallsApiOuterClass.SetCallAudioQualityRequest.newBuilder()
                .setCallId(callId)
                .setQuality(quality)
                .build()
            requireClient().setCallAudioQuality(request)
            Unit
        }
    }

    fun subscribeCallEvents(): Flow<CallsApiOuterClass.CallEvent> {
        val request = CallsApiOuterClass.SubscribeCallEventsRequest.newBuilder().build()
        return requireClient().subscribeCallEvents(request)
    }

    private fun requireClient() =
        grpcManager.callsClient ?: error("Calls клиент не создан")
}
