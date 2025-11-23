package com.barkfluff.messenger.domain.model

import java.time.Instant

data class User(
    val id: Long,
    val firstName: String,
    val lastName: String,
    val username: String,
    val bio: String?,
    val profilePictureUrl: String?,
    val profilePictureId: String?,
    val registrationDate: Instant,
    val badges: List<UserBadge> = emptyList()
) {
    val fullName: String
        get() = "$firstName $lastName"
}

data class UserBadge(
    val badge: Badge,
    val priority: Int,
    val assignedDate: Instant
)

data class Badge(
    val id: Long,
    val name: String,
    val description: String,
    val imageUrl: String,
    val createdDate: Instant,
    val isActive: Boolean
)
