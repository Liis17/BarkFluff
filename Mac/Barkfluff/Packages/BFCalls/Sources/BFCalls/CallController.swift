//
//  CallController.swift
//  BFCalls
//
//  Оркестратор звонка: сигнализация (CallsRepository) + медиа (LiveKit Room).
//  Источник состояния для UI. Привязывается к одному CallEventsStreamManager.
//

import Foundation
import Observation
import AVFoundation
import BFNetworking
import LiveKit

@MainActor
@Observable
public final class CallController {

    // MARK: - Наблюдаемое состояние

    public private(set) var phase: CallPhase = .idle
    public private(set) var call: ActiveCallInfo?
    public private(set) var participants: [CallTile] = []
    public private(set) var localVideoTrack: VideoTrack?
    public private(set) var isMicEnabled = false
    public private(set) var isCameraEnabled = false
    public private(set) var isScreenSharing = false
    public private(set) var audioQuality: CallAudioQualityDTO = .auto
    public private(set) var videoQuality: CallVideoQualityLevel = .auto
    /// Момент начала разговора (для таймера в UI).
    public private(set) var callStartedAt: Date?

    /// Имена/аватары участников (резолвятся через `participantResolver`).
    public private(set) var displays: [Int64: CallParticipantDisplay] = [:]

    // MARK: - Зависимости

    private let callsRepository: CallsRepositoryProtocol
    private let eventsManager: CallEventsStreamManager

    /// Резолвер отображения участника (имя/аватар). Устанавливает приложение
    /// (через UserService/UserCache), чтобы BFCalls не зависел от BFCore.
    public var participantResolver: (@Sendable (Int64) async -> CallParticipantDisplay?)?

    private var room: Room?
    private var roomDelegate: RoomDelegateAdapter?
    private var observeTask: Task<Void, Never>?

    public init(callsRepository: CallsRepositoryProtocol, eventsManager: CallEventsStreamManager) {
        self.callsRepository = callsRepository
        self.eventsManager = eventsManager
    }

    // MARK: - Жизненный цикл (старт/стоп подписки на события)

    public func start() {
        guard observeTask == nil else { return }
        observeTask = Task { [weak self] in
            guard let self else { return }
            await self.eventsManager.start()
            let stream = await self.eventsManager.events
            for await event in stream {
                await self.handle(event)
            }
        }
    }

    public func stop() async {
        observeTask?.cancel()
        observeTask = nil
        await eventsManager.stop()
        await teardown()
    }

    // MARK: - Публичные действия

    /// Старт звонка: ровно одно из `calleeUserID` (1-на-1) / `chatID` (групповой).
    public func startCall(calleeUserID: Int64?, chatID: String?, media: CallMediaTypeDTO) async {
        guard phase == .idle else { return }
        let isGroup = (chatID != nil)
        call = ActiveCallInfo(callID: "", peerUserID: calleeUserID, chatID: chatID, media: media, isGroup: isGroup, isIncoming: false)
        if let calleeUserID { ensureDisplay(for: calleeUserID) }
        phase = .outgoing
        do {
            let info = try await callsRepository.initiateCall(calleeUserID: calleeUserID, chatID: chatID, media: media)
            call?.callID = info.callID
            audioQuality = info.audioQuality
            await connect(info, media: media)
        } catch {
            await teardown()
        }
    }

    public func accept() async {
        guard phase == .incoming, let current = call else { return }
        do {
            let info = try await callsRepository.acceptCall(callID: current.callID)
            audioQuality = info.audioQuality
            await connect(info, media: current.media)
        } catch {
            await teardown()
        }
    }

    public func reject() async {
        guard let current = call else { return }
        try? await callsRepository.rejectCall(callID: current.callID)
        await teardown()
    }

    public func hangUp() async {
        if let current = call, !current.callID.isEmpty {
            try? await callsRepository.endCall(callID: current.callID)
        }
        await teardown()
    }

    public func toggleMicrophone() async {
        guard let room else { return }
        let newValue = !isMicEnabled
        do {
            try await room.localParticipant.setMicrophone(enabled: newValue)
            isMicEnabled = newValue
        } catch {}
    }

    public func toggleCamera() async {
        guard let room else { return }
        let newValue = !isCameraEnabled
        if newValue { await Self.requestCameraPermission() }
        do {
            try await room.localParticipant.setCamera(enabled: newValue)
            isCameraEnabled = newValue
            refreshFromRoom()
        } catch {}
    }

    public func toggleScreenShare() async {
        guard let room else { return }
        let newValue = !isScreenSharing
        do {
            _ = try await room.localParticipant.setScreenShare(enabled: newValue)
            isScreenSharing = newValue
        } catch {}
    }

    /// Сменить общее качество голоса. Применение придёт обратно событием
    /// `audioQualityChanged` (единый источник истины, как в вебе).
    public func requestAudioQuality(_ quality: CallAudioQualityDTO) async {
        guard let current = call, !current.callID.isEmpty else { return }
        try? await callsRepository.setAudioQuality(callID: current.callID, quality: quality)
    }

    /// Локальное качество своего видео-стрима (на backend не ходит).
    public func setVideoQuality(_ level: CallVideoQualityLevel) async {
        videoQuality = level
        guard let room, isCameraEnabled, let preset = level.preset else { return }
        let publish = VideoPublishOptions(encoding: VideoEncoding(maxBitrate: preset.bitrate, maxFps: preset.fps))
        let capture = CameraCaptureOptions(dimensions: Dimensions(width: preset.width, height: preset.height), fps: preset.fps)
        try? await room.localParticipant.setCamera(enabled: false)
        try? await room.localParticipant.setCamera(enabled: true, captureOptions: capture, publishOptions: publish)
        refreshFromRoom()
    }

    // MARK: - События сигнализации

    private func handle(_ event: CallEventDTO) async {
        switch event {
        case let .incoming(callID, callerUserID, chatID, media, _):
            guard phase == .idle else { return } // занят — игнорируем ринг
            let isGroup = !chatID.isEmpty
            call = ActiveCallInfo(callID: callID, peerUserID: callerUserID, chatID: isGroup ? chatID : nil,
                                  media: media, isGroup: isGroup, isIncoming: true)
            ensureDisplay(for: callerUserID)
            phase = .incoming
        case .accepted:
            break // caller уже подключён к комнате; ждём участника
        case let .rejected(callID, _):
            if call?.callID == callID { await teardown() }
        case let .ended(callID, _, _):
            if call?.callID == callID { await teardown() }
        case .participant:
            refreshFromRoom()
        case let .audioQualityChanged(_, quality, _):
            audioQuality = quality
            await applyAudioPreset(quality)
        }
    }

    // MARK: - LiveKit Room

    private func connect(_ info: CallConnectionInfo, media: CallMediaTypeDTO) async {
        phase = .connecting
        let adapter = RoomDelegateAdapter()
        adapter.controller = self
        let room = Room(delegate: adapter)
        self.roomDelegate = adapter
        self.room = room
        do {
            // Микрофон — запрашиваем доступ до подключения.
            await Self.requestMicPermission()
            try await room.connect(
                url: info.livekitURL,
                token: info.accessToken,
                connectOptions: ConnectOptions(autoSubscribe: true),
                roomOptions: RoomOptions(adaptiveStream: true, dynacast: true)
            )
            // Микрофон — best-effort: если недоступен, звонок НЕ закрываем.
            do {
                try await room.localParticipant.setMicrophone(enabled: true)
                isMicEnabled = true
            } catch {
                isMicEnabled = false
            }
            // Камеру НЕ включаем автоматически: на macOS авто-старт захвата камеры
            // в момент connect роняет CMIO/WebRTC. Для видеозвонка камера стартует
            // выключенной — пользователь включает её кнопкой (toggleCamera).
            if phase == .connecting { phase = .active }
            if callStartedAt == nil { callStartedAt = Date() }
            refreshFromRoom()
        } catch {
            await teardown()
        }
    }

    private func applyAudioPreset(_ quality: CallAudioQualityDTO) async {
        guard let room, isMicEnabled else { return }
        let options: AudioPublishOptions? = CallAudioPreset.bitrate(for: quality).map {
            AudioPublishOptions(encoding: AudioEncoding(maxBitrate: $0))
        }
        try? await room.localParticipant.setMicrophone(enabled: false)
        try? await room.localParticipant.setMicrophone(enabled: true, publishOptions: options)
    }

    private func teardown() async {
        if let room {
            await room.disconnect()
        }
        room = nil
        roomDelegate = nil
        participants = []
        localVideoTrack = nil
        isMicEnabled = false
        isCameraEnabled = false
        isScreenSharing = false
        callStartedAt = nil
        call = nil
        phase = .idle
    }

    // MARK: - Колбэки делегата (вызываются на MainActor)

    func onConnectionState(connected: Bool) {
        if connected, phase == .connecting {
            phase = .active
            if callStartedAt == nil { callStartedAt = Date() }
        }
        refreshFromRoom()
    }

    func onRoomChanged() {
        refreshFromRoom()
    }

    func onDisconnected() {
        Task { await teardown() }
    }

    private func refreshFromRoom() {
        guard let room else {
            participants = []
            localVideoTrack = nil
            return
        }
        var tiles: [CallTile] = []
        for (_, participant) in room.remoteParticipants {
            let idString = participant.identity?.stringValue ?? ""
            let userID = Int64(idString)
            if let userID { ensureDisplay(for: userID) }
            tiles.append(CallTile(id: idString, userID: userID, isSpeaking: participant.isSpeaking,
                                  isScreenShare: false,
                                  videoTrack: Self.activeVideo(participant.videoTracks, source: .camera)))
            if let screenTrack = Self.activeVideo(participant.videoTracks, source: .screenShareVideo) {
                tiles.append(CallTile(id: idString + "#screen", userID: userID, isSpeaking: false,
                                      isScreenShare: true, videoTrack: screenTrack))
            }
        }
        participants = tiles
        localVideoTrack = Self.activeVideo(room.localParticipant.videoTracks, source: .camera)
    }

    /// Видеотрек публикации заданного источника, только если он НЕ замьючен
    /// (иначе после выключения камеры SwiftUIVideoView держит последний кадр).
    private static func activeVideo(_ publications: [TrackPublication], source: Track.Source) -> VideoTrack? {
        guard let pub = publications.first(where: { $0.source == source && !$0.isMuted }) else { return nil }
        return pub.track as? VideoTrack
    }

    /// Запрос доступа к микрофону (нужен NSMicrophoneUsageDescription в Info.plist).
    private static func requestMicPermission() async {
        _ = await AVCaptureDevice.requestAccess(for: .audio)
    }

    /// Запрос доступа к камере (нужен NSCameraUsageDescription в Info.plist).
    private static func requestCameraPermission() async {
        _ = await AVCaptureDevice.requestAccess(for: .video)
    }

    /// Лениво резолвит имя/аватар участника через `participantResolver` и кэширует.
    private func ensureDisplay(for userID: Int64) {
        guard displays[userID] == nil, let resolver = participantResolver else { return }
        displays[userID] = CallParticipantDisplay(name: "", initials: "", avatarURL: nil) // плейсхолдер от повторных запросов
        Task { [weak self] in
            guard let resolved = await resolver(userID) else { return }
            self?.displays[userID] = resolved
        }
    }
}

// MARK: - RoomDelegate адаптер (NSObject, форвардит события на MainActor)

final class RoomDelegateAdapter: NSObject, RoomDelegate, @unchecked Sendable {

    weak var controller: CallController?

    @objc
    func room(_ room: Room, didUpdateConnectionState connectionState: LiveKit.ConnectionState, from oldConnectionState: LiveKit.ConnectionState) {
        let connected = (connectionState == .connected)
        let controller = controller
        Task { @MainActor in controller?.onConnectionState(connected: connected) }
    }

    @objc
    func room(_ room: Room, didDisconnectWithError error: LiveKitError?) {
        let controller = controller
        Task { @MainActor in controller?.onDisconnected() }
    }

    @objc
    func room(_ room: Room, participantDidConnect participant: RemoteParticipant) {
        let controller = controller
        Task { @MainActor in controller?.onRoomChanged() }
    }

    @objc
    func room(_ room: Room, participantDidDisconnect participant: RemoteParticipant) {
        let controller = controller
        Task { @MainActor in controller?.onRoomChanged() }
    }

    @objc
    func room(_ room: Room, didUpdateSpeakingParticipants participants: [Participant]) {
        let controller = controller
        Task { @MainActor in controller?.onRoomChanged() }
    }

    @objc
    func room(_ room: Room, participant: RemoteParticipant, didSubscribeTrack publication: RemoteTrackPublication) {
        let controller = controller
        Task { @MainActor in controller?.onRoomChanged() }
    }

    @objc
    func room(_ room: Room, participant: RemoteParticipant, didUnsubscribeTrack publication: RemoteTrackPublication) {
        let controller = controller
        Task { @MainActor in controller?.onRoomChanged() }
    }

    @objc
    func room(_ room: Room, participant: LocalParticipant, didPublishTrack publication: LocalTrackPublication) {
        let controller = controller
        Task { @MainActor in controller?.onRoomChanged() }
    }

    @objc
    func room(_ room: Room, participant: Participant, trackPublication: TrackPublication, didUpdateIsMuted isMuted: Bool) {
        let controller = controller
        Task { @MainActor in controller?.onRoomChanged() }
    }
}
