package com.barkfluff.client.send

import com.barkfluff.client.cache.OutgoingFailureCategory
import com.barkfluff.client.grpc.AccountRestrictionErrorCodes

internal fun permanentOutgoingFailureCategory(errorCode: String?): OutgoingFailureCategory? =
    when (errorCode?.uppercase()) {
        AccountRestrictionErrorCodes.USER_UNAVAILABLE -> OutgoingFailureCategory.ACCESS
        else -> null
    }
