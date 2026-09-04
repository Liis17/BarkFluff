package com.barkfluff.client.search

import com.barkfluff.client.domain.gateway.UserDirectoryGateway
import com.barkfluff.client.domain.model.UserProfile
import javax.inject.Inject

/** Backend seam for the standalone search screen. */
interface SearchUsersGateway {
    suspend fun search(query: String): Result<List<UserProfile>>
}

class GrpcSearchUsersGateway @Inject constructor(
    private val userDirectoryGateway: UserDirectoryGateway
) : SearchUsersGateway {
    override suspend fun search(query: String): Result<List<UserProfile>> =
        userDirectoryGateway.search(query)
}
