//
//  MessagesListView.swift
//  Barkfluff
//
//  ScrollView с сообщениями, пагинацией и автоскроллом (iOS версия)
//

import SwiftUI
import BFCore

/// ScrollView с сообщениями
struct MessagesListView: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    let items: [MessageListItem]
    let currentUserID: Int64
    let isGroupChat: Bool
    let isLoadingMore: Bool
    let firstUnreadMessageID: Int64?
    let onLoadMore: () -> Void
    let scrollPosition: ScrollPositionManager
    var onRetry: ((String) -> Void)?
    var onDeleteFailed: ((String) -> Void)?
    var onAttachmentTap: ((MessageAttachment, [MessageAttachment]) -> Void)?
    var onReply: ((Message) -> Void)?
    var onForward: ((Int64) -> Void)?
    var onEdit: ((Message) -> Void)?
    var onDelete: ((Int64) -> Void)?
    var onCopyText: ((String) -> Void)?
    var onSaveImages: (([MessageAttachment]) -> Void)?
    var onCopyImage: ((MessageAttachment) -> Void)?
    var onSaveDocuments: (([MessageAttachment]) -> Void)?

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(spacing: Theme.Spacing.xxs) {
                    // Отступ сверху
                    Color.clear
                        .frame(height: 20)

                    // Индикатор загрузки старых сообщений
                    if isLoadingMore {
                        ProgressView()
                            .padding(.top, Theme.Spacing.md)
                    }

                    // Сообщения
                    ForEach(items) { item in
                        switch item {
                        case .dateSeparator(let date):
                            MessageDateSeparatorView(date: date)

                        case .message(let message, let groupInfo):
                            MessageBubbleView(
                                message: message,
                                currentUserID: currentUserID,
                                groupInfo: groupInfo,
                                showSenderName: isGroupChat,
                                onRetry: onRetry,
                                onDeleteFailed: onDeleteFailed,
                                onAttachmentTap: onAttachmentTap,
                                onReply: onReply,
                                onForward: onForward,
                                onEdit: onEdit,
                                onDelete: onDelete,
                                onCopyText: onCopyText,
                                onSaveImages: onSaveImages,
                                onCopyImage: onCopyImage,
                                onSaveDocuments: onSaveDocuments
                            )
                            .transition(messageTransition)
                            .onAppear {
                                // Пагинация при достижении первого сообщения
                                guard scrollPosition.isInitialLoadComplete else { return }
                                if let firstMessage = items.first(where: {
                                    if case .message = $0 { return true }
                                    return false
                                }), case .message(let firstMsg, _) = firstMessage,
                                   firstMsg.id == message.id {
                                    onLoadMore()
                                }
                            }
                        }
                    }

                    // Якорь для scrollToBottom (без визуального отступа —
                    // высоту инпута уже резервирует safeAreaInset)
                    Color.clear
                        .frame(height: 0)
                        .id("bottom")
                }
                .padding(.horizontal, Theme.Spacing.sm)
            }
            .onChange(of: scrollPosition.scrollToID) { _, newID in
                if let id = newID {
                    if reduceMotion {
                        proxy.scrollTo(id, anchor: .bottom)
                    } else {
                        withAnimation(.smooth) {
                            proxy.scrollTo(id, anchor: .bottom)
                        }
                    }
                }
            }
            .onChange(of: items.count) { oldCount, newCount in
                // Автоскролл при новых сообщениях, если были внизу
                if scrollPosition.isAtBottom, newCount > oldCount {
                    scrollToBottom(proxy: proxy, animate: !reduceMotion)
                }
            }
            .defaultScrollAnchor(.bottom)
            .task {
                // Скролл к первому непрочитанному или вниз
                if let unreadID = firstUnreadMessageID {
                    proxy.scrollTo("msg-\(unreadID)", anchor: .center)
                } else {
                    scrollToBottom(proxy: proxy, animate: false)
                }
                // Даем время ScrollView стабилизироваться
                try? await Task.sleep(for: .milliseconds(300))
                scrollPosition.isInitialLoadComplete = true
            }
        }
    }

    private func scrollToBottom(proxy: ScrollViewProxy, animate: Bool) {
        if animate {
            withAnimation(.smooth) {
                proxy.scrollTo("bottom", anchor: .bottom)
            }
        } else {
            proxy.scrollTo("bottom", anchor: .bottom)
        }
    }

    private var messageTransition: AnyTransition {
        if reduceMotion {
            return .opacity
        }
        return .asymmetric(
            insertion: .move(edge: .bottom).combined(with: .opacity),
            removal: .opacity
        )
    }
}

#Preview {
    let messages: [MessageListItem] = [
        .dateSeparator(date: Date()),
        .message(
            Message(
                id: 1,
                chatID: "test",
                senderID: 2,
                senderName: "Иван",
                content: MessageContent(text: "Привет!"),
                sentAt: Date()
            ),
            MessageGroupInfo(isFirstInGroup: true, isLastInGroup: true, showTime: true)
        ),
        .message(
            Message(
                id: 2,
                chatID: "test",
                senderID: 1,
                senderName: "Я",
                content: MessageContent(text: "Привет, как дела?"),
                sentAt: Date().addingTimeInterval(1)
            ),
            MessageGroupInfo(isFirstInGroup: true, isLastInGroup: true, showTime: true)
        )
    ]

    MessagesListView(
        items: messages,
        currentUserID: 1,
        isGroupChat: false,
        isLoadingMore: false,
        firstUnreadMessageID: nil,
        onLoadMore: {},
        scrollPosition: ScrollPositionManager()
    )
}
