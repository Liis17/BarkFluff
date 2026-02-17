package com.barkfluff.android.data.model

sealed class AuthResult {
    data class Success(
        val accessToken: String,
        val accessTokenExpiry: Long,
        val refreshToken: String,
    ) : AuthResult()

    object NeedsOtp : AuthResult()
    object WrongOtp : AuthResult()
    data class Error(val message: String) : AuthResult()
}
