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
    let onLoadMore: () -> Void
    let onScrollToBottom: () -> Void
    let scrollPosition: ScrollPositionManager

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
                                showSenderName: isGroupChat
                            )
                            .id(message.id)
                            .onAppear {
                                // Пагинация при достижении первого сообщения
                                if items.first(where: {
                                    if case .message(let msg, _) = $0 { return msg.id == message.id }
                                    return false
                                }) != nil {
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
            .task {
                // Начальный скролл вниз
                scrollToBottom(proxy: proxy, animate: false)
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
        view.onScroll = { [weak scrollPosition] newOffset in
            scrollPosition?.scrollOffset = newOffset
        }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

/// NSView для отслеживания скролла
class ScrollOffsetNSView: NSView {
    var onScroll: ((CGFloat) -> Void)?

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
        onScroll?(clipView.bounds.origin.y)
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
        onLoadMore: {},
        onScrollToBottom: {},
        scrollPosition: ScrollPositionManager()
    )
}
