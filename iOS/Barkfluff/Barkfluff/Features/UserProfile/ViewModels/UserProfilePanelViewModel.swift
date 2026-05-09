//
//  UserProfilePanelViewModel.swift
//  Barkfluff (iOS)
//
//  ViewModel для экрана профиля собеседника / инфо группы.
//  Логика идентична macOS-клиенту — push-навигация в NavigationStack.
//

import SwiftUI
import BFCore

@MainActor
@Observable
final class UserProfilePanelViewModel {

    // MARK: - Dependencies

    let fileService: FileServiceProtocol
    private let chat: Chat
    private let currentUserID: Int64
    private let userService: UserServiceProtocol
    private let chatService: ChatServiceProtocol
    private let sharedMediaService: SharedMediaServiceProtocol
    private let onlineStatusService: OnlineStatusServiceProtocol

    // MARK: - Profile State

    private(set) var user: User?
    private(set) var isLoadingProfile = false
    private(set) var profileError: String?

    // MARK: - Online Status

    var onlineStatus: OnlineStatus = .unknown
    private var onlineStatusTask: Task<Void, Never>?
    private var trackedOnlineUserID: Int64?

    // MARK: - Computed Profile Properties

    var isGroupChat: Bool { chat.isGroupChat }

    var displayName: String {
        if chat.isGroupChat {
            return chat.title
        }
        return user?.displayName ?? chat.title
    }

    var username: String? { user?.username }

    var avatarURL: String? {
        if chat.isGroupChat {
            return chat.pictureURL
        }
        return user?.profilePicturePreviewURL ?? chat.pictureURL
    }

    var fullSizeAvatarURL: String? {
        if chat.isGroupChat {
            return chat.pictureURL
        }
        return user?.profilePictureURL ?? chat.pictureURL
    }

    var posterFileID: String? {
        guard !chat.isGroupChat else { return nil }
        return user?.profilePosterFileID
    }

    var initials: String {
        if let user = user {
            return user.initials
        }
        return chat.avatarInitials
    }

    var bio: String? { user?.bio }

    var badges: [UserBadge] { user?.badges ?? [] }

    var registrationDate: Date? { user?.registrationDate }

    var userID: Int64? { user?.id }

    var storageLimitGB: Int32? {
        guard let bytes = user?.storageLimitBytes, bytes > 0 else { return nil }
        return Int32(bytes / 1_073_741_824)
    }

    // MARK: - Members State (Group Chats)

    private(set) var members: [DetailedChatMember] = []
    private(set) var memberCount: Int = 0
    private(set) var isLoadingMembers = false

    // MARK: - Shared Media State

    var selectedMediaFilter: SharedMediaFilter = .media
    private(set) var mediaItems: [SharedMediaItem] = []
    private(set) var documentItems: [SharedMediaItem] = []
    private(set) var isLoadingMedia = false
    private(set) var hasMoreMedia = true
    private var mediaOffset: Int32 = 0
    private var documentOffset: Int32 = 0

    // MARK: - Init

    init(
        chat: Chat,
        currentUserID: Int64,
        userService: UserServiceProtocol,
        chatService: ChatServiceProtocol,
        sharedMediaService: SharedMediaServiceProtocol,
        fileService: FileServiceProtocol,
        onlineStatusService: OnlineStatusServiceProtocol
    ) {
        self.chat = chat
        self.currentUserID = currentUserID
        self.userService = userService
        self.chatService = chatService
        self.sharedMediaService = sharedMediaService
        self.fileService = fileService
        self.onlineStatusService = onlineStatusService

        if chat.isGroupChat {
            self.memberCount = chat.members.count
        }
    }

    // MARK: - Load Profile

    func loadProfile() async {
        isLoadingProfile = true
        profileError = nil

        do {
            if chat.isGroupChat {
                let result = try await chatService.listChatMembers(
                    chatID: chat.id,
                    offset: 0,
                    size: 10
                )
                members = result.items
                memberCount = Int(result.totalCount)
            } else {
                guard let otherMember = chat.members.first(where: { $0.userID != currentUserID }) else {
                    profileError = "Не удалось определить собеседника"
                    isLoadingProfile = false
                    return
                }

                let fetchedUser = try await userService.getUser(userID: otherMember.userID)
                user = fetchedUser

                let allBadges = try await userService.getUserBadges(userID: otherMember.userID)
                var updatedUser = fetchedUser
                updatedUser.badges = allBadges
                user = updatedUser

                await startListeningForOnlineStatus(userID: otherMember.userID)
            }
        } catch {
            profileError = error.localizedDescription
        }

        isLoadingProfile = false
    }

    // MARK: - Online Status

    private func startListeningForOnlineStatus(userID: Int64) async {
        let status = await onlineStatusService.currentStatus(for: userID)
        self.onlineStatus = status

        await onlineStatusService.track(userID)
        trackedOnlineUserID = userID

        let refreshed = await onlineStatusService.currentStatus(for: userID)
        self.onlineStatus = refreshed

        onlineStatusTask = Task { [weak self, onlineStatusService] in
            let stream = await onlineStatusService.statusStream(for: userID)
            for await newStatus in stream {
                guard let self else { break }
                await MainActor.run { self.onlineStatus = newStatus }
            }
        }
    }

    func stopListeningForOnlineStatus() {
        onlineStatusTask?.cancel()
        onlineStatusTask = nil

        if let userID = trackedOnlineUserID {
            let service = onlineStatusService
            Task { await service.untrack(userID) }
            trackedOnlineUserID = nil
        }
    }

    // MARK: - Load All Members

    func loadAllMembers() async {
        guard !isLoadingMembers else { return }
        isLoadingMembers = true

        do {
            let result = try await chatService.listChatMembers(
                chatID: chat.id,
                offset: Int32(members.count),
                size: 50
            )
            members.append(contentsOf: result.items)
            memberCount = Int(result.totalCount)
        } catch {
            // Ошибка — показать существующие
        }

        isLoadingMembers = false
    }

    // MARK: - Shared Media

    func loadSharedMedia() async {
        isLoadingMedia = true

        switch selectedMediaFilter {
        case .media:
            mediaItems = []
            mediaOffset = 0
        case .documents:
            documentItems = []
            documentOffset = 0
        }
        hasMoreMedia = true

        do {
            let result = try await sharedMediaService.loadSharedMedia(
                chatID: chat.id,
                offset: 0,
                filter: selectedMediaFilter
            )

            switch selectedMediaFilter {
            case .media:
                mediaItems = result.items
                mediaOffset = Int32(result.items.count)
            case .documents:
                documentItems = result.items
                documentOffset = Int32(result.items.count)
            }

            hasMoreMedia = result.hasMore
        } catch {
            // Ошибка — оставить пустой список
        }

        isLoadingMedia = false
    }

    func loadMoreSharedMedia() async {
        guard !isLoadingMedia, hasMoreMedia else { return }
        isLoadingMedia = true

        let currentOffset: Int32
        switch selectedMediaFilter {
        case .media: currentOffset = mediaOffset
        case .documents: currentOffset = documentOffset
        }

        do {
            let result = try await sharedMediaService.loadSharedMedia(
                chatID: chat.id,
                offset: currentOffset,
                filter: selectedMediaFilter
            )

            switch selectedMediaFilter {
            case .media:
                mediaItems.append(contentsOf: result.items)
                mediaOffset += Int32(result.items.count)
            case .documents:
                documentItems.append(contentsOf: result.items)
                documentOffset += Int32(result.items.count)
            }

            hasMoreMedia = result.hasMore
        } catch {
            hasMoreMedia = false
        }

        isLoadingMedia = false
    }
}
