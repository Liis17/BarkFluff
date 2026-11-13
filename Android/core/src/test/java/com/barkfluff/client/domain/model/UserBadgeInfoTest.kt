package com.barkfluff.client.domain.model

import barkfluff.users.UsersApiOuterClass
import com.google.protobuf.Timestamp
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Test

class UserBadgeInfoTest {

    @Test
    fun splitUserBadgesSeparatesThePrimaryFromOtherBadges() {
        val primary = badge(id = 1, isPrimary = true)
        val other = badge(id = 2)
        val secondOther = badge(id = 3)

        val sections = splitUserBadges(listOf(primary, other, secondOther))

        assertSame(primary, sections.primary)
        assertEquals(listOf(other, secondOther), sections.others)
    }

    @Test
    fun splitUserBadgesKeepsAllBadgesInOthersWhenPrimaryIsNotSet() {
        val badges = listOf(badge(id = 1), badge(id = 2))

        val sections = splitUserBadges(badges)

        assertNull(sections.primary)
        assertEquals(badges, sections.others)
    }

    @Test
    fun protoBadgeMapsFieldsAndTimestampToDomainModel() {
        val proto = UsersApiOuterClass.UserBadge.newBuilder()
            .setBadge(
                UsersApiOuterClass.Badge.newBuilder()
                    .setId(17)
                    .setName("Helper")
                    .setDescription("Early community member")
                    .setImageUrl("https://node.example/badges/17.png")
                    .setMiniImageUrl("https://node.example/badges/17-mini.png")
            )
            .setAssignedDate(Timestamp.newBuilder().setSeconds(1_700_000_000L).setNanos(123_000_000))
            .setIsPrimary(true)
            .build()

        assertEquals(
            UserBadgeInfo(
                badgeId = 17,
                name = "Helper",
                description = "Early community member",
                imageUrl = "https://node.example/badges/17.png",
                miniImageUrl = "https://node.example/badges/17-mini.png",
                assignedAtMillis = 1_700_000_000_123L,
                isPrimary = true,
            ),
            proto.toDomain(),
        )
    }

    private fun badge(id: Int, isPrimary: Boolean = false) = UserBadgeInfo(
        badgeId = id,
        name = "Badge $id",
        description = "",
        imageUrl = "image-$id",
        miniImageUrl = "",
        assignedAtMillis = 0L,
        isPrimary = isPrimary,
    )
}
