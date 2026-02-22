//
//  UserProfilePanelViewModel.swift
//  Barkfluff
//
//  ViewModel для панели профиля пользователя/группы
//

import SwiftUI
import BFCore

/// ViewModel для панели профиля
@MainActor
@Observable
final class UserProfilePanelViewModel {

    // MARK: - Dependencies

    let fileService: FileServiceProtocol
    private let chat: Chat
    private let userService: UserServiceProtocol
    private let chatService: ChatServiceProtocol
    private let sharedMediaService: SharedMediaServiceProtocol

    // MARK: - Profile State

    private(set) var user: User?
    private(set) var isLoadingProfile = false
    private(set) var profileError: String?

    // MARK: - Computed Profile Properties

    var isGroupChat: Bool { chat.isGroupChat }

    var displayName: String {
        if chat.isGroupChat {
            return chat.title
        }
        return user?.displayName ?? chat.title
    }

    var username: String? {
        user?.username
    }

    /// URL превью аватара (для отображения в шапке)
    var avatarURL: String? {
        if chat.isGroupChat {
            return chat.pictureURL
        }
        return user?.profilePicturePreviewURL ?? chat.pictureURL
    }

    /// URL полноразмерного аватара (для просмотра в полном размере)
    var fullSizeAvatarURL: String? {
        if chat.isGroupChat {
            return chat.pictureURL
        }
        return user?.profilePictureURL ?? chat.pictureURL
    }

    var initials: String {
        if let user = user {
            return user.initials
        }
        return chat.avatarInitials
    }

    var bio: String? {
        user?.bio
    }

    var badges: [UserBadge] {
        user?.badges ?? []
    }

    var registrationDate: Date? {
        user?.registrationDate
    }

    var userID: Int64? {
        user?.id
    }

    var storageLimitGB: Int32? {
        // Конвертируем из байт в гигабайты
        guard let bytes = user?.storageLimitBytes, bytes > 0 else { return nil }
        return Int32(bytes / 1_073_741_824) // 1024^3
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
    private var lastMediaMessageID: Int64?
    private var lastDocumentMessageID: Int64?

    // MARK: - Init

    init(
        chat: Chat,
        userService: UserServiceProtocol,
        chatService: ChatServiceProtocol,
        sharedMediaService: SharedMediaServiceProtocol,
        fileService: FileServiceProtocol
    ) {
        self.chat = chat
        self.userService = userService
        self.chatService = chatService
        self.sharedMediaService = sharedMediaService
        self.fileService = fileService

        // Для групп — начальное кол-во участников из chat.members
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
                // Группа: загрузить участников
                let result = try await chatService.listChatMembers(
                    chatID: chat.id,
                    offset: 0,
                    size: 10
                )
                members = result.items
                memberCount = Int(result.totalCount)
            } else {
                // DM: определить ID собеседника из chat.members
                guard let otherMember = chat.members.first else {
                    profileError = "Не удалось определить собеседника"
                    isLoadingProfile = false
                    return
                }

                // Загрузить профиль
                let fetchedUser = try await userService.getUser(userID: otherMember.userID)
                user = fetchedUser

                // Загрузить все баджи
                let allBadges = try await userService.getUserBadges(userID: otherMember.userID)
                // Обновить user с полным списком баджей
                var updatedUser = fetchedUser
                updatedUser.badges = allBadges
                user = updatedUser
            }
        } catch {
            profileError = error.localizedDescription
        }

        isLoadingProfile = false
    }

    // MARK: - Load All Members (Group, pagination)

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

        // Сброс для текущего фильтра
        switch selectedMediaFilter {
        case .media:
            mediaItems = []
            lastMediaMessageID = nil
        case .documents:
            documentItems = []
            lastDocumentMessageID = nil
        }
        hasMoreMedia = true

        do {
            let items = try await sharedMediaService.loadSharedMedia(
                chatID: chat.id,
                beforeMessageID: nil,
                filter: selectedMediaFilter
            )

            switch selectedMediaFilter {
            case .media:
                mediaItems = items
                lastMediaMessageID = items.last?.messageID
            case .documents:
                documentItems = items
                lastDocumentMessageID = items.last?.messageID
            }

            hasMoreMedia = !items.isEmpty
        } catch {
            // Ошибка — оставить пустой список
        }

        isLoadingMedia = false
    }

    func loadMoreSharedMedia() async {
        guard !isLoadingMedia, hasMoreMedia else { return }
        isLoadingMedia = true

        let beforeID: Int64?
        switch selectedMediaFilter {
        case .media: beforeID = lastMediaMessageID
        case .documents: beforeID = lastDocumentMessageID
        }

        guard let beforeID else {
            isLoadingMedia = false
            return
        }

        do {
            let newItems = try await sharedMediaService.loadSharedMedia(
                chatID: chat.id,
                beforeMessageID: beforeID,
                filter: selectedMediaFilter
            )

            switch selectedMediaFilter {
            case .media:
                mediaItems.append(contentsOf: newItems)
                lastMediaMessageID = newItems.last?.messageID
            case .documents:
                documentItems.append(contentsOf: newItems)
                lastDocumentMessageID = newItems.last?.messageID
            }

            hasMoreMedia = !newItems.isEmpty
        } catch {
            hasMoreMedia = false
        }

        isLoadingMedia = false
    }
}
