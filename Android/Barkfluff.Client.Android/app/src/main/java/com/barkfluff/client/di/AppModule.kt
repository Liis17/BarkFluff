package com.barkfluff.client.di

import android.content.Context
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.send.OutgoingMessageQueue
import com.barkfluff.client.calls.CallEventsService
import com.barkfluff.client.calls.CallRepository
import com.barkfluff.client.crypto.BarkFluffSignalStore
import com.barkfluff.client.crypto.PrekeyManager
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.grpc.GrpcClientRegistry
import com.barkfluff.client.grpc.MediaHttpTransport
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.grpc.RealtimeSideEffects
import com.barkfluff.client.grpc.TokenCoordinator
import com.barkfluff.client.domain.gateway.AccountSecurityGateway
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.ChatDirectoryGateway
import com.barkfluff.client.domain.gateway.ChatFolderGateway
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.domain.gateway.FastAuthGateway
import com.barkfluff.client.domain.gateway.GrpcAccountSecurityGateway
import com.barkfluff.client.domain.gateway.GrpcAuthGateway
import com.barkfluff.client.domain.gateway.GrpcChatDirectoryGateway
import com.barkfluff.client.domain.gateway.GrpcChatFolderGateway
import com.barkfluff.client.domain.gateway.GrpcFileMediaGateway
import com.barkfluff.client.domain.gateway.GrpcFastAuthGateway
import com.barkfluff.client.domain.gateway.GrpcMessageGateway
import com.barkfluff.client.domain.gateway.GrpcRealtimeGateway
import com.barkfluff.client.domain.gateway.GrpcServerDiscoveryGateway
import com.barkfluff.client.domain.gateway.GrpcStickerGateway
import com.barkfluff.client.domain.gateway.GrpcUserDirectoryGateway
import com.barkfluff.client.domain.gateway.GrpcUserProfileGateway
import com.barkfluff.client.domain.gateway.GrpcUserSettingsGateway
import com.barkfluff.client.domain.gateway.MessageGateway
import com.barkfluff.client.domain.gateway.RealtimeGateway
import com.barkfluff.client.domain.gateway.ServerDiscoveryGateway
import com.barkfluff.client.domain.gateway.StickerGateway
import com.barkfluff.client.domain.gateway.UserDirectoryGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.gateway.UserSettingsGateway
import com.barkfluff.client.notifications.RealtimeSideEffectsImpl
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.repository.PrivateChatRepository
import com.barkfluff.client.repository.SecretChatRepository
import com.barkfluff.client.search.GrpcSearchUsersGateway
import com.barkfluff.client.search.SearchUsersGateway
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
    fun provideGrpcClientRegistry(grpcManager: GrpcManager): GrpcClientRegistry =
        grpcManager.clientRegistry()

    @Provides
    @Singleton
    fun provideMediaHttpTransport(@ApplicationContext context: Context): MediaHttpTransport =
        MediaHttpTransport(context)

    @Provides
    @Singleton
    fun provideTokenCoordinator(grpcManager: GrpcManager): TokenCoordinator =
        grpcManager.tokenCoordinator()

    @Provides
    @Singleton
    fun provideSearchUsersGateway(implementation: GrpcSearchUsersGateway): SearchUsersGateway =
        implementation

    @Provides
    @Singleton
    fun provideServerDiscoveryGateway(grpcManager: GrpcManager): ServerDiscoveryGateway =
        GrpcServerDiscoveryGateway(grpcManager)

    @Provides
    @Singleton
    fun provideAuthGateway(
        grpcManager: GrpcManager,
        @ApplicationContext context: Context,
    ): AuthGateway = GrpcAuthGateway(grpcManager, context)

    @Provides
    @Singleton
    fun provideAccountSecurityGateway(grpcManager: GrpcManager): AccountSecurityGateway =
        GrpcAccountSecurityGateway(grpcManager)

    @Provides
    @Singleton
    fun provideUserProfileGateway(grpcManager: GrpcManager): UserProfileGateway =
        GrpcUserProfileGateway(grpcManager)

    @Provides
    @Singleton
    fun provideUserSettingsGateway(grpcManager: GrpcManager): UserSettingsGateway =
        GrpcUserSettingsGateway(grpcManager)

    @Provides
    @Singleton
    fun provideUserDirectoryGateway(grpcManager: GrpcManager): UserDirectoryGateway =
        GrpcUserDirectoryGateway(grpcManager)

    @Provides
    @Singleton
    fun provideChatDirectoryGateway(grpcManager: GrpcManager): ChatDirectoryGateway =
        GrpcChatDirectoryGateway(grpcManager)

    @Provides
    @Singleton
    fun provideMessageGateway(
        chatRepository: ChatRepository,
        grpcManager: GrpcManager,
    ): MessageGateway = GrpcMessageGateway(chatRepository, grpcManager)

    @Provides
    @Singleton
    fun provideChatFolderGateway(grpcManager: GrpcManager): ChatFolderGateway =
        GrpcChatFolderGateway(grpcManager)

    @Provides
    @Singleton
    fun provideFileMediaGateway(chatRepository: ChatRepository): FileMediaGateway =
        GrpcFileMediaGateway(chatRepository)

    @Provides
    @Singleton
    fun provideStickerGateway(grpcManager: GrpcManager): StickerGateway =
        GrpcStickerGateway(grpcManager)

    @Provides
    @Singleton
    fun provideRealtimeGateway(realtimeService: RealtimeService): RealtimeGateway =
        GrpcRealtimeGateway(realtimeService)

    @Provides
    @Singleton
    fun provideFastAuthGateway(grpcManager: GrpcManager): FastAuthGateway =
        GrpcFastAuthGateway(grpcManager)

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
        tokenCoordinator: TokenCoordinator,
        sideEffects: RealtimeSideEffects
    ): RealtimeService = RealtimeService(context, grpcManager, tokenCoordinator, sideEffects)

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
        grpcManager: GrpcManager,
        mediaTransport: MediaHttpTransport,
    ): ChatRepository = ChatRepository(context, grpcManager, mediaTransport)

    @Provides
    @Singleton
    fun provideOutgoingMessageQueue(
        @ApplicationContext context: Context,
        chatCacheRepository: ChatCacheRepository,
        chatRepository: ChatRepository,
        tokenCoordinator: TokenCoordinator
    ): OutgoingMessageQueue = OutgoingMessageQueue(context, chatCacheRepository, chatRepository, tokenCoordinator)

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
        callRepository: CallRepository,
        tokenCoordinator: TokenCoordinator,
    ): CallEventsService = CallEventsService(context, grpcManager, callRepository, tokenCoordinator)
}
