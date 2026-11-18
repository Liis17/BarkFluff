package com.barkfluff.client.grpc

import io.grpc.Metadata
import io.grpc.StatusException
import io.grpc.StatusRuntimeException

object AccountRestrictionErrorCodes {
    const val ACCOUNT_BLOCKED = "F34D8C22-C075-4E9C-92AD-C893F192D8FA"
    const val ACCOUNT_PENDING_DELETION = "8E6F0F30-98AF-4F96-A63B-20D52AEEFB6A"
    const val USER_UNAVAILABLE = "6BC0E7A1-5117-4910-9717-1784E6921F6F"
}

fun Throwable.grpcErrorCode(): String? {
    val key = Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER)
    return generateSequence(this) { it.cause }.firstNotNullOfOrNull { error ->
        when (error) {
            is StatusException -> error.trailers?.get(key)
            is StatusRuntimeException -> error.trailers?.get(key)
            else -> null
        }
    }
}
