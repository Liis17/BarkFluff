package com.barkfluff.messenger.di

import android.content.Context
import com.barkfluff.messenger.data.local.SessionManager
import com.barkfluff.messenger.data.remote.interceptor.AuthInterceptor
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import io.grpc.ManagedChannel
import io.grpc.ManagedChannelBuilder
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking
import javax.inject.Named
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object NetworkModule {

    @Provides
    @Singleton
    fun provideAuthInterceptor(sessionManager: SessionManager): AuthInterceptor {
        return AuthInterceptor(sessionManager)
    }

    @Provides
    @Singleton
    @Named("beaconChannel")
    fun provideBeaconChannel(
        @ApplicationContext context: Context,
        sessionManager: SessionManager
    ): ManagedChannel {
        val serverInfo = runBlocking { sessionManager.serverInfo.first() }
        val endpoint = serverInfo?.beaconEndpoint ?: "localhost:5000"

        return ManagedChannelBuilder.forTarget(endpoint)
            .usePlaintext()
            .build()
    }

    @Provides
    @Singleton
    @Named("identityChannel")
    fun provideIdentityChannel(
        sessionManager: SessionManager,
        authInterceptor: AuthInterceptor
    ): ManagedChannel {
        val serverInfo = runBlocking { sessionManager.serverInfo.first() }
        val endpoint = serverInfo?.identityEndpoint ?: "localhost:5001"

        return ManagedChannelBuilder.forTarget(endpoint)
            .usePlaintext()
            .intercept(authInterceptor)
            .build()
    }

    @Provides
    @Singleton
    @Named("usersChannel")
    fun provideUsersChannel(
        sessionManager: SessionManager,
        authInterceptor: AuthInterceptor
    ): ManagedChannel {
        val serverInfo = runBlocking { sessionManager.serverInfo.first() }
        val endpoint = serverInfo?.usersEndpoint ?: "localhost:5002"

        return ManagedChannelBuilder.forTarget(endpoint)
            .usePlaintext()
            .intercept(authInterceptor)
            .build()
    }

    @Provides
    @Singleton
    @Named("filesChannel")
    fun provideFilesChannel(
        sessionManager: SessionManager,
        authInterceptor: AuthInterceptor
    ): ManagedChannel {
        val serverInfo = runBlocking { sessionManager.serverInfo.first() }
        val endpoint = serverInfo?.filesEndpoint ?: "localhost:5003"

        return ManagedChannelBuilder.forTarget(endpoint)
            .usePlaintext()
            .intercept(authInterceptor)
            .build()
    }

    @Provides
    @Singleton
    @Named("messagesChannel")
    fun provideMessagesChannel(
        sessionManager: SessionManager,
        authInterceptor: AuthInterceptor
    ): ManagedChannel {
        val serverInfo = runBlocking { sessionManager.serverInfo.first() }
        val endpoint = serverInfo?.messagesEndpoint ?: "localhost:5004"

        return ManagedChannelBuilder.forTarget(endpoint)
            .usePlaintext()
            .intercept(authInterceptor)
            .build()
    }

    @Provides
    @Singleton
    @Named("updatesChannel")
    fun provideUpdatesChannel(
        sessionManager: SessionManager,
        authInterceptor: AuthInterceptor
    ): ManagedChannel {
        val serverInfo = runBlocking { sessionManager.serverInfo.first() }
        val endpoint = serverInfo?.updatesEndpoint ?: "localhost:5005"

        return ManagedChannelBuilder.forTarget(endpoint)
            .usePlaintext()
            .intercept(authInterceptor)
            .build()
    }

    @Provides
    @Singleton
    @Named("fastAuthChannel")
    fun provideFastAuthChannel(
        sessionManager: SessionManager,
        authInterceptor: AuthInterceptor
    ): ManagedChannel {
        val serverInfo = runBlocking { sessionManager.serverInfo.first() }
        val endpoint = serverInfo?.fastAuthEndpoint
            ?: serverInfo?.identityEndpoint
            ?: "localhost:5010"

        return ManagedChannelBuilder.forTarget(endpoint)
            .usePlaintext()
            .intercept(authInterceptor)
            .build()
    }
}
