//
//  ConversationView.swift
//  Barkfluff
//
//  Экран переписки (iOS версия)
//  Полноценный мессенджер с поддержкой текста и медиа
//

import SwiftUI
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

    // Для просмотра медиа
    @State private var selectedMediaAttachment: MessageAttachment?
    @State private var allMediaInMessage: [MessageAttachment] = []

    // MARK: - Constants

    private let maxFileSize: Int64 = 500_000_000  // 500 MB

    var body: some View {
        ZStack {
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

            // Аватарка справа
            ToolbarItem(placement: .primaryAction) {
                Button {
                    // TODO: Открыть информацию о чате
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
                    }
                )
            }
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
    }

    // MARK: - Online Status

    @ViewBuilder
    private var onlineStatusText: some View {
        if let status = viewModel?.otherUserOnlineStatus {
            switch status {
            case .online:
                Text("онлайн")
                    .font(.caption2)
                    .foregroundStyle(.green)
            case .offline(let lastSeen):
                Text(lastSeen.map { "был(а) \(formatLastSeen($0))" } ?? "не в сети")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            case .unknown:
                EmptyView()
            }
        }
    }

    private func formatLastSeen(_ date: Date) -> String {
        let calendar = Calendar.current

        if calendar.isDateInToday(date) {
            let formatter = DateFormatter()
            formatter.dateFormat = "HH:mm"
            return "в \(formatter.string(from: date))"
        } else if calendar.isDateInYesterday(date) {
            return "вчера"
        } else {
            let formatter = DateFormatter()
            formatter.locale = Locale(identifier: "ru_RU")
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
                viewModel.isNewConversation ? "Новый диалог" : "Нет сообщений",
                systemImage: "bubble.left.and.bubble.right",
                description: Text(viewModel.isNewConversation
                    ? "Напишите первое сообщение!"
                    : "Начните диалог!")
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
                }
            )
        }
    }

    // MARK: - Actions

    private func sendMessage(viewModel: ConversationViewModel) {
        let trimmedText = messageText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedText.isEmpty || !selectedAttachments.isEmpty else { return }

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
            chatService: chat.isNewConversation ? container.chatService : nil
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
                Text("Не удалось загрузить")
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
