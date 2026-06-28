//
//  CallViews.swift
//  BFCalls
//
//  Общие (кросс-платформенные) SwiftUI-вью звонка: ринг, экран, контролы,
//  плитки участников, self-PiP, панель качества, свёрнутая плашка.
//  Монтирование (оверлей/плавающее окно) — на стороне приложений.
//

import SwiftUI
import LiveKit
import BFNetworking

// MARK: - Аватар

struct CallAvatar: View {
    let display: CallParticipantDisplay?
    let size: CGFloat

    var body: some View {
        ZStack {
            Circle().fill(Color.accentColor.opacity(0.25))
            if let urlString = display?.avatarURL, let url = URL(string: urlString) {
                AsyncImage(url: url) { image in
                    image.resizable().scaledToFill()
                } placeholder: {
                    initialsText
                }
                .clipShape(Circle())
            } else {
                initialsText
            }
        }
        .frame(width: size, height: size)
    }

    private var initialsText: some View {
        Text((display?.initials.isEmpty == false) ? display!.initials : "?")
            .font(.system(size: size * 0.4, weight: .semibold))
            .foregroundStyle(.primary)
    }
}

// MARK: - Видео с адаптацией под соотношение сторон

/// Рендер видеотрека. Соотношение сторон контейнера берётся из `track.dimensions`,
/// поэтому блок адаптируется под реальную картинку (без искажений и серых полей).
struct CallVideoView: View {
    let track: VideoTrack
    var mirror: Bool = false

    var body: some View {
        SwiftUIVideoView(track, layoutMode: .fill, mirrorMode: mirror ? .mirror : .auto)
            .aspectRatio(aspectRatio, contentMode: .fit)
    }

    /// Соотношение сторон видео (nil, пока размеры неизвестны — тогда контейнер заполняется).
    var aspectRatio: CGFloat? {
        guard let dimensions = track.dimensions, dimensions.width > 0, dimensions.height > 0 else { return nil }
        return CGFloat(dimensions.width) / CGFloat(dimensions.height)
    }
}

/// Соотношение сторон видеотрека для контейнера (дефолт 4:3, пока размеры неизвестны).
func callVideoAspect(_ track: VideoTrack?) -> CGFloat {
    guard let d = track?.dimensions, d.width > 0, d.height > 0 else { return 4.0 / 3.0 }
    return CGFloat(d.width) / CGFloat(d.height)
}

// MARK: - Таймер разговора

struct CallTimerView: View {
    let startedAt: Date?

    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { _ in
            Text(text).monospacedDigit()
        }
    }

    private var text: String {
        guard let startedAt else { return "00:00" }
        let total = max(0, Int(Date().timeIntervalSince(startedAt)))
        return String(format: "%02d:%02d", total / 60, total % 60)
    }
}

// MARK: - Кнопки управления

struct CallControlButton: View {
    let system: String
    var active: Bool = false
    var danger: Bool = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: system)
                .font(.title2)
                .frame(width: 54, height: 54)
                .background(background, in: Circle())
                .foregroundStyle(.white)
        }
        .buttonStyle(.plain)
    }

    private var background: Color {
        if danger { return .red }
        return active ? .accentColor : Color.gray.opacity(0.35)
    }
}

struct CallControlButtonSmall: View {
    let system: String
    var danger: Bool = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: system)
                .font(.callout)
                .frame(width: 34, height: 34)
                .background(danger ? Color.red : Color.gray.opacity(0.35), in: Circle())
                .foregroundStyle(.white)
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Плитка участника

struct CallTileView: View {
    let tile: CallTile
    let display: CallParticipantDisplay?
    var onExpand: (() -> Void)? = nil

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 16).fill(Color.black.opacity(0.85))
            if let track = tile.videoTrack {
                // Плитка повторяет соотношение сторон видео → SwiftUIVideoView.fill
                // заполняет её без искажений и серых полей.
                SwiftUIVideoView(track, layoutMode: .fill, mirrorMode: .auto)
                    .clipShape(RoundedRectangle(cornerRadius: 16))
            } else {
                CallAvatar(display: display, size: 96)
            }
            VStack {
                HStack {
                    Spacer()
                    if tile.videoTrack != nil, let onExpand {
                        Button(action: onExpand) {
                            Image(systemName: "arrow.up.left.and.arrow.down.right")
                                .font(.caption)
                                .padding(6)
                                .background(.black.opacity(0.5), in: Circle())
                                .foregroundStyle(.white)
                        }
                        .buttonStyle(.plain)
                    }
                }
                Spacer()
                HStack {
                    Text(label)
                        .font(.caption)
                        .lineLimit(1)
                        .padding(.horizontal, 8).padding(.vertical, 4)
                        .background(.black.opacity(0.5), in: Capsule())
                        .foregroundStyle(.white)
                    Spacer()
                }
            }
            .padding(8)
        }
        .aspectRatio(tile.videoTrack != nil ? callVideoAspect(tile.videoTrack) : 4.0 / 3.0, contentMode: .fit)
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .strokeBorder(Color.green, lineWidth: tile.isSpeaking ? 3 : 0)
        )
        .contentShape(RoundedRectangle(cornerRadius: 16))
        .onTapGesture { if tile.videoTrack != nil { onExpand?() } }
    }

    private var label: String {
        let name = (display?.name.isEmpty == false) ? display!.name : (tile.userID.map { "ID \($0)" } ?? "—")
        return tile.isScreenShare ? "\(name) · демонстрация экрана" : name
    }
}

// MARK: - Сетка участников

struct CallParticipantsGrid: View {
    let controller: CallController
    var onExpand: (VideoTrack) -> Void

    private let columns = [GridItem(.adaptive(minimum: 200), spacing: 12)]

    var body: some View {
        if controller.participants.isEmpty {
            waiting
        } else {
            ScrollView {
                LazyVGrid(columns: columns, spacing: 12) {
                    ForEach(controller.participants) { tile in
                        CallTileView(
                            tile: tile,
                            display: tile.userID.flatMap { controller.displays[$0] },
                            onExpand: { if let track = tile.videoTrack { onExpand(track) } }
                        )
                    }
                }
                .padding(12)
            }
        }
    }

    private var waiting: some View {
        let display = controller.call?.peerUserID.flatMap { controller.displays[$0] }
        return VStack(spacing: 16) {
            CallAvatar(display: display, size: 110)
            Text((display?.name.isEmpty == false) ? display!.name : "Соединение…")
                .font(.title3.bold())
            Text(controller.phase == .outgoing ? "Вызов…" : "Подключение…")
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

// MARK: - Панель качества

struct CallQualityPanel: View {
    let controller: CallController

    private let audioLevels: [(CallAudioQualityDTO, String)] = [
        (.auto, "Авто"), (.low, "Низкое"), (.medium, "Среднее"), (.high, "Высокое"),
    ]
    private let videoLevels: [(CallVideoQualityLevel, String)] = [
        (.auto, "Авто"), (.low, "Низкое"), (.medium, "Среднее"), (.high, "Высокое"),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Голос · для всех").font(.caption).foregroundStyle(.secondary)
            HStack(spacing: 8) {
                ForEach(audioLevels, id: \.0) { level, title in
                    chip(title, selected: controller.audioQuality == level) {
                        Task { await controller.requestAudioQuality(level) }
                    }
                }
            }
            if controller.isCameraEnabled {
                Text("Видео · ваш стрим").font(.caption).foregroundStyle(.secondary)
                HStack(spacing: 8) {
                    ForEach(videoLevels, id: \.0) { level, title in
                        chip(title, selected: controller.videoQuality == level) {
                            Task { await controller.setVideoQuality(level) }
                        }
                    }
                }
            }
        }
        .padding()
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
    }

    private func chip(_ title: String, selected: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.caption)
                .padding(.horizontal, 10).padding(.vertical, 6)
                .background(selected ? Color.accentColor : Color.gray.opacity(0.25), in: Capsule())
                .foregroundStyle(selected ? .white : .primary)
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Панель контролов

struct CallControlsBar: View {
    let controller: CallController
    @Binding var showQuality: Bool

    var body: some View {
        HStack(spacing: 16) {
            CallControlButton(system: controller.isMicEnabled ? "mic.fill" : "mic.slash.fill",
                              active: controller.isMicEnabled, danger: !controller.isMicEnabled) {
                Task { await controller.toggleMicrophone() }
            }
            CallControlButton(system: controller.isCameraEnabled ? "video.fill" : "video.slash.fill",
                              active: controller.isCameraEnabled, danger: !controller.isCameraEnabled) {
                Task { await controller.toggleCamera() }
            }
            CallControlButton(system: "rectangle.on.rectangle",
                              active: controller.isScreenSharing) {
                Task { await controller.toggleScreenShare() }
            }
            CallControlButton(system: "slider.horizontal.3", active: showQuality) {
                showQuality.toggle()
            }
            CallControlButton(system: "phone.down.fill", danger: true) {
                Task { await controller.hangUp() }
            }
        }
    }
}

// MARK: - Входящий звонок (ринг)

public struct IncomingCallView: View {
    let controller: CallController

    public init(controller: CallController) {
        self.controller = controller
    }

    public var body: some View {
        let call = controller.call
        let display = call?.peerUserID.flatMap { controller.displays[$0] }
        VStack(spacing: 20) {
            CallAvatar(display: display, size: 96)
            Text(call?.isGroup == true
                 ? "Групповой звонок"
                 : ((display?.name.isEmpty == false) ? display!.name : "Входящий звонок"))
                .font(.title2.bold())
            Text(call?.media == .video ? "Видеозвонок" : "Аудиозвонок")
                .foregroundStyle(.secondary)
            HStack(spacing: 48) {
                Button { Task { await controller.reject() } } label: {
                    ringButton("phone.down.fill", .red)
                }
                Button { Task { await controller.accept() } } label: {
                    ringButton("phone.fill", .green)
                }
            }
            .buttonStyle(.plain)
            .padding(.top, 8)
        }
        .padding(32)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 24))
    }

    private func ringButton(_ system: String, _ color: Color) -> some View {
        Image(systemName: system)
            .font(.title)
            .frame(width: 64, height: 64)
            .background(color, in: Circle())
            .foregroundStyle(.white)
    }
}

// MARK: - Экран звонка (развёрнутый)

public struct CallScreenView: View {
    let controller: CallController
    var onMinimize: (() -> Void)?
    @State private var showQuality = false
    @State private var fullscreenTrack: VideoTrack?

    public init(controller: CallController, onMinimize: (() -> Void)? = nil) {
        self.controller = controller
        self.onMinimize = onMinimize
    }

    public var body: some View {
        ZStack {
            VStack(spacing: 0) {
                HStack {
                    Text(controller.call?.isGroup == true ? "Групповой звонок" : "Звонок")
                        .font(.headline)
                    Spacer()
                    CallTimerView(startedAt: controller.callStartedAt)
                        .font(.headline)
                    if let onMinimize {
                        Button(action: onMinimize) {
                            Image(systemName: "minus.circle.fill").font(.title3)
                        }
                        .buttonStyle(.plain)
                        .padding(.leading, 8)
                    }
                }
                .padding()

                ZStack(alignment: .bottomTrailing) {
                    CallParticipantsGrid(controller: controller) { track in
                        fullscreenTrack = track
                    }
                    if let local = controller.localVideoTrack {
                        CallVideoView(track: local, mirror: true)
                            .frame(width: 150)
                            .clipShape(RoundedRectangle(cornerRadius: 12))
                            .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(.white.opacity(0.3)))
                            .padding(16)
                            .onTapGesture { fullscreenTrack = local }
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)

                ZStack(alignment: .bottom) {
                    if showQuality {
                        CallQualityPanel(controller: controller)
                            .padding(.bottom, 84)
                            .transition(.opacity)
                    }
                    CallControlsBar(controller: controller, showQuality: $showQuality)
                        .padding()
                }
            }

            if let track = fullscreenTrack {
                CallFullscreenVideo(track: track) { fullscreenTrack = nil }
                    .transition(.opacity)
                    .zIndex(10)
            }
        }
        .animation(.easeInOut(duration: 0.2), value: showQuality)
        .animation(.easeInOut(duration: 0.2), value: fullscreenTrack != nil)
    }
}

// MARK: - Видео на весь экран

struct CallFullscreenVideo: View {
    let track: VideoTrack
    var onClose: () -> Void

    var body: some View {
        ZStack(alignment: .topTrailing) {
            Color.black.ignoresSafeArea()
            CallVideoView(track: track)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            Button(action: onClose) {
                Image(systemName: "xmark.circle.fill")
                    .font(.largeTitle)
                    .foregroundStyle(.white)
            }
            .buttonStyle(.plain)
            .padding()
        }
        .contentShape(Rectangle())
        .onTapGesture { onClose() }
    }
}

// MARK: - Свёрнутая плашка (macOS)

public struct CallMinimizedBar: View {
    let controller: CallController
    var onExpand: () -> Void

    public init(controller: CallController, onExpand: @escaping () -> Void) {
        self.controller = controller
        self.onExpand = onExpand
    }

    public var body: some View {
        let display = controller.call?.peerUserID.flatMap { controller.displays[$0] }
        HStack(spacing: 12) {
            CallAvatar(display: display, size: 36)
            VStack(alignment: .leading, spacing: 2) {
                Text(controller.call?.isGroup == true
                     ? "Групповой звонок"
                     : ((display?.name.isEmpty == false) ? display!.name : "Звонок"))
                    .font(.subheadline.bold())
                    .lineLimit(1)
                CallTimerView(startedAt: controller.callStartedAt)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            CallControlButtonSmall(system: controller.isMicEnabled ? "mic.fill" : "mic.slash.fill",
                                   danger: !controller.isMicEnabled) {
                Task { await controller.toggleMicrophone() }
            }
            CallControlButtonSmall(system: "phone.down.fill", danger: true) {
                Task { await controller.hangUp() }
            }
            Button(action: onExpand) {
                Image(systemName: "arrow.up.left.and.arrow.down.right").font(.callout)
            }
            .buttonStyle(.plain)
        }
        .padding(12)
        .frame(width: 320)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
        .shadow(radius: 12)
    }
}
