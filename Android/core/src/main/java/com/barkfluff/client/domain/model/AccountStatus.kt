package com.barkfluff.client.domain.model

import barkfluff.users.UsersApiOuterClass

enum class AccountStatus {
    ACTIVE,
    BLOCKED,
    PENDING_DELETION,
    DELETED,
    UNKNOWN;

    companion object {
        fun fromStoredNumber(value: Int): AccountStatus = entries.getOrNull(value) ?: UNKNOWN
    }
}

data class AccountRestriction(
    val status: AccountStatus,
    val reason: String,
    val deletionScheduledAtMillis: Long?,
)

fun UsersApiOuterClass.AccountStatus.toDomain(): AccountStatus = when (this) {
    UsersApiOuterClass.AccountStatus.ACTIVE -> AccountStatus.ACTIVE
    UsersApiOuterClass.AccountStatus.BLOCKED -> AccountStatus.BLOCKED
    UsersApiOuterClass.AccountStatus.PENDING_DELETION -> AccountStatus.PENDING_DELETION
    UsersApiOuterClass.AccountStatus.DELETED -> AccountStatus.DELETED
    UsersApiOuterClass.AccountStatus.UNRECOGNIZED -> AccountStatus.UNKNOWN
}
