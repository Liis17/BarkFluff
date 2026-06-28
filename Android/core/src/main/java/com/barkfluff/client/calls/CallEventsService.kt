package com.barkfluff.client.calls

import android.content.Context
import android.util.Log
import barkfluff.calls.CallsApiOuterClass
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.grpc.GrpcManager
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.collect
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlin.coroutines.coroutineContext
import kotlin.math.min
import kotlin.math.pow

class CallEventsService(
    private val context: Context,
    private val grpcManager: GrpcManager,
    private val callRepository: CallRepository
) {
    enum class ConnectionState { DISCONNECTED, CONNECTING, CONNECTED }
    enum class Phase { INCOMING, RINGING, CONNECTING, ACTIVE, ENDED }

    data class CallState(
        val callId: String,
        val phase: Phase,
        val mediaType: CallsApiOuterClass.CallMediaType,
        val callerUserId: Long = 0L,
        val chatId: String = "",
        val startedAtMs: Long = 0L,
        val endedReason: CallsApiOuterClass.CallEndReason = CallsApiOuterClass.CallEndReason.CALL_END_REASON_UNKNOWN,
        val durationSeconds: Long = 0L
    ) {
        val isTerminal: Boolean get() = phase == Phase.ENDED
    }

    private val globalParam = GlobalParam(context)
    private var serviceScope: CoroutineScope? = null
    private var streamJob: Job? = null

    private val _connectionState = MutableStateFlow(ConnectionState.DISCONNECTED)
    val connectionState: StateFlow<ConnectionState> = _connectionState

    private val _events = MutableSharedFlow<CallsApiOuterClass.CallEvent>(extraBufferCapacity = 32)
    val events: SharedFlow<CallsApiOuterClass.CallEvent> = _events

    private val _currentCall = MutableStateFlow<CallState?>(null)
    val currentCall: StateFlow<CallState?> = _currentCall

    fun resume() {
        val currentScope = serviceScope
        if (currentScope != null && currentScope.isActive) return

        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) {
            Log.v(TAG, "resume skipped: calls endpoint is empty")
            return
        }

        grpcManager.createCallsClient(callsAddress, context, includeDeviceInfo = true)
        val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
        serviceScope = scope
        streamJob = scope.launch { streamWithReconnect() }
    }

    fun pause() {
        _connectionState.value = ConnectionState.DISCONNECTED
        streamJob?.cancel()
        streamJob = null
        serviceScope?.cancel()
        serviceScope = null
    }

    fun shutdown() {
        pause()
    }

    private suspend fun streamWithReconnect() {
        var attempts = 0
        while (coroutineContext.isActive) {
            try {
                _connectionState.value = ConnectionState.CONNECTING
                ensureCallsClient()
                if (!grpcManager.ensureTokenValid(context)) {
                    throw IllegalStateException("Access token is not valid")
                }

                _connectionState.value = ConnectionState.CONNECTED
                callRepository.subscribeCallEvents().collect { event ->
                    attempts = 0
                    _events.emit(event)
                    handleEvent(event)
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                attempts++
                _connectionState.value = ConnectionState.DISCONNECTED
                Log.w(TAG, "Call events stream error (attempt $attempts): ${e.message}")

                if (attempts >= TOKEN_REFRESH_AFTER_ATTEMPTS) {
                    grpcManager.forceRefreshToken(context)
                }

                val backoff = min(
                    BASE_BACKOFF_MS * 2.0.pow((attempts - 1).coerceAtMost(10).toDouble()),
                    MAX_BACKOFF_MS.toDouble()
                ).toLong()
                delay(backoff)
                ensureCallsClient(force = true)
            }
        }
    }

    private suspend fun handleEvent(event: CallsApiOuterClass.CallEvent) {
        when (event.eventCase) {
            CallsApiOuterClass.CallEvent.EventCase.INCOMING -> handleIncoming(event.incoming)
            CallsApiOuterClass.CallEvent.EventCase.ACCEPTED -> updatePhase(event.accepted.callId, Phase.CONNECTING)
            CallsApiOuterClass.CallEvent.EventCase.MEMBER -> handleParticipant(event.member)
            CallsApiOuterClass.CallEvent.EventCase.REJECTED -> finishCall(event.rejected.callId, CallsApiOuterClass.CallEndReason.CALL_END_REJECTED)
            CallsApiOuterClass.CallEvent.EventCase.ENDED -> finishCall(event.ended.callId, event.ended.reason, event.ended.durationSeconds)
            else -> Unit
        }
    }

    private suspend fun handleIncoming(event: CallsApiOuterClass.IncomingCallEvent) {
        val activeCall = _currentCall.value
        if (activeCall != null && !activeCall.isTerminal && activeCall.callId != event.callId) {
            Log.i(TAG, "Rejecting incoming call ${event.callId}: already in ${activeCall.callId}")
            callRepository.reject(event.callId)
            return
        }

        _currentCall.value = CallState(
            callId = event.callId,
            phase = Phase.INCOMING,
            mediaType = event.mediaType,
            callerUserId = event.callerUserId,
            chatId = event.chatId,
            startedAtMs = event.startedAt.seconds * 1000L
        )
    }

    private fun handleParticipant(event: CallsApiOuterClass.ParticipantEvent) {
        if (event.action == CallsApiOuterClass.ParticipantAction.PARTICIPANT_JOINED) {
            updatePhase(event.callId, Phase.ACTIVE)
        }
    }

    private fun updatePhase(callId: String, phase: Phase) {
        val current = _currentCall.value
        if (current?.callId == callId) {
            _currentCall.value = current.copy(phase = phase)
        }
    }

    private fun finishCall(
        callId: String,
        reason: CallsApiOuterClass.CallEndReason,
        durationSeconds: Long = 0L
    ) {
        val current = _currentCall.value
        if (current?.callId == callId) {
            _currentCall.value = current.copy(
                phase = Phase.ENDED,
                endedReason = reason,
                durationSeconds = durationSeconds
            )
        }
    }

    private fun ensureCallsClient(force: Boolean = false) {
        if (!force && grpcManager.callsClient != null) return
        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) throw IllegalStateException("Calls endpoint is empty")
        grpcManager.createCallsClient(callsAddress, context, includeDeviceInfo = true).getOrThrow()
    }

    companion object {
        private const val TAG = "CallEventsService"
        private const val BASE_BACKOFF_MS = 2_000L
        private const val MAX_BACKOFF_MS = 30_000L
        private const val TOKEN_REFRESH_AFTER_ATTEMPTS = 3
    }
}
