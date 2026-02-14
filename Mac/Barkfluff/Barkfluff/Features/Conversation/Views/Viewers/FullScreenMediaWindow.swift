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

/// Менеджер полноэкранного окна медиа
@MainActor
class FullScreenMediaWindowManager: ObservableObject {
    static let shared = FullScreenMediaWindowManager()

    private var window: NSWindow?

    private init() {}

    /// Открыть полноэкранное окно с медиа
    func openMediaViewer(attachments: [MessageAttachment], initialIndex: Int, container: DependencyContainer) {
        // Закрываем предыдущее окно если есть
        closeWindow()

        // Создаем WindowController с контентом
        let contentView = FullScreenMediaViewerView(
            attachments: attachments,
            initialIndex: initialIndex,
            onClose: { [weak self] in
                self?.closeWindow()
            }
        )
        .environment(container)

        // Создаем окно
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 800, height: 600),
            styleMask: [.titled, .closable, .fullSizeContentView, .resizable],
            backing: .buffered,
            defer: false
        )

        window.center()
        window.contentView = NSHostingView(rootView: contentView)
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        window.backgroundColor = .windowBackgroundColor
        window.isOpaque = true
        window.level = .normal
        window.collectionBehavior = [.fullScreenPrimary]
        window.hidesOnDeactivate = false
        window.ignoresMouseEvents = false
        window.acceptsMouseMovedEvents = true

        // Показываем окно
        window.makeKeyAndOrderFront(nil)

        // Входим в fullscreen
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) {
            window.toggleFullScreen(nil)
        }

        self.window = window
    }

    /// Закрыть окно
    func closeWindow() {
        guard let window = window else { return }
        self.window = nil

        // Выходим из fullscreen перед закрытием
        if window.styleMask.contains(.fullScreen) {
            window.toggleFullScreen(nil)
        }

        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2) {
            window.close()
        }
    }
}

// MARK: - Full Screen Media Viewer View

struct FullScreenMediaViewerView: View {
    let attachments: [MessageAttachment]
    let initialIndex: Int
    let onClose: () -> Void

    @State private var currentIndex: Int
    @State private var showControls = true
    @State private var controlsTimer: Timer?

    init(attachments: [MessageAttachment], initialIndex: Int = 0, onClose: @escaping () -> Void) {
        self.attachments = attachments
        self.initialIndex = initialIndex
        self.onClose = onClose
        self._currentIndex = State(initialValue: initialIndex)
    }

    var body: some View {
        ZStack {
            // Полупрозрачный фон
            Color.black.opacity(0.9)
                .ignoresSafeArea()

            // Основной контент
            TabView(selection: $currentIndex) {
                ForEach(Array(attachments.enumerated()), id: \.element.id) { index, attachment in
                    viewerContent(for: attachment)
                        .tag(index)
                }
            }
            .padding(.top, 40) // Отступ от верха для кнопок

            // Кнопка закрытия (всегда видна)
            closeButton

            // Оверлей с контролами
            if showControls {
                controlsOverlay
            }
        }
        .onAppear {
            scheduleControlsHide()
        }
        .onMoveCommand { direction in
            handleArrowKey(direction)
        }
        .onExitCommand {
            onClose()
        }
        .onKeyPress(.escape) {
            onClose()
            return .handled
        }
        .keyboardShortcut(.escape, modifiers: [])
    }

    // MARK: - Close Button

    private var closeButton: some View {
        VStack {
            HStack {
                Button {
                    onClose()
                } label: {
                    HStack(spacing: 6) {
                        Image(systemName: "xmark")
                            .font(.body)
                        Text("Закрыть")
                            .font(.subheadline)
                    }
                    .foregroundStyle(.primary)
                    .padding(.horizontal, 12)
                    .padding(.vertical, 6)
                }
                .buttonStyle(.plain)
                .glassEffect(.regular, in: .capsule)

                Spacer()

                // Счётчик
                if attachments.count > 1 {
                    Text("\(currentIndex + 1) из \(attachments.count)")
                        .font(.subheadline)
                        .fontWeight(.medium)
                        .foregroundStyle(.primary)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .glassEffect(.regular, in: .capsule)
                }

                Spacer()

                // Кнопка "Поделиться"
                if let attachment = attachments[safe: currentIndex] {
                    ShareLink(item: attachment.fileID) {
                        Image(systemName: "square.and.arrow.up")
                            .font(.body)
                            .foregroundStyle(.primary)
                            .padding(.horizontal, 12)
                            .padding(.vertical, 6)
                    }
                    .buttonStyle(.plain)
                    .glassEffect(.regular, in: .capsule)
                }
            }
            .padding()
            Spacer()
        }
    }

    // MARK: - Content

    @ViewBuilder
    private func viewerContent(for attachment: MessageAttachment) -> some View {
        switch attachment.type {
        case .image, .gif:
            FullScreenImageView(attachment: attachment)

        case .video:
            FullScreenVideoPlayerView(attachment: attachment, onClose: onClose)

        case .document, .audio:
            FullScreenDocumentPlaceholder(attachment: attachment)
        }
    }

    // MARK: - Controls Overlay

    private var controlsOverlay: some View {
        VStack {
            Spacer()

            // Нижний тулбар с навигацией
            if attachments.count > 1 {
                bottomToolbar
            }
        }
        .transition(.opacity)
    }

    private var bottomToolbar: some View {
        HStack(spacing: 60) {
            // Кнопка "Назад"
            Button {
                if currentIndex > 0 {
                    withAnimation { currentIndex -= 1 }
                }
            } label: {
                Image(systemName: "chevron.left")
                    .font(.title2)
                    .foregroundStyle(currentIndex > 0 ? .white : .white.opacity(0.3))
                    .frame(width: 44, height: 44)
            }
            .buttonStyle(.plain)
            .glassEffect(.clear, in: .circle)
            .disabled(currentIndex == 0)

            Spacer()

            // Кнопка "Вперёд"
            Button {
                if currentIndex < attachments.count - 1 {
                    withAnimation { currentIndex += 1 }
                }
            } label: {
                Image(systemName: "chevron.right")
                    .font(.title2)
                    .foregroundStyle(currentIndex < attachments.count - 1 ? .white : .white.opacity(0.3))
                    .frame(width: 44, height: 44)
            }
            .buttonStyle(.plain)
            .glassEffect(.clear, in: .circle)
            .disabled(currentIndex >= attachments.count - 1)
        }
        .padding(.horizontal, 60)
        .padding(.bottom, 50)
    }

    // MARK: - Keyboard Navigation

    private func handleArrowKey(_ direction: MoveCommandDirection) {
        withAnimation {
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

// MARK: - Full Screen Image View

struct FullScreenImageView: View {
    let attachment: MessageAttachment

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
            if let previewURL = attachment.previewURL, !previewURL.isEmpty {
                imageURL = URL(string: previewURL)
            } else {
                let urlString = try await container.fileService.getDownloadURL(fileID: attachment.fileID)
                imageURL = URL(string: urlString)
            }
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

// MARK: - Document Placeholder

struct FullScreenDocumentPlaceholder: View {
    let attachment: MessageAttachment

    var body: some View {
        VStack(spacing: 24) {
            FileIconView(fileName: attachment.fileName, size: 80)

            VStack(spacing: 8) {
                Text(attachment.fileName)
                    .font(.headline)
                    .foregroundStyle(.white)

                Text(attachment.formattedSize)
                    .font(.subheadline)
                    .foregroundStyle(.white.opacity(0.7))
            }
        }
    }
}

// MARK: - Array Extension

private extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
