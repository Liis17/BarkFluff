//
//  CustomVideoPlayerView.swift
//  Barkfluff
//
//  Кастомный видеоплеер с Liquid Glass контролами
//  Без использования стандартного VideoPlayer (конфликтует с Liquid Glass)
//

import SwiftUI
import AVKit
import BFCore

/// Кастомный видеоплеер с AVPlayerLayer
struct CustomVideoPlayerView: NSViewRepresentable {
    let player: AVPlayer

    func makeNSView(context: Context) -> NSView {
        let view = VideoPlayerNSView()
        view.player = player
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        if let view = nsView as? VideoPlayerNSView {
            view.player = player
        }
    }
}

/// NSView с AVPlayerLayer
class VideoPlayerNSView: NSView {
    var player: AVPlayer? {
        didSet {
            playerLayer.player = player
        }
    }

    private var playerLayer: AVPlayerLayer {
        layer as! AVPlayerLayer
    }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer = AVPlayerLayer()
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
        wantsLayer = true
        layer = AVPlayerLayer()
    }

    override func layout() {
        super.layout()
        playerLayer.frame = bounds
        playerLayer.videoGravity = .resizeAspect
    }
}

/// Полноэкранный видеоплеер с Liquid Glass контролами
struct FullScreenVideoPlayer: View {
    let attachment: MessageAttachment
    let onClose: () -> Void

    @Environment(DependencyContainer.self) private var container

    @State private var player: AVPlayer?
    @State private var isLoading = true
    @State private var error: Error?
    @State private var isPlaying = false
    @State private var currentTime: CMTime = .zero
    @State private var duration: CMTime = .zero
    @State private var showControls = true
    @State private var isDragging = false
    @State private var timeObserverToken: Any?

    private var durationSeconds: Double {
        CMTimeGetSeconds(duration)
    }

    private var currentSeconds: Double {
        CMTimeGetSeconds(currentTime)
    }

    private var progress: Double {
        guard durationSeconds > 0 else { return 0 }
        return currentSeconds / durationSeconds
    }

    var body: some View {
        ZStack {
            Color.clear.ignoresSafeArea()

            if let player = player {
                CustomVideoPlayerView(player: player)
                    .ignoresSafeArea()

                // Контролы поверх видео
                if showControls {
                    controlsOverlay
                }
            } else if isLoading {
                loadingView
            } else {
                errorView
            }
        }
        .task {
            await loadVideo()
        }
        .onDisappear {
            if let token = timeObserverToken {
                player?.removeTimeObserver(token)
                timeObserverToken = nil
            }
            player?.pause()
            player = nil
        }
        .onTapGesture {
            withAnimation(.easeInOut(duration: 0.2)) {
                showControls.toggle()
            }
        }
        .onExitCommand {
            onClose()
        }
    }

    private var loadingView: some View {
        VStack(spacing: 16) {
            ProgressView()
                .controlSize(.large)
                .tint(.white)
            Text("Загрузка видео...")
                .font(.subheadline)
                .foregroundStyle(.white.opacity(0.7))
        }
    }

    // MARK: - Controls Overlay

    private var controlsOverlay: some View {
        VStack {
            // Верхний тулбар
            topBar
                .background(
                    LinearGradient(
                        colors: [.black.opacity(0.7), .clear],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )

            Spacer()

            // Центральные контролы
            centerControls

            Spacer()

            // Нижний тулбар с прогрессом
            bottomBar
                .background(
                    LinearGradient(
                        colors: [.clear, .black.opacity(0.7)],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )
        }
        .transition(.opacity)
    }

    private var topBar: some View {
        HStack {
            // Кнопка закрытия
            Button {
                onClose()
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .font(.title)
                    .foregroundStyle(.white)
            }
            .buttonStyle(.plain)
            .glassEffect(.clear, in: .circle)

            Spacer()

            Text(attachment.fileName)
                .font(.subheadline)
                .foregroundStyle(.white)
                .lineLimit(1)

            Spacer()

            // Placeholder для симметрии
            Circle()
                .fill(.clear)
                .frame(width: 28, height: 28)
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 16)
    }

    private var centerControls: some View {
        GlassEffectContainer {
            HStack(spacing: 40) {
                // Назад на 10 сек
                Button {
                    seekBackward()
                } label: {
                    Image(systemName: "gobackward.10")
                        .font(.title)
                        .foregroundStyle(.white)
                        .frame(width: 50, height: 50)
                }
                .buttonStyle(.plain)
                .glassEffect(.regular, in: .circle)

                // Play/Pause
                Button {
                    togglePlayback()
                } label: {
                    Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                        .font(.largeTitle)
                        .foregroundStyle(.white)
                        .frame(width: 70, height: 70)
                }
                .buttonStyle(.plain)
                .glassEffect(.regular.interactive(), in: .circle)

                // Вперёд на 10 сек
                Button {
                    seekForward()
                } label: {
                    Image(systemName: "goforward.10")
                        .font(.title)
                        .foregroundStyle(.white)
                        .frame(width: 50, height: 50)
                }
                .buttonStyle(.plain)
                .glassEffect(.regular, in: .circle)
            }
        }
    }

    private var bottomBar: some View {
        VStack(spacing: 12) {
            // Прогресс бар
            GeometryReader { geo in
                ZStack(alignment: .leading) {
                    // Фон
                    Capsule()
                        .fill(.white.opacity(0.3))

                    // Прогресс
                    Capsule()
                        .fill(.white)
                        .frame(width: geo.size.width * (isDragging ? progress : progress))
                }
                .gesture(
                    DragGesture(minimumDistance: 0)
                        .onChanged { value in
                            isDragging = true
                            let newProgress = max(0, min(1, value.location.x / geo.size.width))
                            seekTo(progress: newProgress)
                        }
                        .onEnded { _ in
                            isDragging = false
                        }
                )
            }
            .frame(height: 6)
            .padding(.horizontal, 20)

            // Время
            HStack {
                Text(formatTime(currentSeconds))
                    .font(.caption)
                    .foregroundStyle(.white)
                    .monospacedDigit()

                Spacer()

                Text(formatTime(durationSeconds))
                    .font(.caption)
                    .foregroundStyle(.white)
                    .monospacedDigit()
            }
            .padding(.horizontal, 20)
        }
        .padding(.vertical, 16)
    }

    private var errorView: some View {
        VStack(spacing: 16) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 48))
                .foregroundStyle(.secondary)

            Text("Не удалось загрузить видео")
                .font(.headline)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Video Control Methods

    private func loadVideo() async {
        do {
            var videoURL: URL?

            if let previewURL = attachment.previewURL, !previewURL.isEmpty {
                videoURL = URL(string: previewURL)
            } else {
                let urlString = try await container.fileService.getDownloadURL(fileID: attachment.fileID)
                videoURL = URL(string: urlString)
            }

            if let url = videoURL {
                let playerItem = AVPlayerItem(url: url)
                player = AVPlayer(playerItem: playerItem)
                setupTimeObserver()
                player?.play()
                isPlaying = true
            }
        } catch {
            self.error = error
        }

        isLoading = false
    }

    private func setupTimeObserver() {
        guard let player = player else { return }

        let interval = CMTime(seconds: 0.1, preferredTimescale: CMTimeScale(NSEC_PER_SEC))
        timeObserverToken = player.addPeriodicTimeObserver(forInterval: interval, queue: .main) { time in
            Task { @MainActor in
                currentTime = time
                if let duration = player.currentItem?.duration {
                    self.duration = duration
                }
            }
        }
    }

    private func togglePlayback() {
        if isPlaying {
            player?.pause()
        } else {
            player?.play()
        }
        isPlaying.toggle()
    }

    private func seekBackward() {
        let newTime = CMTimeSubtract(currentTime, CMTime(seconds: 10, preferredTimescale: 1))
        player?.seek(to: max(newTime, .zero))
    }

    private func seekForward() {
        let newTime = CMTimeAdd(currentTime, CMTime(seconds: 10, preferredTimescale: 1))
        player?.seek(to: min(newTime, duration))
    }

    private func seekTo(progress: Double) {
        let newTime = CMTime(seconds: progress * durationSeconds, preferredTimescale: 1)
        player?.seek(to: newTime)
    }

    private func formatTime(_ seconds: Double) -> String {
        guard seconds.isFinite else { return "0:00" }
        let mins = Int(seconds) / 60
        let secs = Int(seconds) % 60
        return String(format: "%d:%02d", mins, secs)
    }
}

#Preview {
    FullScreenVideoPlayer(
        attachment: MessageAttachment(
            id: 1,
            type: .video,
            fileID: "test",
            fileName: "video.mp4",
            fileSize: 15_000_000
        ),
        onClose: {}
    )
    .environment(DependencyContainer())
}
