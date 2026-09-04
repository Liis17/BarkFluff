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
import com.barkfluff.client.grpc.GrpcApiTransport
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
import com.barkfluff.client.domain.gateway.GrpcPrekeyGateway
import com.barkfluff.client.domain.gateway.GrpcPrivateChatGateway
import com.barkfluff.client.domain.gateway.GrpcRealtimeGateway
import com.barkfluff.client.domain.gateway.GrpcServerDiscoveryGateway
import com.barkfluff.client.domain.gateway.GrpcSecretChatGateway
import com.barkfluff.client.domain.gateway.GrpcStickerGateway
import com.barkfluff.client.domain.gateway.GrpcUserDirectoryGateway
import com.barkfluff.client.domain.gateway.GrpcUserProfileGateway
import com.barkfluff.client.domain.gateway.GrpcUserSettingsGateway
import com.barkfluff.client.domain.gateway.MessageGateway
import com.barkfluff.client.domain.gateway.PrekeyGateway
import com.barkfluff.client.domain.gateway.PrivateChatGateway
import com.barkfluff.client.domain.gateway.RealtimeGateway
import com.barkfluff.client.domain.gateway.ServerDiscoveryGateway
import com.barkfluff.client.domain.gateway.SecretChatGateway
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
 * Composition root: one typed client registry, transport policy, domain gateways, realtime,
 * durable composer and E2E repositories. UI components receive ports rather than RPC stubs.
 */
@Module
@InstallIn(SingletonComponent::class)
object AppModule {

    @Provides
    @Singleton
    fun provideGrpcApiTransport(@ApplicationContext context: Context): GrpcApiTransport =
        GrpcApiTransport(context)

    @Provides
    @Singleton
    fun provideGrpcClientRegistry(transport: GrpcApiTransport): GrpcClientRegistry =
        transport.clientRegistry()

    @Provides
    @Singleton
    fun provideMediaHttpTransport(@ApplicationContext context: Context): MediaHttpTransport =
        MediaHttpTransport(context)

    @Provides
    @Singleton
    fun provideTokenCoordinator(transport: GrpcApiTransport): TokenCoordinator =
        transport.tokenCoordinator()

    @Provides
    @Singleton
    fun provideSearchUsersGateway(implementation: GrpcSearchUsersGateway): SearchUsersGateway =
        implementation

    @Provides
    @Singleton
    fun provideServerDiscoveryGateway(transport: GrpcApiTransport): ServerDiscoveryGateway =
        GrpcServerDiscoveryGateway(transport)

    @Provides
    @Singleton
    fun provideAuthGateway(
        transport: GrpcApiTransport,
        @ApplicationContext context: Context,
    ): AuthGateway = GrpcAuthGateway(transport, context)

    @Provides
    @Singleton
    fun provideAccountSecurityGateway(transport: GrpcApiTransport): AccountSecurityGateway =
        GrpcAccountSecurityGateway(transport)

    @Provides
    @Singleton
    fun provideUserProfileGateway(transport: GrpcApiTransport): UserProfileGateway =
        GrpcUserProfileGateway(transport)

    @Provides
    @Singleton
    fun provideUserSettingsGateway(transport: GrpcApiTransport): UserSettingsGateway =
        GrpcUserSettingsGateway(transport)

    @Provides
    @Singleton
    fun provideUserDirectoryGateway(transport: GrpcApiTransport): UserDirectoryGateway =
        GrpcUserDirectoryGateway(transport)

    @Provides
    @Singleton
    fun providePresenceGateway(clientRegistry: GrpcClientRegistry): PresenceGateway =
        GrpcPresenceGateway(clientRegistry)

    @Provides
    @Singleton
    fun provideChatDirectoryGateway(transport: GrpcApiTransport): ChatDirectoryGateway =
        GrpcChatDirectoryGateway(transport)

    @Provides
    @Singleton
    fun provideChatDraftGateway(chatRepository: ChatRepository): ChatDraftGateway =
        GrpcChatDraftGateway(chatRepository)

    @Provides
    @Singleton
    fun provideMessageGateway(
        chatRepository: ChatRepository,
        transport: GrpcApiTransport,
    ): MessageGateway = GrpcMessageGateway(chatRepository, transport)

    @Provides
    @Singleton
    fun provideChatFolderGateway(transport: GrpcApiTransport): ChatFolderGateway =
        GrpcChatFolderGateway(transport)

    @Provides
    @Singleton
    fun provideFileMediaGateway(chatRepository: ChatRepository): FileMediaGateway =
        GrpcFileMediaGateway(chatRepository)

    @Provides
    @Singleton
    fun provideStickerGateway(transport: GrpcApiTransport): StickerGateway =
        GrpcStickerGateway(transport)

    @Provides
    @Singleton
    fun provideRealtimeGateway(realtimeService: RealtimeService): RealtimeGateway =
        GrpcRealtimeGateway(realtimeService)

    @Provides
    @Singleton
    fun provideFastAuthGateway(transport: GrpcApiTransport): FastAuthGateway =
        GrpcFastAuthGateway(transport)

    @Provides
    @Singleton
    fun providePrekeyGateway(transport: GrpcApiTransport): PrekeyGateway =
        GrpcPrekeyGateway(transport)

    @Provides
    @Singleton
    fun providePrivateChatGateway(repository: PrivateChatRepository): PrivateChatGateway =
        GrpcPrivateChatGateway(repository)

    @Provides
    @Singleton
    fun provideSecretChatGateway(repository: SecretChatRepository): SecretChatGateway =
        GrpcSecretChatGateway(repository)

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
        userProfileGateway: UserProfileGateway,
        fileMediaGateway: FileMediaGateway,
    ): RealtimeSideEffects = RealtimeSideEffectsImpl(context, userProfileGateway, fileMediaGateway)

    @Provides
    @Singleton
    fun provideRealtimeService(
        @ApplicationContext context: Context,
        clientRegistry: GrpcClientRegistry,
        tokenCoordinator: TokenCoordinator,
        messageGateway: MessageGateway,
        sideEffects: RealtimeSideEffects
    ): RealtimeService = RealtimeService(context, clientRegistry, tokenCoordinator, messageGateway, sideEffects)

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
        transport: GrpcApiTransport,
        mediaTransport: MediaHttpTransport,
    ): ChatRepository = ChatRepository(context, transport, mediaTransport)

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
        transport: GrpcApiTransport
    ): PrivateChatRepository = PrivateChatRepository(context, transport)

    @Provides
    @Singleton
    fun provideSecretChatRepository(
        @ApplicationContext context: Context,
        transport: GrpcApiTransport,
        signalStore: BarkFluffSignalStore
    ): SecretChatRepository = SecretChatRepository(context, transport, signalStore)

    @Provides
    @Singleton
    fun provideCallRepository(clientRegistry: GrpcClientRegistry): CallRepository =
        CallRepository(clientRegistry)

    @Provides
    @Singleton
    fun provideCallGateway(callRepository: CallRepository): CallGateway =
        com.barkfluff.client.domain.gateway.GrpcCallGateway(callRepository)

    @Provides
    @Singleton
    fun provideCallEventsService(
        @ApplicationContext context: Context,
        clientRegistry: GrpcClientRegistry,
        callRepository: CallRepository,
        tokenCoordinator: TokenCoordinator,
    ): CallEventsService = CallEventsService(context, clientRegistry, callRepository, tokenCoordinator)
}
