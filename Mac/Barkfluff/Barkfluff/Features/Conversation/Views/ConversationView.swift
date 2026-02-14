//
//  ConversationView.swift
//  Barkfluff
//
//  Экран переписки в стиле iMessage
//  ZStack с плавающими заголовком и полем ввода
//

import SwiftUI
import BFCore

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

    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ConversationViewModel?
    @State private var messageText = ""
    @State private var scrollPosition = ScrollPositionManager()
    @State private var headerHeight: CGFloat = 0
    @State private var inputHeight: CGFloat = 0

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
                ConversationHeaderView(chat: chat)
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
                MessageInputView(text: $messageText) {
                    sendMessage()
                }
                .background(
                    GeometryReader { geo in
                        Color.clear.preference(
                            key: InputHeightKey.self,
                            value: geo.size.height
                        )
                    }
                )
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
        }
        .ignoresSafeArea(edges: .top)
        .toolbarBackground(.hidden, for: .windowToolbar)
        .onPreferenceChange(HeaderHeightKey.self) { headerHeight = $0 }
        .onPreferenceChange(InputHeightKey.self) { inputHeight = $0 }
        // Обработка нажатия на вложение
        .onReceive(NotificationCenter.default.publisher(for: .attachmentTapped)) { notification in
            if let attachment = notification.userInfo?["attachment"] as? MessageAttachment,
               let allAttachments = notification.userInfo?["allAttachments"] as? [MessageAttachment] {
                let index = allAttachments.firstIndex(of: attachment) ?? 0
                // Открываем полноэкранный просмотр
                FullScreenMediaWindowManager.shared.openMediaViewer(
                    attachments: allAttachments,
                    initialIndex: index,
                    container: container
                )
            }
        }
        .task {
            if viewModel == nil {
                let vm = ConversationViewModel(
                    chat: chat,
                    messageService: container.messageService,
                    updatesService: container.updatesService,
                    currentUserID: container.currentUserID
                )
                viewModel = vm
                await vm.loadMessages()
                await vm.startListeningForUpdates()
            }
        }
        .onDisappear {
            viewModel?.stopListeningForUpdates()
        }
        .onChange(of: chat.id) { _, _ in
            scrollPosition.reset()
            viewModel?.stopListeningForUpdates()
            viewModel = nil

            Task {
                let vm = ConversationViewModel(
                    chat: chat,
                    messageService: container.messageService,
                    updatesService: container.updatesService,
                    currentUserID: container.currentUserID
                )
                viewModel = vm
                await vm.loadMessages()
                await vm.startListeningForUpdates()
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
                "Нет сообщений",
                systemImage: "bubble.left.and.bubble.right",
                description: Text("Начните диалог!")
            )
        } else {
            MessagesListView(
                items: viewModel.listItems,
                currentUserID: viewModel.currentUserID,
                isGroupChat: chat.isGroupChat,
                isLoadingMore: viewModel.isLoadingMore,
                headerHeight: headerHeight,
                inputHeight: inputHeight,
                onLoadMore: {
                    Task { await viewModel.loadMoreMessages() }
                },
                onScrollToBottom: {
                    scrollPosition.scrollToBottom()
                },
                scrollPosition: scrollPosition
            )
        }
    }

    // MARK: - Actions

    private func sendMessage() {
        guard !messageText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return }
        guard let viewModel else { return }

        let text = messageText
        messageText = ""

        Task {
            await viewModel.sendMessage(text: text)
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
