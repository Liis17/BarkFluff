package com.barkfluff.android.data.model

data class ServerConfig(
    val serverName: String,
    val identityHost: String,
    val identityPort: Int,
    val identityTls: Boolean,
    val messagesHost: String,
    val messagesPort: Int,
    val messagesTls: Boolean,
    val colorMain: String,
    val colorLite: String,
    val colorHard: String,
)
