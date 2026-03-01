//
//  ConversationView.swift
//  Barkfluff
//
//  Экран переписки в стиле iMessage
//  ZStack с плавающими заголовком и полем ввода
//

import SwiftUI
import BFCore
import UniformTypeIdentifiers

// MARK: - PreferenceKeys для измерения высот оверлеев

struct HeaderHeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = nextValue()
    }
}

struct InputHeightKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = nextValue()
    }
}

struct ConversationView: View {
    let chat: Chat

    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ConversationViewModel?
    @State private var messageText = ""
    @State private var scrollPosition = ScrollPositionManager()
    @State private var headerHeight: CGFloat = 0
    @State private var inputHeight: CGFloat = 0

    // MARK: - Attachment States

    @State private var selectedAttachments: [SelectedAttachment] = []
    @State private var isDragOver = false

    /// Монитор для перехвата Cmd+V
    @State private var pasteMonitor: Any?

    // MARK: - Constants

    private let maxFileSize: Int64 = 500_000_000  // 500 MB

    var body: some View {
        ZStack {
            // Слой 1: Список сообщений (полная область)
            if let viewModel {
                messagesList(viewModel: viewModel)
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }

            // Слой 2: Плавающий заголовок сверху
            VStack(spacing: 0) {
                ConversationHeaderView(
                    chat: chat,
                    onlineStatus: viewModel?.otherUserOnlineStatus ?? .unknown
                )
                .background(
                    GeometryReader { geo in
                        Color.clear.preference(
                            key: HeaderHeightKey.self,
                            value: geo.size.height
                        )
                    }
                )
                Spacer()
            }

            // Слой 3: Плавающий ввод снизу
            VStack(spacing: 0) {
                Spacer()
                MessageInputView(
                    text: $messageText,
                    selectedAttachments: $selectedAttachments,
                    isSending: viewModel?.isSendingAttachments ?? false,
                    uploadProgress: viewModel?.uploadProgress ?? [:],
                    onSend: { sendMessage() },
                    onFileSelected: { urls, forceAsDocument in
                        for url in urls {
                            do {
                                try addAttachment(.fileURL(url: url, forceAsDocument: forceAsDocument))
                            } catch {
                                viewModel?.uploadError = error.localizedDescription
                            }
                        }
                    }
                )
                .background(
                    GeometryReader { geo in
                        Color.clear.preference(
                            key: InputHeightKey.self,
                            value: geo.size.height
                        )
                    }
                )

                // Ошибка загрузки файлов
                if let viewModel, let error = viewModel.uploadError {
                    HStack {
                        Spacer()
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.red)
                            .padding(.horizontal, Theme.Spacing.md)
                            .padding(.vertical, Theme.Spacing.xs)
                            .background(.ultraThinMaterial)
                            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.sm))
                        Spacer()
                    }
                    .padding(.bottom, Theme.Spacing.xs)
                }
            }

            // Слой 4: Кнопка "вниз" (над вводом)
            if scrollPosition.showScrollToBottom {
                VStack {
                    Spacer()
                    ScrollToBottomButton(
                        unreadCount: scrollPosition.unreadCount
                    ) {
                        scrollPosition.scrollToBottom()
                    }
                    .padding(.bottom, inputHeight + Theme.Spacing.md)
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
                    .padding(.bottom, inputHeight + Theme.Spacing.lg)
                }
            }

            // Drag & Drop overlay
            if isDragOver {
                DragDropOverlay()
                    .transition(.opacity)
            }
        }
        .ignoresSafeArea(edges: .top)
        .toolbarBackground(.hidden, for: .windowToolbar)
        .onPreferenceChange(HeaderHeightKey.self) { headerHeight = $0 }
        .onPreferenceChange(InputHeightKey.self) { inputHeight = $0 }
        // Обработка нажатия на медиа-вложение
        .onReceive(NotificationCenter.default.publisher(for: .attachmentTapped)) { notification in
            if let attachment = notification.userInfo?["attachment"] as? MessageAttachment,
               let allAttachments = notification.userInfo?["allAttachments"] as? [MessageAttachment] {
                let index = allAttachments.firstIndex(of: attachment) ?? 0
                let messageText = notification.userInfo?["messageText"] as? String
                FullScreenMediaWindowManager.shared.openMediaViewer(
                    attachments: allAttachments,
                    initialIndex: index,
                    messageText: messageText,
                    container: container
                )
            }
        }
        // Обработка нажатия на документ/аудио — скачивание
        .onReceive(NotificationCenter.default.publisher(for: .documentDownloadRequested)) { notification in
            if let attachment = notification.userInfo?["attachment"] as? MessageAttachment {
                Task {
                    try? await FileDownloadHelper.downloadToDownloads(
                        fileID: attachment.fileID,
                        fileName: attachment.fileName,
                        fileService: container.fileService
                    )
                }
            }
        }
        // При возврате фокуса в приложение — перепрочитать сообщения
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
            Task {
                await viewModel?.markAllAsReadIfNeeded()
            }
        }
        // Drag & Drop
        .onDrop(of: [.fileURL, .image], isTargeted: $isDragOver) { providers in
            handleDrop(providers: providers)
            return true
        }
        .animation(.easeInOut(duration: 0.2), value: isDragOver)
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
        // Обработка Escape - очистить вложения если есть
        .onKeyPress(.escape) {
            if !selectedAttachments.isEmpty {
                selectedAttachments = []
                return .handled
            }
            return .ignored
        }
        // Глобальная обработка Cmd+V для вставки файлов из буфера обмена
        .onPasteCommand(of: [.image, .fileURL, .png, .jpeg, .tiff]) { providers in
            handlePaste(providers: providers)
        }
        // Альтернативный перехват Cmd+V через NSEvent monitor
        .onAppear {
            setupPasteMonitor()
        }
        .onDisappear {
            removePasteMonitor()
        }
    }

    // MARK: - Paste Monitor

    private func setupPasteMonitor() {
        pasteMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            // Cmd+V
            if event.modifierFlags.contains(.command) && event.keyCode == 9 { // keyCode 9 = V
                handleClipboardPaste()
                return nil // Поглощаем событие
            }
            return event
        }
    }

    private func removePasteMonitor() {
        if let monitor = pasteMonitor {
            NSEvent.removeMonitor(monitor)
            pasteMonitor = nil
        }
    }

    /// Прямое чтение из NSPasteboard
    private func handleClipboardPaste() {
        let pasteboard = NSPasteboard.general

        // Проверяем изображение
        if pasteboard.canReadItem(withDataConformingToTypes: [UTType.image.identifier]) {
            if let imageData = pasteboard.data(forType: .png) ?? pasteboard.data(forType: .tiff) {
                let ext = imageData.count >= 4 && imageData[1] == 0x50 && imageData[2] == 0x4E && imageData[3] == 0x47 ? "png" : "jpg"
                let name = "pasted_image_\(Int(Date().timeIntervalSince1970)).\(ext)"
                do {
                    try addAttachment(.imageData(data: imageData, fileName: name))
                } catch {
                    viewModel?.uploadError = error.localizedDescription
                }
                return
            }
        }

        // Проверяем файл URL
        if let urls = pasteboard.readObjects(forClasses: [NSURL.self], options: nil) as? [URL], !urls.isEmpty {
            for url in urls {
                do {
                    try addAttachment(.fileURL(url: url, forceAsDocument: false))
                } catch {
                    viewModel?.uploadError = error.localizedDescription
                }
            }
        }
    }

    // MARK: - Paste Handling

    /// Обработка вставки из буфера обмена (Cmd+V) - через SwiftUI onPasteCommand
    private func handlePaste(providers: [NSItemProvider]) {
        for provider in providers {
            // Изображение из буфера (скриншот, копия картинки и т.д.)
            if provider.hasItemConformingToTypeIdentifier(UTType.image.identifier) {
                provider.loadDataRepresentation(forTypeIdentifier: UTType.image.identifier) { data, error in
                    guard let data, error == nil else { return }
                    DispatchQueue.main.async {
                        let ext: String
                        if data.count >= 4 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 {
                            ext = "png"
                        } else {
                            ext = "jpg"
                        }
                        let name = "pasted_image_\(Int(Date().timeIntervalSince1970)).\(ext)"
                        do {
                            try self.addAttachment(.imageData(data: data, fileName: name))
                        } catch {
                            self.viewModel?.uploadError = error.localizedDescription
                        }
                    }
                }
                continue
            }

            // Файл из Finder (скопированный путь)
            if provider.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
                provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier) { item, error in
                    guard let data = item as? Data,
                          let url = URL(dataRepresentation: data, relativeTo: nil) else { return }
                    DispatchQueue.main.async {
                        do {
                            try self.addAttachment(.fileURL(url: url, forceAsDocument: false))
                        } catch {
                            self.viewModel?.uploadError = error.localizedDescription
                        }
                    }
                }
            }
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
                headerHeight: headerHeight,
                inputHeight: inputHeight,
                firstUnreadMessageID: viewModel.firstUnreadMessageID,
                onLoadMore: {
                    Task { await viewModel.loadMoreMessages() }
                },
                onScrollToBottom: {
                    scrollPosition.scrollToBottom()
                },
                scrollPosition: scrollPosition,
                onRetry: { localID in
                    viewModel.retryFailedMessage(localID: localID)
                },
                onDeleteFailed: { localID in
                    viewModel.deleteFailedMessage(localID: localID)
                }
            )
        }
    }

    // MARK: - Actions

    private func sendMessage() {
        let trimmedText = messageText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedText.isEmpty || !selectedAttachments.isEmpty else { return }
        guard let viewModel else { return }

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

    private func handleDrop(providers: [NSItemProvider]) {
        for provider in providers {
            if provider.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
                provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier) { item, _ in
                    guard let data = item as? Data,
                          let url = URL(dataRepresentation: data, relativeTo: nil) else { return }
                    DispatchQueue.main.async {
                        do {
                            // Drag & Drop - определяем тип автоматически по расширению
                            try self.addAttachment(.fileURL(url: url, forceAsDocument: false))
                        } catch {
                            self.viewModel?.uploadError = error.localizedDescription
                        }
                    }
                }
            } else if provider.hasItemConformingToTypeIdentifier(UTType.image.identifier) {
                provider.loadDataRepresentation(forTypeIdentifier: UTType.image.identifier) { data, error in
                    guard let data, error == nil else { return }
                    DispatchQueue.main.async {
                        let name = "pasted_image_\(Int(Date().timeIntervalSince1970)).png"
                        self.selectedAttachments.append(.imageData(data: data, fileName: name))
                    }
                }
            }
        }
    }
}

#Preview {
    ConversationView(
        chat: Chat(
            id: "test-chat",
            title: "Славик",
            isGroupChat: false,
            members: [
                ChatMember(
                    userID: 2,
                    username: "slavik",
                    firstName: "Славик",
                    lastName: "",
                    role: .member
                )
            ]
        )
    )
    .environment(DependencyContainer())
}
