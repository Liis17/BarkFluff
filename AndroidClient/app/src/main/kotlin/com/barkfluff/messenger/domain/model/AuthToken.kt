package com.barkfluff.messenger.domain.model

import java.time.Instant

data class AuthToken(
    val accessToken: String,
    val refreshToken: String,
    val accessTokenExpiration: Instant,
    val refreshTokenExpiration: Instant
)

data class ServerInfo(
    val name: String,
    val description: String,
    val color: ServerColor,
    val beaconEndpoint: String,
    val identityEndpoint: String,
    val usersEndpoint: String,
    val filesEndpoint: String,
    val messagesEndpoint: String,
    val updatesEndpoint: String,
    val fastAuthEndpoint: String? = null
)

data class ServerColor(
    val liteHex: String,
    val mainHex: String,
    val hardHex: String
)

data class Session(
    val userId: Long,
    val token: AuthToken,
    val serverInfo: ServerInfo,
    val user: User
)
