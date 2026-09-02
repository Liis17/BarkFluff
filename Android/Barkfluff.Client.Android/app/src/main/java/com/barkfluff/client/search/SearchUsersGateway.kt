package com.barkfluff.client.search

import com.barkfluff.client.grpc.GrpcManager
import javax.inject.Inject

/** Backend seam for the standalone search screen. */
interface SearchUsersGateway {
    suspend fun search(query: String): Result<List<GrpcManager.UserData>>
}

class GrpcSearchUsersGateway @Inject constructor(
    private val grpcManager: GrpcManager
) : SearchUsersGateway {
    override suspend fun search(query: String): Result<List<GrpcManager.UserData>> =
        grpcManager.searchUsers(query)
}
