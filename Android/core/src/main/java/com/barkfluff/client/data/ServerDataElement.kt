package com.barkfluff.client.data

/**
 * Данные о сервере для отображения в списке
 */
data class ServerDataElement(
    val title: String = "",
    val description: String = "",
    val ip: String = "",
    val userCount: String = "",
    val publicName: String = "",
    val location: String = "",
    val hexColor: String = "",
    val filesMediaEndpoint: String = ""
)
