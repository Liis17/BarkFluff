package com.barkfluff.client.domain.model

import barkfluff.users.UsersApiOuterClass
import org.junit.Assert.assertEquals
import org.junit.Test

class AccountStatusTest {
    @Test
    fun `maps all server statuses and fails closed for unknown values`() {
        assertEquals(AccountStatus.ACTIVE, UsersApiOuterClass.AccountStatus.ACTIVE.toDomain())
        assertEquals(AccountStatus.BLOCKED, UsersApiOuterClass.AccountStatus.BLOCKED.toDomain())
        assertEquals(AccountStatus.PENDING_DELETION, UsersApiOuterClass.AccountStatus.PENDING_DELETION.toDomain())
        assertEquals(AccountStatus.DELETED, UsersApiOuterClass.AccountStatus.DELETED.toDomain())
        assertEquals(AccountStatus.UNKNOWN, UsersApiOuterClass.AccountStatus.UNRECOGNIZED.toDomain())
        assertEquals(AccountStatus.DELETED, AccountStatus.fromStoredNumber(3))
        assertEquals(AccountStatus.UNKNOWN, AccountStatus.fromStoredNumber(Int.MAX_VALUE))
    }
}
