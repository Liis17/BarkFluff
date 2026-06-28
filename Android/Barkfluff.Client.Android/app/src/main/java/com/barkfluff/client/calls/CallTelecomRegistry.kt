package com.barkfluff.client.calls

import android.telecom.Connection
import android.telecom.DisconnectCause
import java.util.concurrent.ConcurrentHashMap

object CallTelecomRegistry {
    private val connections = ConcurrentHashMap<String, Connection>()
    private val answeringCalls = ConcurrentHashMap.newKeySet<String>()
    private val activeCalls = ConcurrentHashMap.newKeySet<String>()

    fun put(callId: String, connection: Connection) {
        if (callId.isNotBlank()) {
            connections[callId] = connection
        }
    }

    fun markAnswering(callId: String) {
        if (callId.isNotBlank()) {
            answeringCalls.add(callId)
        }
    }

    fun clearAnswering(callId: String) {
        answeringCalls.remove(callId)
    }

    fun markActive(callId: String) {
        if (callId.isBlank()) return
        answeringCalls.remove(callId)
        activeCalls.add(callId)
        connections[callId]?.setActive()
    }

    fun hasConnection(callId: String): Boolean =
        callId.isNotBlank() && connections.containsKey(callId)

    fun isAnsweringOrActive(callId: String): Boolean =
        answeringCalls.contains(callId) || activeCalls.contains(callId)

    fun disconnect(callId: String, cause: Int = DisconnectCause.LOCAL) {
        answeringCalls.remove(callId)
        activeCalls.remove(callId)
        val connection = connections.remove(callId) ?: return
        connection.setDisconnected(DisconnectCause(cause))
        connection.destroy()
    }
}