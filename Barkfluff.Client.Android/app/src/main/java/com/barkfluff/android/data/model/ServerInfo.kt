package com.barkfluff.android.data.model

data class ServerInfo(
    val name: String,
    val description: String,
    val accountsCount: Long,
    val beaconHost: String,
    val beaconPort: Int,
    val location: String,
    val colorMain: String,
    val colorLite: String,
    val colorHard: String,
)
