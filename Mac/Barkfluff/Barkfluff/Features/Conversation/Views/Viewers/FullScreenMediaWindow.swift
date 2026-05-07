//
//  FullScreenMediaWindow.swift
//  Barkfluff
//
//  Полноэкранное окно для просмотра медиа
//

import SwiftUI
import BFCore
import Combine
import AVKit
import Nuke
import NukeUI

// MARK: - Borderless Key Window

/// Borderless окно, которое принимает key events (ESC, стрелки)
/// Стандартный NSWindow с borderless стилем НЕ может стать key window,
/// поэтому необходим подкласс
private class MediaViewerWindow: NSWindow {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }

    /// Обработка ESC на уровне окна — надёжнее чем SwiftUI .onKeyPress
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { // ESC
            FullScreenMediaWindowManager.shared.closeWindow()
        } else {
            super.keyDown(with: event)
        }
    }
}

// MARK: - Window Manager

/// Менеджер полноэкранного окна медиа
@MainActor
class FullScreenMediaWindowManager: NSObject, ObservableObject {
    static let shared = FullScreenMediaWindowManager()

    private(set) var currentWindow: NSWindow?
    private var isClosing = false

    private var window: NSWindow? {
        get { currentWindow }
        set { currentWindow = newValue }
    }

    private override init() {
        super.init()
    }

    /// Открыть полноэкранное окно с медиа
    func openMediaViewer(
        attachments: [MessageAttachment],
        initialIndex: Int,
        messageText: String?,
        container: DependencyContainer
    ) {
        // Закрываем предыдущее окно если есть
        if window != nil {
            forceCloseWindow()
        }

        isClosing = false

        // Фильтруем только медиа-вложения
        let mediaAttachments = attachments.filter {
            $0.type == .image || $0.type == .video || $0.type == .gif
        }
        guard !mediaAttachments.isEmpty else { return }

        // Корректируем индекс
        let adjustedIndex = min(initialIndex, mediaAttachments.count - 1)

        let contentView = FullScreenMediaViewerView(
            attachments: mediaAttachments,
            initialIndex: adjustedIndex,
            messageText: messageText,
            onClose: {
                FullScreenMediaWindowManager.shared.closeWindow()
            }
        )
        .environment(container)

        // Создаем кастомное окно
        let window = MediaViewerWindow(
            contentRect: NSScreen.main?.frame ?? NSRect(x: 0, y: 0, width: 800, height: 600),
            styleMask: [.borderless, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )

        window.contentView = NSHostingView(rootView: contentView)
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        // Прозрачное окно — фон рисует SwiftUI
        window.backgroundColor = .clear
        window.isOpaque = false
        // Окно поверх дока и меню-бара
        window.level = NSWindow.Level(rawValue: Int(CGShieldingWindowLevel()))
        window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        window.hidesOnDeactivate = false
        window.ignoresMouseEvents = false
        window.acceptsMouseMovedEvents = true
        window.isMovable = false
        window.hasShadow = false

        // На весь экран
        window.setFrame(NSScreen.main?.frame ?? window.frame, display: true)
        window.alphaValue = 0

        // Показываем окно и делаем key (key = принимает клавиатурные события)
        window.makeKeyAndOrderFront(nil)

        // Анимация появления
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.2
            window.animator().alphaValue = 1
        }

        self.window = window
    }

    /// Закрыть окно — безопасно для вызова из SwiftUI и из keyDown
    func closeWindow() {
        guard let window = window, !isClosing else { return }
        isClosing = true

        // 1. Моментально прячем окно с экрана (не уничтожает content)
        window.orderOut(nil)

        // 2. На следующем run loop чистим — SwiftUI уже точно не на стеке
        DispatchQueue.main.async { [weak self] in
            window.contentView = nil
            self?.window = nil
            self?.isClosing = false
        }
    }

    /// Немедленное закрытие (для повторного открытия)
    private func forceCloseWindow() {
        guard let window = window else { return }
        window.orderOut(nil)
        window.contentView = nil
        self.window = nil
        isClosing = false
    }
}

// MARK: - Full Screen Media Viewer View

struct FullScreenMediaViewerView: View {
    let attachments: [MessageAttachment]
    let initialIndex: Int
    let messageText: String?
    let onClose: () -> Void

    @Environment(DependencyContainer.self) private var container

    @State private var currentIndex: Int
    @State private var showControls = true
    @State private var controlsTimer: Timer?
    @State private var isDownloading = false
    @State private var shareFileURL: URL?

    init(
        attachments: [MessageAttachment],
        initialIndex: Int = 0,
        messageText: String? = nil,
        onClose: @escaping () -> Void
    ) {
        self.attachments = attachments
        self.initialIndex = initialIndex
        self.messageText = messageText
        self.onClose = onClose
        self._currentIndex = State(initialValue: initialIndex)
    }

    private var currentAttachment: MessageAttachment? {
        attachments[safe: currentIndex]
    }

    var body: some View {
        ZStack {
            // Полупрозрачный чёрный фон
            Color.black.opacity(0.85)
                .ignoresSafeArea()

            // Основной контент
            TabView(selection: $currentIndex) {
                ForEach(Array(attachments.enumerated()), id: \.element.id) { index, attachment in
                    viewerContent(for: attachment)
                        .tag(index)
                }
            }
            #if os(iOS)
            .tabViewStyle(.page(indexDisplayMode: .never))
            #else
            .tabViewStyle(.automatic)
            #endif

            // Верхний тулбар
            VStack {
                topToolbar
                Spacer()
            }

            // Нижняя область: текст + миниатюры + стрелки
            VStack {
                Spacer()
                bottomArea
            }
        }
        .onAppear {
            scheduleControlsHide()
        }
        .onMoveCommand { direction in
            handleArrowKey(direction)
        }
    }

    // MARK: - Top Toolbar

    private var topToolbar: some View {
        HStack(spacing: 12) {
            // Кнопка "Закрыть"
            Button {
                onClose()
            } label: {
                HStack(spacing: 6) {
                    Image(systemName: "xmark")
                        .font(.body)
                    Text("Закрыть")
                        .font(.subheadline)
                }
                .foregroundStyle(.white)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .contentShape(Capsule())
            }
            .buttonStyle(.plain)
            .glassEffect(.regular, in: .capsule)

            Spacer()

            // Счётчик
            if attachments.count > 1 {
                Text("\(currentIndex + 1) из \(attachments.count)")
                    .font(.subheadline)
                    .fontWeight(.medium)
                    .foregroundStyle(.white)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 6)
                    .glassEffect(.regular, in: .capsule)
            }

            Spacer()

            // Кнопки действий
            HStack(spacing: 8) {
                // Скачать оригинал
                Button {
                    downloadOriginal()
                } label: {
                    Group {
                        if isDownloading {
                            ProgressView()
                                .controlSize(.small)
                                .tint(.white)
                        } else {
                            Image(systemName: "arrow.down.circle")
                                .font(.body)
                        }
                    }
                    .foregroundStyle(.white)
                    .frame(width: 32, height: 32)
                    .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .glassEffect(.regular, in: .circle)
                .disabled(isDownloading)

                // Поделиться
                Button {
                    shareFile()
                } label: {
                    Image(systemName: "square.and.arrow.up")
                        .font(.body)
                        .foregroundStyle(.white)
                        .frame(width: 32, height: 32)
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .glassEffect(.regular, in: .circle)
            }
        }
        .padding(.horizontal, 20)
        .padding(.top, 16)
    }

    // MARK: - Bottom Area

    private var bottomArea: some View {
        VStack(spacing: 12) {
            // Текст сообщения
            if let text = messageText, !text.isEmpty {
                Text(text)
                    .font(.body)
                    .foregroundStyle(.white)
                    .multilineTextAlignment(.center)
                    .lineLimit(3)
                    .padding(.horizontal, 60)
            }

            // Миниатюры + стрелки навигации
            if attachments.count > 1 {
                HStack(spacing: 16) {
                    // Стрелка назад
                    Button {
                        if currentIndex > 0 {
                            withAnimation(.easeInOut(duration: 0.2)) { currentIndex -= 1 }
                        }
                    } label: {
                        Image(systemName: "chevron.left")
                            .font(.title2)
                            .foregroundStyle(currentIndex > 0 ? .white : .white.opacity(0.3))
                            .frame(width: 36, height: 36)
                    }
                    .buttonStyle(.plain)
                    .glassEffect(.clear, in: .circle)
                    .disabled(currentIndex == 0)

                    // Полоска миниатюр
                    thumbnailStrip

                    // Стрелка вперёд
                    Button {
                        if currentIndex < attachments.count - 1 {
                            withAnimation(.easeInOut(duration: 0.2)) { currentIndex += 1 }
                        }
                    } label: {
                        Image(systemName: "chevron.right")
                            .font(.title2)
                            .foregroundStyle(currentIndex < attachments.count - 1 ? .white : .white.opacity(0.3))
                            .frame(width: 36, height: 36)
                    }
                    .buttonStyle(.plain)
                    .glassEffect(.clear, in: .circle)
                    .disabled(currentIndex >= attachments.count - 1)
                }
                .padding(.horizontal, 20)
            }
        }
        .padding(.bottom, 24)
    }

    // MARK: - Thumbnail Strip

    private var thumbnailStrip: some View {
        ScrollViewReader { proxy in
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 6) {
                    ForEach(Array(attachments.enumerated()), id: \.element.id) { index, attachment in
                        ThumbnailView(attachment: attachment, isSelected: index == currentIndex)
                            .id(index)
                            .onTapGesture {
                                withAnimation(.easeInOut(duration: 0.2)) {
                                    currentIndex = index
                                }
                            }
                    }
                }
                .padding(.horizontal, 4)
            }
            .frame(maxWidth: CGFloat(min(attachments.count, 8)) * 58)
            .onChange(of: currentIndex) { _, newIndex in
                withAnimation(.easeInOut(duration: 0.2)) {
                    proxy.scrollTo(newIndex, anchor: .center)
                }
            }
        }
    }

    // MARK: - Content

    @ViewBuilder
    private func viewerContent(for attachment: MessageAttachment) -> some View {
        switch attachment.type {
        case .image, .gif, .sticker:
            FullScreenImageView(attachment: attachment, onClose: onClose)
        case .video:
            FullScreenVideoPlayerView(attachment: attachment, onClose: onClose)
        case .document, .audio, .voice, .forwardedMessage:
            EmptyView()
        }
    }

    // MARK: - Actions

    private func downloadOriginal() {
        guard let attachment = currentAttachment else { return }
        isDownloading = true

        Task {
            defer { isDownloading = false }
            do {
                try await FileDownloadHelper.downloadToDownloads(
                    fileID: attachment.fileID,
                    fileName: attachment.fileName,
                    fileService: container.fileService,
                    mediaCacheManager: container.mediaCacheManager,
                    cacheType: attachment.type.cacheType
                )
            } catch {
                // Ошибка скачивания — можно добавить алерт
            }
        }
    }

    private func shareFile() {
        guard let attachment = currentAttachment else { return }

        Task {
            do {
                let fileURL = try await FileDownloadHelper.downloadToTemp(
                    fileID: attachment.fileID,
                    fileName: attachment.fileName,
                    fileService: container.fileService,
                    mediaCacheManager: container.mediaCacheManager,
                    cacheType: attachment.type.cacheType
                )

                guard let window = FullScreenMediaWindowManager.shared.currentWindow,
                      let contentView = window.contentView else { return }

                // Временно понижаем level окна, иначе picker откроется под ним
                let savedLevel = window.level
                window.level = .floating

                let picker = NSSharingServicePicker(items: [fileURL])
                let rect = NSRect(
                    x: contentView.bounds.maxX - 80,
                    y: contentView.bounds.maxY - 50,
                    width: 1,
                    height: 1
                )
                picker.show(relativeTo: rect, of: contentView, preferredEdge: .minY)

                // Вернём level после небольшой задержки (picker уже показан)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                    window.level = savedLevel
                }
            } catch {
                // Ошибка
            }
        }
    }

    // MARK: - Keyboard Navigation

    private func handleArrowKey(_ direction: MoveCommandDirection) {
        withAnimation(.easeInOut(duration: 0.2)) {
            switch direction {
            case .left:
                if currentIndex > 0 { currentIndex -= 1 }
            case .right:
                if currentIndex < attachments.count - 1 { currentIndex += 1 }
            default:
                break
            }
        }
        showControlsTemporarily()
    }

    // MARK: - Controls Visibility

    private func showControlsTemporarily() {
        withAnimation { showControls = true }
        scheduleControlsHide()
    }

    private func scheduleControlsHide() {
        controlsTimer?.invalidate()
        controlsTimer = Timer.scheduledTimer(withTimeInterval: 4.0, repeats: false) { _ in
            Task { @MainActor in
                withAnimation { showControls = false }
            }
        }
    }
}

// MARK: - Thumbnail View

struct ThumbnailView: View {
    let attachment: MessageAttachment
    let isSelected: Bool

    private let thumbnailSize: CGFloat = 52

    var body: some View {
        ZStack {
            CachedImageView(
                fileID: previewCacheFileID,
                type: .preview,
                presignedURLHint: attachment.previewURL,
                processors: [.resize(size: CGSize(width: thumbnailSize * 2, height: thumbnailSize * 2))],
                content: { image in
                    image
                        .resizable()
                        .aspectRatio(contentMode: .fill)
                },
                placeholder: { placeholderView }
            )

            // Бейдж для видео
            if attachment.type == .video {
                Image(systemName: "play.fill")
                    .font(.caption2)
                    .foregroundStyle(.white)
            }
        }
        .frame(width: thumbnailSize, height: thumbnailSize)
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .overlay(
            RoundedRectangle(cornerRadius: 6)
                .stroke(isSelected ? .white : .clear, lineWidth: 2)
        )
        .opacity(isSelected ? 1 : 0.6)
    }

    private var placeholderView: some View {
        Rectangle()
            .fill(.white.opacity(0.15))
            .overlay {
                Image(systemName: attachment.type == .video ? "video.fill" : "photo")
                    .font(.caption)
                    .foregroundStyle(.white.opacity(0.5))
            }
    }

    private var previewCacheFileID: String {
        if let previewFileID = attachment.previewFileID, !previewFileID.isEmpty {
            return previewFileID
        }
        if let url = attachment.previewURL, let fid = S3URLParser.fileID(from: url) {
            return fid
        }
        return attachment.fileID
    }
}

// MARK: - Full Screen Image View

struct FullScreenImageView: View {
    let attachment: MessageAttachment
    let onClose: () -> Void

    @Environment(DependencyContainer.self) private var container

    @State private var scale: CGFloat = 1.0
    @State private var lastScale: CGFloat = 1.0
    @State private var offset: CGSize = .zero
    @State private var lastOffset: CGSize = .zero
    @State private var imageURL: URL?
    @State private var isLoading = true

    var body: some View {
        GeometryReader { geo in
            ZStack {
                // Фоновая область — клик по ней закрывает viewer
                Color.clear
                    .contentShape(Rectangle())
                    .onTapGesture {
                        onClose()
                    }

                if let url = imageURL {
                    AsyncImage(url: url) { phase in
                        switch phase {
                        case .success(let image):
                            image
                                .resizable()
                                .aspectRatio(contentMode: .fit)
                                .scaleEffect(scale)
                                .offset(offset)
                                .gesture(
                                    SimultaneousGesture(
                                        MagnificationGesture()
                                            .onChanged { value in
                                                scale = min(max(lastScale * value, 1), 5)
                                            }
                                            .onEnded { _ in
                                                lastScale = scale
                                                if scale < 1 {
                                                    withAnimation { scale = 1; lastScale = 1 }
                                                }
                                            },
                                        DragGesture()
                                            .onChanged { value in
                                                if scale > 1 {
                                                    offset = CGSize(
                                                        width: lastOffset.width + value.translation.width,
                                                        height: lastOffset.height + value.translation.height
                                                    )
                                                }
                                            }
                                            .onEnded { _ in lastOffset = offset }
                                    )
                                )
                                // Одиночный тап на изображение — не закрывать
                                .onTapGesture { }
                                .onTapGesture(count: 2) {
                                    withAnimation(.spring) {
                                        if scale > 1 {
                                            scale = 1; offset = .zero
                                            lastScale = 1; lastOffset = .zero
                                        } else {
                                            scale = 2
                                        }
                                    }
                                }
                        case .failure:
                            errorView
                        default:
                            loadingView
                        }
                    }
                } else if isLoading {
                    loadingView
                } else {
                    errorView
                }
            }
            .frame(width: geo.size.width, height: geo.size.height)
        }
        .task { await loadImage() }
    }

    private var loadingView: some View {
        VStack(spacing: 16) {
            ProgressView()
                .controlSize(.large)
                .tint(.white)
            Text("Загрузка...")
                .font(.subheadline)
                .foregroundStyle(.white.opacity(0.7))
        }
    }

    private var errorView: some View {
        VStack(spacing: 16) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 48))
                .foregroundStyle(.white.opacity(0.7))
            Text("Не удалось загрузить")
                .foregroundStyle(.white.opacity(0.7))
        }
    }

    private func loadImage() async {
        do {
            // Оригинал — всегда через кеш-менеджер по fileID.
            // Для GIF/стикеров тип кеша другой, но логика та же.
            let cacheType: CachedFileType
            switch attachment.type {
            case .gif: cacheType = .gif
            case .sticker: cacheType = .sticker
            default: cacheType = .image
            }
            let url = try await container.mediaCacheManager.resolveURL(
                for: attachment.fileID,
                type: cacheType
            )
            imageURL = url
        } catch {}
        isLoading = false
    }
}

// MARK: - Full Screen Video Player (wrapper)

struct FullScreenVideoPlayerView: View {
    let attachment: MessageAttachment
    let onClose: () -> Void

    var body: some View {
        FullScreenVideoPlayer(attachment: attachment, onClose: onClose)
    }
}

// MARK: - Array Extension

private extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
