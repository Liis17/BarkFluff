package com.barkfluff.messenger.data.repository

import com.barkfluff.messenger.domain.model.Badge
import com.barkfluff.messenger.domain.model.User
import com.barkfluff.messenger.domain.model.UserBadge
import io.grpc.ManagedChannel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.Instant
import javax.inject.Inject
import javax.inject.Named

class UserRepository @Inject constructor(
    @Named("usersChannel") private val usersChannel: ManagedChannel
) {

    suspend fun getUser(userId: Long): Result<User> = withContext(Dispatchers.IO) {
        try {
            val stub = users_api.UsersApiGrpc.newBlockingStub(usersChannel)

            val request = users_api.UsersApi.GetUserRequest.newBuilder()
                .setUserId(userId)
                .build()

            val response = stub.getUser(request)
            val userProto = response.user

            val user = User(
                id = userProto.id,
                firstName = userProto.firstName,
                lastName = userProto.lastName,
                username = userProto.username,
                bio = userProto.bio.takeIf { it.isNotBlank() },
                profilePictureUrl = userProto.profilePicture.takeIf { it.isNotBlank() },
                profilePictureId = null,
                registrationDate = Instant.ofEpochSecond(
                    userProto.registrationDate.seconds,
                    userProto.registrationDate.nanos.toLong()
                ),
                badges = userProto.badgesList.map { badgeProto ->
                    UserBadge(
                        badge = Badge(
                            id = badgeProto.badge.id,
                            name = badgeProto.badge.name,
                            description = badgeProto.badge.description,
                            imageUrl = badgeProto.badge.imageUrl,
                            createdDate = Instant.ofEpochSecond(
                                badgeProto.badge.createdDate.seconds,
                                badgeProto.badge.createdDate.nanos.toLong()
                            ),
                            isActive = badgeProto.badge.isActive
                        ),
                        priority = badgeProto.priority,
                        assignedDate = Instant.ofEpochSecond(
                            badgeProto.assignedDate.seconds,
                            badgeProto.assignedDate.nanos.toLong()
                        )
                    )
                }
            )

            Result.success(user)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun searchUsers(query: String, offset: Int = 0, limit: Int = 50): Result<List<User>> =
        withContext(Dispatchers.IO) {
            try {
                val stub = users_api.UsersApiGrpc.newBlockingStub(usersChannel)

                val request = users_api.UsersApi.SearchUsersRequest.newBuilder()
                    .setQuery(query)
                    .setPage(
                        shared.Shared.PageRequest.newBuilder()
                            .setOffset(offset)
                            .setSize(limit)
                            .build()
                    )
                    .build()

                val response = stub.searchUsers(request)

                val users = response.usersList.map { userProto ->
                    User(
                        id = userProto.id,
                        firstName = userProto.firstName,
                        lastName = userProto.lastName,
                        username = userProto.username,
                        bio = userProto.bio.takeIf { it.isNotBlank() },
                        profilePictureUrl = userProto.profilePicture.takeIf { it.isNotBlank() },
                        profilePictureId = null,
                        registrationDate = Instant.ofEpochSecond(
                            userProto.registrationDate.seconds,
                            userProto.registrationDate.nanos.toLong()
                        ),
                        badges = emptyList()
                    )
                }

                Result.success(users)
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    suspend fun updateProfile(
        firstName: String? = null,
        lastName: String? = null,
        username: String? = null,
        bio: String? = null
    ): Result<Unit> = withContext(Dispatchers.IO) {
        try {
            val stub = users_api.UsersApiGrpc.newBlockingStub(usersChannel)

            firstName?.let {
                lastName?.let { ln ->
                    val request = users_api.UsersApi.ChangeNameRequest.newBuilder()
                        .setFirstName(it)
                        .setLastName(ln)
                        .build()
                    stub.changeName(request)
                }
            }

            username?.let {
                val request = users_api.UsersApi.ChangeUsernameRequest.newBuilder()
                    .setUsername(it)
                    .build()
                stub.changeUsername(request)
            }

            bio?.let {
                val request = users_api.UsersApi.ChangeBioRequest.newBuilder()
                    .setBio(it)
                    .build()
                stub.changeBio(request)
            }

            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    suspend fun checkUsernameAvailable(username: String): Result<Boolean> =
        withContext(Dispatchers.IO) {
            try {
                val stub = users_api.UsersApiGrpc.newBlockingStub(usersChannel)

                val request = users_api.UsersApi.CheckExistUsernameRequest.newBuilder()
                    .setUsername(username)
                    .build()

                val response = stub.checkExistUsername(request)
                Result.success(!response.isExist)
            } catch (e: Exception) {
                Result.failure(e)
            }
        }

    suspend fun checkEmailAvailable(email: String): Result<Boolean> = withContext(Dispatchers.IO) {
        try {
            val stub = users_api.UsersApiGrpc.newBlockingStub(usersChannel)

            val request = users_api.UsersApi.CheckExistEmailRequest.newBuilder()
                .setEmail(email)
                .build()

            val response = stub.checkExistEmail(request)
            Result.success(!response.isExist)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}
