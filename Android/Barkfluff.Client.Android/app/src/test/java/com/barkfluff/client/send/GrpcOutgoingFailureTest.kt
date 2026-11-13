package com.barkfluff.client.send

import com.barkfluff.client.cache.OutgoingFailureCategory
import com.barkfluff.client.grpc.AccountRestrictionErrorCodes
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class GrpcOutgoingFailureTest {
    @Test
    fun userUnavailableIsPermanentAccessFailure() {
        assertEquals(
            OutgoingFailureCategory.ACCESS,
            permanentOutgoingFailureCategory(AccountRestrictionErrorCodes.USER_UNAVAILABLE)
        )
    }

    @Test
    fun unrelatedRpcErrorsRemainRetryable() {
        assertNull(permanentOutgoingFailureCategory("other-error"))
        assertNull(permanentOutgoingFailureCategory(null))
    }
}
