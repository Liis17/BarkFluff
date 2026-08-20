package com.barkfluff.client.di

import android.content.Context
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.calls.CallEventsService
import com.barkfluff.client.calls.CallRepository
import com.barkfluff.client.crypto.BarkFluffSignalStore
import com.barkfluff.client.crypto.PrekeyManager
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.grpc.RealtimeSideEffects
import com.barkfluff.client.notifications.RealtimeSideEffectsImpl
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.repository.PrivateChatRepository
import com.barkfluff.client.repository.SecretChatRepository
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

/**
 * Composition root приложения: единственные экземпляры gRPC-менеджера, realtime-сервисов,
 * E2E-инфраструктуры и репозиториев. Публичные свойства BarkFluffApplication — делегаты
 * к этим синглтонам, поэтому существующие касты (application as BarkFluffApplication).*
 * продолжают работать; новые компоненты получают зависимости через конструктор.
 */
@Module
@InstallIn(SingletonComponent::class)
object AppModule {

    @Provides
    @Singleton
    fun provideGrpcManager(@ApplicationContext context: Context): GrpcManager =
        GrpcManager(context)

    @Provides
    @Singleton
    fun provideChatCacheRepository(@ApplicationContext context: Context): ChatCacheRepository =
        ChatCacheRepository(context)

    @Provides
    @Singleton
    fun provideChatDraftRepository(
        @ApplicationContext context: Context,
        grpcManager: GrpcManager,
        chatCacheRepository: ChatCacheRepository
    ): ChatDraftRepository = ChatDraftRepository(context, grpcManager, chatCacheRepository)

    @Provides
    @Singleton
    fun provideRealtimeSideEffects(
        @ApplicationContext context: Context,
        grpcManager: GrpcManager
    ): RealtimeSideEffects = RealtimeSideEffectsImpl(context, grpcManager)

    @Provides
    @Singleton
    fun provideRealtimeService(
        @ApplicationContext context: Context,
        grpcManager: GrpcManager,
        sideEffects: RealtimeSideEffects
    ): RealtimeService = RealtimeService(context, grpcManager, sideEffects)

    @Provides
    @Singleton
    fun provideSignalStore(@ApplicationContext context: Context): BarkFluffSignalStore =
        BarkFluffSignalStore(context)

    @Provides
    @Singleton
    fun providePrekeyManager(
        @ApplicationContext context: Context,
        signalStore: BarkFluffSignalStore
    ): PrekeyManager = PrekeyManager(context, signalStore)

    @Provides
    @Singleton
    fun provideChatRepository(
        @ApplicationContext context: Context,
        grpcManager: GrpcManager
    ): ChatRepository = ChatRepository(context, grpcManager)

    @Provides
    @Singleton
    fun providePrivateChatRepository(
        @ApplicationContext context: Context,
        grpcManager: GrpcManager
    ): PrivateChatRepository = PrivateChatRepository(context, grpcManager)

    @Provides
    @Singleton
    fun provideSecretChatRepository(
        @ApplicationContext context: Context,
        grpcManager: GrpcManager,
        signalStore: BarkFluffSignalStore
    ): SecretChatRepository = SecretChatRepository(context, grpcManager, signalStore)

    @Provides
    @Singleton
    fun provideCallRepository(grpcManager: GrpcManager): CallRepository =
        CallRepository(grpcManager)

    @Provides
    @Singleton
    fun provideCallEventsService(
        @ApplicationContext context: Context,
        grpcManager: GrpcManager,
        callRepository: CallRepository
    ): CallEventsService = CallEventsService(context, grpcManager, callRepository)
}
