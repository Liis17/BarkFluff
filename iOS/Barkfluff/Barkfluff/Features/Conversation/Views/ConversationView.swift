//
//  ConversationView.swift
//  Barkfluff
//
//  Экран переписки (iOS версия)
//  Полноценный мессенджер с поддержкой текста и медиа
//

import SwiftUI
import UIKit
import BFCore
import PhotosUI
import Nuke
import NukeUI

struct ConversationView: View {
    let chat: Chat

    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    @State private var viewModel: ConversationViewModel?
    @State private var messageText = ""
    @State private var scrollPosition = ScrollPositionManager()
    @State private var selectedAttachments: [SelectedAttachment] = []

    @Environment(\.locale) private var locale

    // Для просмотра медиа
    @State private var selectedMediaAttachment: MessageAttachment?
    @State private var allMediaInMessage: [MessageAttachment] = []

    // Подтверждение удаления
    @State private var deleteCandidateID: Int64?

    // Sheet пикера стикеров
    @State private var showStickerPicker = false

    // MARK: - Constants

    private let maxFileSize: Int64 = 500_000_000  // 500 MB

    var body: some View {
        ZStack {
            // Слой 0: Фон чата (персонализация)
            ChatBackgroundView()
                .ignoresSafeArea()

            // Слой 1: Список сообщений
            if let viewModel {
                messagesList(viewModel: viewModel)
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }

            // Слой 2: Кнопка "вниз"
            if scrollPosition.showScrollToBottom {
                VStack {
                    Spacer()
                    ScrollToBottomButton(
                        unreadCount: scrollPosition.unreadCount
                    ) {
                        scrollPosition.scrollToBottom()
                    }
                    .padding(.bottom, 80)
                }
            }

            // Ошибка (если есть)
            if let viewModel, let error = viewModel.errorMessage {
                VStack {
                    Spacer()
                    HStack {
                        Spacer()
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .padding(Theme.Spacing.sm)
                            .background(.ultraThinMaterial)
                            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))
                        Spacer()
                    }
                    .padding(.bottom, 100)
                }
            }
        }
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            // Заголовок с информацией о чате (только текст, без аватарки)
            ToolbarItem(placement: .principal) {
                VStack(spacing: 2) {
                    Text(chat.title)
                        .font(.headline)
                        .lineLimit(1)

                    if !chat.isGroupChat {
                        onlineStatusText
                    }
                }
            }

            // Аватарка справа — открывает профиль собеседника / инфо группы
            ToolbarItem(placement: .primaryAction) {
                Button {
                    coordinator.openUserProfile(for: chat)
                } label: {
                    AvatarView(
                        imageURL: chat.pictureURL,
                        initials: chat.avatarInitials,
                        size: 32
                    )
                }
            }
        }
        .toolbar(.hidden, for: .tabBar) // Скрываем таб-бар в диалоге
        .safeAreaInset(edge: .bottom) {
            if let viewModel {
                VStack(spacing: 8) {
                    if let editing = viewModel.editingMessage {
                        EditPreviewView(
                            snippet: ReplyPreviewView.makeSnippet(editing, locale: locale),
                            onCancel: {
                                viewModel.cancelEdit()
                                messageText = ""
                            }
                        )
                        .padding(.horizontal, 8)
                    } else if let reply = viewModel.pendingReply {
                        ReplyPreviewView(
                            authorName: reply.senderName ?? String(localized: "common.unknown_user"),
                            snippet: ReplyPreviewView.makeSnippet(reply, locale: locale),
                            onCancel: { viewModel.clearPendingReply() }
                        )
                        .padding(.horizontal, 8)
                    }

                    MessageInputView(
                        text: $messageText,
                        selectedAttachments: $selectedAttachments,
                        isSending: viewModel.isSendingAttachments,
                        uploadProgress: viewModel.uploadProgress,
                        onSend: { sendMessage(viewModel: viewModel) },
                        onFileSelected: { urls, forceAsDocument in
                            for url in urls {
                                do {
                                    try addAttachment(.fileURL(url: url, forceAsDocument: forceAsDocument))
                                } catch {
                                    viewModel.uploadError = error.localizedDescription
                                }
                            }
                        },
                        onStickerTap: { showStickerPicker = true }
                    )
                }
            }
        }
        .confirmationDialog(
            "conversation.delete.title",
            isPresented: Binding(
                get: { deleteCandidateID != nil },
                set: { if !$0 { deleteCandidateID = nil } }
            ),
            titleVisibility: .visible
        ) {
            Button("conversation.delete.button", role: .destructive) {
                if let id = deleteCandidateID {
                    Task { await viewModel?.deleteMessage(messageID: id) }
                }
                deleteCandidateID = nil
            }
            Button("common.cancel", role: .cancel) {
                deleteCandidateID = nil
            }
        } message: {
            Text("conversation.delete.message")
        }
        .task {
            if viewModel == nil {
                let vm = createViewModel(for: chat)
                viewModel = vm
                await vm.loadMessages()
                await vm.startListeningForUpdates()
                if !chat.isNewConversation {
                    coordinator.notifyChatOpened(chat.id)
                }
            }
        }
        .onDisappear {
            viewModel?.stopListeningForUpdates()
        }
        .onChange(of: chat.id) { _, _ in
            scrollPosition.reset()
            viewModel?.stopListeningForUpdates()
            viewModel = nil
            selectedAttachments = []

            Task {
                let vm = createViewModel(for: chat)
                viewModel = vm
                await vm.loadMessages()
                await vm.startListeningForUpdates()
                if !chat.isNewConversation {
                    coordinator.notifyChatOpened(chat.id)
                }
            }
        }
        // Full-screen media viewer
        .fullScreenCover(item: $selectedMediaAttachment) { attachment in
            MediaViewerView(
                attachments: allMediaInMessage,
                initialAttachment: attachment,
                fileService: container.fileService
            )
        }
        // Bottom-sheet пикера стикеров
        .sheet(isPresented: $showStickerPicker) {
            StickerPickerView(
                service: container.stickersService,
                recentStore: container.recentStickersStore,
                onStickerSelected: { sticker in
                    Task { await viewModel?.sendSticker(sticker) }
                    // Sheet остаётся открытым — Android-style, можно слать несколько подряд
                }
            )
            .presentationDetents([.medium, .large])
            .presentationDragIndicator(.hidden)
            .presentationBackgroundInteraction(.enabled(upThrough: .medium))
        }
    }

    // MARK: - Online Status

    @ViewBuilder
    private var onlineStatusText: some View {
        if let status = viewModel?.otherUserOnlineStatus {
            switch status {
            case .online:
                Text("conversation.status.online")
                    .font(.caption2)
                    .foregroundStyle(.green)
            case .offline(let lastSeen):
                if let lastSeen {
                    Text("conversation.status.last_seen \(formatLastSeen(lastSeen))")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                } else {
                    Text("conversation.status.offline")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }
            case .unknown:
                EmptyView()
            }
        }
    }

    private func formatLastSeen(_ date: Date) -> String {
        let calendar = Calendar.current

        if calendar.isDateInToday(date) {
            let formatter = DateFormatter()
            formatter.locale = locale
            formatter.dateFormat = "HH:mm"
            return String(localized: "conversation.status.last_seen.today \(formatter.string(from: date))")
        } else if calendar.isDateInYesterday(date) {
            return String(localized: "conversation.status.last_seen.yesterday")
        } else {
            let formatter = DateFormatter()
            formatter.locale = locale
            formatter.dateFormat = "d MMM"
            return formatter.string(from: date)
        }
    }

    // MARK: - Subviews

    @ViewBuilder
    private func messagesList(viewModel: ConversationViewModel) -> some View {
        if viewModel.isLoading && viewModel.messages.isEmpty {
            ProgressView()
                .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if viewModel.messages.isEmpty {
            ContentUnavailableView(
                viewModel.isNewConversation
                    ? LocalizedStringKey("conversation.empty.new.title")
                    : LocalizedStringKey("conversation.empty.no_messages.title"),
                systemImage: "bubble.left.and.bubble.right",
                description: Text(viewModel.isNewConversation
                    ? "conversation.empty.new.description"
                    : "conversation.empty.no_messages.description")
            )
        } else {
            MessagesListView(
                items: viewModel.listItems,
                currentUserID: viewModel.currentUserID,
                isGroupChat: chat.isGroupChat,
                isLoadingMore: viewModel.isLoadingMore,
                firstUnreadMessageID: viewModel.firstUnreadMessageID,
                onLoadMore: {
                    Task { await viewModel.loadMoreMessages() }
                },
                scrollPosition: scrollPosition,
                onRetry: { localID in
                    viewModel.retryFailedMessage(localID: localID)
                },
                onDeleteFailed: { localID in
                    viewModel.deleteFailedMessage(localID: localID)
                },
                onAttachmentTap: { attachment, allAttachments in
                    selectedMediaAttachment = attachment
                    allMediaInMessage = allAttachments
                },
                onReply: { message in
                    viewModel.setPendingReply(message)
                },
                onForward: { messageID in
                    coordinator.presentedSheet = .forwardMessage(
                        messageID: messageID,
                        sourceChatID: chat.id
                    )
                },
                onEdit: { message in
                    viewModel.enterEditMode(message)
                    messageText = message.content.text
                },
                onDelete: { messageID in
                    deleteCandidateID = messageID
                },
                onCopyText: { text in
                    UIPasteboard.general.string = text
                },
                onSaveImages: { images in
                    Task { await MediaActions.saveImages(images, container: container) }
                },
                onCopyImage: { image in
                    Task { await MediaActions.copyImageToPasteboard(image, container: container) }
                },
                onSaveDocuments: { docs in
                    Task { await MediaActions.saveDocuments(docs, container: container) }
                }
            )
        }
    }

    // MARK: - Actions

    private func sendMessage(viewModel: ConversationViewModel) {
        let trimmedText = messageText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedText.isEmpty || !selectedAttachments.isEmpty else { return }

        // Если в режиме редактирования — отправляем submit edit
        if viewModel.editingMessage != nil {
            let text = messageText
            messageText = ""
            Task { await viewModel.submitEdit(text: text) }
            return
        }

        let text = messageText
        let attachments = selectedAttachments

        // Очистка UI сразу (optimistic)
        messageText = ""
        selectedAttachments = []

        Task {
            await viewModel.sendMessageWithAttachments(
                text: text,
                attachments: attachments
            )
        }
    }

    // MARK: - ViewModel Factory

    private func createViewModel(for chat: Chat) -> ConversationViewModel {
        let vm = ConversationViewModel(
            chat: chat,
            messageService: container.messageService,
            updatesService: container.updatesService,
            onlineStatusService: container.onlineStatusService,
            fileService: container.fileService,
            currentUserID: container.currentUserID,
            chatService: chat.isNewConversation ? container.chatService : nil,
            localMessageRepository: container.localMessageRepository
        )
        vm.onMessageSent = { [weak coordinator] message in
            coordinator?.notifyMessageSent(chatID: chat.id, message: message)
        }
        vm.onChatResolved = { [weak coordinator] resolvedChat in
            coordinator?.selectedChat = resolvedChat
        }
        return vm
    }

    // MARK: - Attachment Helpers

    private func addAttachment(_ attachment: SelectedAttachment) throws {
        // Проверка размера файла
        if let size = attachment.fileSize, size > maxFileSize {
            throw AttachmentError.fileTooLarge(maxSize: maxFileSize)
        }
        selectedAttachments.append(attachment)
    }
}

// MARK: - Media Viewer View

/// Просмотрщик медиа с загрузкой через fileService
struct MediaViewerView: View {
    let attachments: [MessageAttachment]
    let initialAttachment: MessageAttachment
    let fileService: FileServiceProtocol

    @Environment(\.dismiss) private var dismiss
    @State private var currentIndex: Int = 0

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            TabView(selection: $currentIndex) {
                ForEach(Array(attachments.enumerated()), id: \.element.id) { index, attachment in
                    MediaItemView(attachment: attachment, fileService: fileService)
                        .tag(index)
                }
            }
            .tabViewStyle(.page(indexDisplayMode: .automatic))

            // Close button
            VStack {
                HStack {
                    Spacer()
                    Button {
                        dismiss()
                    } label: {
                        Image(systemName: "xmark.circle.fill")
                            .font(.largeTitle)
                            .foregroundStyle(.white)
                    }
                    .padding()
                }
                Spacer()
            }
        }
        .onAppear {
            currentIndex = attachments.firstIndex(of: initialAttachment) ?? 0
        }
    }
}

/// Отдельный элемент медиа с асинхронной загрузкой через Nuke (full screen)
struct MediaItemView: View {
    let attachment: MessageAttachment
    let fileService: FileServiceProtocol

    @State private var imageURL: URL?
    @State private var isLoading = true
    @State private var hasError = false

    /// Для full screen используем оригинальный fileID, не превью
    private var fileID: String {
        attachment.fileID
    }

    var body: some View {
        Group {
            if let url = imageURL {
                LazyImage(url: url) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fit)
                    } else if state.isLoading {
                        loadingView
                    } else {
                        errorView
                    }
                }
            } else if isLoading {
                loadingView
            } else {
                errorView
            }
        }
        .task(id: fileID) {
            await loadURL()
        }
    }

    private func loadURL() async {
        // Проверяем кэш URL
        if let cached = URLCacheShared.cachedURL(fileID: fileID) {
            imageURL = cached
            isLoading = false
            return
        }

        do {
            let urlString = try await fileService.getDownloadURL(fileID: fileID)
            if let url = URL(string: urlString) {
                imageURL = url
                URLCacheShared.storeURL(url, for: fileID)
            }
        } catch {
            hasError = true
        }
        isLoading = false
    }

    private var loadingView: some View {
        ZStack {
            Color.gray.opacity(0.3)
            ProgressView()
                .controlSize(.large)
                .tint(.white)
        }
    }

    private var errorView: some View {
        ZStack {
            Color.gray.opacity(0.3)
            VStack(spacing: 12) {
                Image(systemName: "photo")
                    .font(.system(size: 48))
                    .foregroundStyle(.white.opacity(0.7))
                Text("conversation.media.load_failed")
                    .font(.subheadline)
                    .foregroundStyle(.white.opacity(0.7))
            }
        }
    }
}

// MARK: - Shared URL Cache for MediaItemView

/// Простой кэш для URL по fileID (глобальный)
private class URLCacheShared {
    static let shared = URLCacheShared()
    private var cache: [String: URL] = [:]
    private let lock = NSLock()

    class func cachedURL(fileID: String) -> URL? {
        shared.lock.lock()
        defer { shared.lock.unlock() }
        return shared.cache[fileID]
    }

    class func storeURL(_ url: URL, for fileID: String) {
        shared.lock.lock()
        defer { shared.lock.unlock() }
        shared.cache[fileID] = url
    }
}

#Preview {
    NavigationStack {
        ConversationView(chat: Chat(
            id: "test",
            title: "Тестовый чат",
            isGroupChat: false,
            members: [
                ChatMember(
                    userID: 2,
                    username: "test",
                    firstName: "Тест",
                    lastName: "",
                    role: .member
                )
            ]
        ))
    }
    .environment(AppCoordinator())
    .environment(DependencyContainer())
}
