package com.barkfluff.client.auth

import androidx.lifecycle.ViewModel
import com.barkfluff.client.domain.auth.AuthenticationChallengeController
import com.barkfluff.client.domain.gateway.AuthenticationChallengeGateway
import dagger.hilt.android.lifecycle.HiltViewModel
import javax.inject.Inject

/** Activity-retained home for a challenge reference; it is never written to saved state. */
@HiltViewModel
class AuthenticationChallengeViewModel @Inject constructor(
    gateway: AuthenticationChallengeGateway,
) : ViewModel() {
    val controller = AuthenticationChallengeController(gateway)
}
