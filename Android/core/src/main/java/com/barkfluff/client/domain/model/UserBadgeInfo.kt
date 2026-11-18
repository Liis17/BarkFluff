package com.barkfluff.client.domain.model

import barkfluff.users.UsersApiOuterClass

data class UserBadgeInfo(
    val badgeId: Int,
    val name: String,
    val description: String,
    val imageUrl: String,
    val miniImageUrl: String,
    val assignedAtMillis: Long,
    val isPrimary: Boolean,
)

data class UserBadgeSections(
    val primary: UserBadgeInfo?,
    val others: List<UserBadgeInfo>,
)

fun splitUserBadges(badges: List<UserBadgeInfo>): UserBadgeSections = UserBadgeSections(
    primary = badges.firstOrNull { it.isPrimary },
    others = badges.filterNot { it.isPrimary },
)

fun UsersApiOuterClass.UserBadge.toDomain(): UserBadgeInfo {
    val badgeInfo = badge
    return UserBadgeInfo(
        badgeId = badgeInfo.id,
        name = badgeInfo.name,
        description = badgeInfo.description,
        imageUrl = badgeInfo.imageUrl,
        miniImageUrl = badgeInfo.miniImageUrl,
        assignedAtMillis = assignedDate.seconds * 1_000L + assignedDate.nanos / 1_000_000L,
        isPrimary = isPrimary,
    )
}
