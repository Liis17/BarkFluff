package com.barkfluff.client.di

import android.content.Context
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.send.OutgoingMessageQueue
import com.barkfluff.client.calls.CallEventsService
import com.barkfluff.client.calls.CallRepository
import com.barkfluff.client.crypto.BarkFluffSignalStore
import com.barkfluff.client.crypto.PrekeyManager
import com.barkfluff.client.drafts.ChatDraftRepository
import com.barkfluff.client.drafts.ComposerAttachmentStore
import com.barkfluff.client.grpc.GrpcTransportFacade
import com.barkfluff.client.grpc.GrpcClientRegistry
import com.barkfluff.client.grpc.MediaHttpTransport
import com.barkfluff.client.grpc.RealtimeService
import com.barkfluff.client.grpc.RealtimeSideEffects
import com.barkfluff.client.grpc.TokenCoordinator
import com.barkfluff.client.domain.gateway.AccountSecurityGateway
import com.barkfluff.client.domain.gateway.AuthGateway
import com.barkfluff.client.domain.gateway.ChatDirectoryGateway
import com.barkfluff.client.domain.gateway.ChatDraftGateway
import com.barkfluff.client.domain.gateway.ChatFolderGateway
import com.barkfluff.client.domain.gateway.CallGateway
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.domain.gateway.FastAuthGateway
import com.barkfluff.client.domain.gateway.GrpcAccountSecurityGateway
import com.barkfluff.client.domain.gateway.GrpcAuthGateway
import com.barkfluff.client.domain.gateway.GrpcChatDirectoryGateway
import com.barkfluff.client.domain.gateway.GrpcChatDraftGateway
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
import com.barkfluff.client.domain.gateway.PresenceGateway
import com.barkfluff.client.domain.gateway.GrpcPresenceGateway
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
    fun provideGrpcTransportFacade(@ApplicationContext context: Context): GrpcTransportFacade =
        GrpcTransportFacade(context)

    @Provides
    @Singleton
    fun provideGrpcClientRegistry(legacyTransport: GrpcTransportFacade): GrpcClientRegistry =
        legacyTransport.clientRegistry()

    @Provides
    @Singleton
    fun provideMediaHttpTransport(@ApplicationContext context: Context): MediaHttpTransport =
        MediaHttpTransport(context)

    @Provides
    @Singleton
    fun provideTokenCoordinator(legacyTransport: GrpcTransportFacade): TokenCoordinator =
        legacyTransport.tokenCoordinator()

    @Provides
    @Singleton
    fun provideSearchUsersGateway(implementation: GrpcSearchUsersGateway): SearchUsersGateway =
        implementation

    @Provides
    @Singleton
    fun provideServerDiscoveryGateway(legacyTransport: GrpcTransportFacade): ServerDiscoveryGateway =
        GrpcServerDiscoveryGateway(legacyTransport)

    @Provides
    @Singleton
    fun provideAuthGateway(
        legacyTransport: GrpcTransportFacade,
        @ApplicationContext context: Context,
    ): AuthGateway = GrpcAuthGateway(legacyTransport, context)

    @Provides
    @Singleton
    fun provideAccountSecurityGateway(legacyTransport: GrpcTransportFacade): AccountSecurityGateway =
        GrpcAccountSecurityGateway(legacyTransport)

    @Provides
    @Singleton
    fun provideUserProfileGateway(legacyTransport: GrpcTransportFacade): UserProfileGateway =
        GrpcUserProfileGateway(legacyTransport)

    @Provides
    @Singleton
    fun provideUserSettingsGateway(legacyTransport: GrpcTransportFacade): UserSettingsGateway =
        GrpcUserSettingsGateway(legacyTransport)

    @Provides
    @Singleton
    fun provideUserDirectoryGateway(legacyTransport: GrpcTransportFacade): UserDirectoryGateway =
        GrpcUserDirectoryGateway(legacyTransport)

    @Provides
    @Singleton
    fun providePresenceGateway(clientRegistry: GrpcClientRegistry): PresenceGateway =
        GrpcPresenceGateway(clientRegistry)

    @Provides
    @Singleton
    fun provideChatDirectoryGateway(legacyTransport: GrpcTransportFacade): ChatDirectoryGateway =
        GrpcChatDirectoryGateway(legacyTransport)

    @Provides
    @Singleton
    fun provideChatDraftGateway(chatRepository: ChatRepository): ChatDraftGateway =
        GrpcChatDraftGateway(chatRepository)

    @Provides
    @Singleton
    fun provideMessageGateway(
        chatRepository: ChatRepository,
        legacyTransport: GrpcTransportFacade,
    ): MessageGateway = GrpcMessageGateway(chatRepository, legacyTransport)

    @Provides
    @Singleton
    fun provideChatFolderGateway(legacyTransport: GrpcTransportFacade): ChatFolderGateway =
        GrpcChatFolderGateway(legacyTransport)

    @Provides
    @Singleton
    fun provideFileMediaGateway(chatRepository: ChatRepository): FileMediaGateway =
        GrpcFileMediaGateway(chatRepository)

    @Provides
    @Singleton
    fun provideStickerGateway(legacyTransport: GrpcTransportFacade): StickerGateway =
        GrpcStickerGateway(legacyTransport)

    @Provides
    @Singleton
    fun provideRealtimeGateway(realtimeService: RealtimeService): RealtimeGateway =
        GrpcRealtimeGateway(realtimeService)

    @Provides
    @Singleton
    fun provideFastAuthGateway(legacyTransport: GrpcTransportFacade): FastAuthGateway =
        GrpcFastAuthGateway(legacyTransport)

    @Provides
    @Singleton
    fun provideChatCacheRepository(@ApplicationContext context: Context): ChatCacheRepository =
        ChatCacheRepository(context)

    @Provides
    @Singleton
    fun provideChatDraftRepository(
        @ApplicationContext context: Context,
        chatCacheRepository: ChatCacheRepository,
        chatDraftGateway: ChatDraftGateway,
    ): ChatDraftRepository = ChatDraftRepository(context, chatCacheRepository, chatDraftGateway)

    @Provides
    @Singleton
    fun provideComposerAttachmentStore(
        @ApplicationContext context: Context,
        chatCacheRepository: ChatCacheRepository,
    ): ComposerAttachmentStore = ComposerAttachmentStore(context, chatCacheRepository)

    @Provides
    @Singleton
    fun provideRealtimeSideEffects(
        @ApplicationContext context: Context,
        legacyTransport: GrpcTransportFacade
    ): RealtimeSideEffects = RealtimeSideEffectsImpl(context, legacyTransport)

    @Provides
    @Singleton
    fun provideRealtimeService(
        @ApplicationContext context: Context,
        legacyTransport: GrpcTransportFacade,
        tokenCoordinator: TokenCoordinator,
        sideEffects: RealtimeSideEffects
    ): RealtimeService = RealtimeService(context, legacyTransport, tokenCoordinator, sideEffects)

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
        legacyTransport: GrpcTransportFacade,
        mediaTransport: MediaHttpTransport,
    ): ChatRepository = ChatRepository(context, legacyTransport, mediaTransport)

    @Provides
    @Singleton
    fun provideOutgoingMessageQueue(
        @ApplicationContext context: Context,
        chatCacheRepository: ChatCacheRepository,
        chatRepository: ChatRepository,
        tokenCoordinator: TokenCoordinator,
        composerAttachmentStore: ComposerAttachmentStore,
    ): OutgoingMessageQueue = OutgoingMessageQueue(
        context,
        chatCacheRepository,
        chatRepository,
        tokenCoordinator,
        composerAttachmentStore,
    )

    @Provides
    @Singleton
    fun providePrivateChatRepository(
        @ApplicationContext context: Context,
        legacyTransport: GrpcTransportFacade
    ): PrivateChatRepository = PrivateChatRepository(context, legacyTransport)

    @Provides
    @Singleton
    fun provideSecretChatRepository(
        @ApplicationContext context: Context,
        legacyTransport: GrpcTransportFacade,
        signalStore: BarkFluffSignalStore
    ): SecretChatRepository = SecretChatRepository(context, legacyTransport, signalStore)

    @Provides
    @Singleton
    fun provideCallRepository(legacyTransport: GrpcTransportFacade): CallRepository =
        CallRepository(legacyTransport)

    @Provides
    @Singleton
    fun provideCallGateway(callRepository: CallRepository): CallGateway =
        com.barkfluff.client.domain.gateway.GrpcCallGateway(callRepository)

    @Provides
    @Singleton
    fun provideCallEventsService(
        @ApplicationContext context: Context,
        legacyTransport: GrpcTransportFacade,
        callRepository: CallRepository,
        tokenCoordinator: TokenCoordinator,
    ): CallEventsService = CallEventsService(context, legacyTransport, callRepository, tokenCoordinator)
}
