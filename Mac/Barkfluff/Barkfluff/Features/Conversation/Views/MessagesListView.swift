//
//  MessagesListView.swift
//  Barkfluff
//
//  ScrollView с сообщениями, пагинацией и автоскроллом
//

import SwiftUI
import BFCore

/// ScrollView с сообщениями
struct MessagesListView: View {
    let items: [MessageListItem]
    let currentUserID: Int64
    let isGroupChat: Bool
    let isLoadingMore: Bool
    let headerHeight: CGFloat
    let inputHeight: CGFloat
    let firstUnreadMessageID: Int64?
    let onLoadMore: () -> Void
    let onScrollToBottom: () -> Void
    let scrollPosition: ScrollPositionManager
    var onRetry: ((String) -> Void)?
    var onDeleteFailed: ((String) -> Void)?
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
                    // Отступ сверху под плавающий заголовок
                    Color.clear
                        .frame(height: max(headerHeight, 120))

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
                                onReply: onReply,
                                onForward: onForward,
                                onEdit: onEdit,
                                onDelete: onDelete,
                                onCopyText: onCopyText,
                                onSaveImages: onSaveImages,
                                onCopyImage: onCopyImage,
                                onSaveDocuments: onSaveDocuments
                            )
                            .transition(.asymmetric(
                                insertion: .move(edge: .bottom).combined(with: .opacity),
                                removal: .opacity
                            ))
                            .onAppear {
                                // Пагинация при достижении первого сообщения (не во время начальной загрузки)
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

                    // Отступ внизу под плавающее поле ввода
                    Color.clear
                        .frame(height: max(inputHeight, 90) + Theme.Spacing.md)
                        .id("bottom")
                }
                .padding(.horizontal, Theme.Spacing.md)
                .background(
                    ScrollOffsetReader(scrollPosition: scrollPosition)
                )
            }
            .onChange(of: scrollPosition.scrollToID) { _, newID in
                if let id = newID {
                    withAnimation(.smooth) {
                        proxy.scrollTo(id, anchor: .bottom)
                    }
                }
            }
            .onChange(of: items.count) { oldCount, newCount in
                // Автоскролл при новых сообщениях, если были внизу
                if scrollPosition.isAtBottom, newCount > oldCount {
                    scrollToBottom(proxy: proxy, animate: true)
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
                // Даем время ScrollView стабилизироваться перед включением пагинации
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
}

/// View для отслеживания позиции скролла
struct ScrollOffsetReader: NSViewRepresentable {
    let scrollPosition: ScrollPositionManager

    func makeNSView(context: Context) -> NSView {
        let view = ScrollOffsetNSView()
        view.onScroll = { [weak scrollPosition] newOffset, isAtBottom in
            scrollPosition?.updateScrollOffset(newOffset, isAtBottom: isAtBottom)
        }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

/// NSView для отслеживания скролла
class ScrollOffsetNSView: NSView {
    var onScroll: ((CGFloat, Bool) -> Void)?

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(scrollDidChange),
            name: NSView.boundsDidChangeNotification,
            object: nil
        )
    }

    @objc private func scrollDidChange(_ notification: Notification) {
        guard let clipView = notification.object as? NSClipView,
              clipView.isDescendant(of: self.enclosingScrollView ?? self) else { return }
        let offset = clipView.bounds.origin.y
        let contentHeight = clipView.documentRect.height
        let visibleHeight = clipView.bounds.height
        let isAtBottom = offset + visibleHeight >= contentHeight - 50
        onScroll?(offset, isAtBottom)
    }

    deinit {
        NotificationCenter.default.removeObserver(self)
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
        headerHeight: 120,
        inputHeight: 60,
        firstUnreadMessageID: nil,
        onLoadMore: {},
        onScrollToBottom: {},
        scrollPosition: ScrollPositionManager()
    )
}
