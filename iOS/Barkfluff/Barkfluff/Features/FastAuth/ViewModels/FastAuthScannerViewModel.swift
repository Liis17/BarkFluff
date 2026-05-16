//
//  FastAuthScannerViewModel.swift
//  Barkfluff (iOS)
//
//  Состояние экрана сканирования QR-кода для FastAuth.
//

import Foundation
import AVFoundation
import BFCore
import Observation

enum FastAuthScannerPhase: Equatable {
    case requestingPermission
    case scanning
    case processing
    case needsPermission
    case failed(String)
}

@Observable
@MainActor
final class FastAuthScannerViewModel {

    var phase: FastAuthScannerPhase = .requestingPermission
    var scannedInfo: ScanFastAuthInfo?

    private var fastAuthService: FastAuthServiceProtocol?

    var hintText: String {
        switch phase {
        case .requestingPermission: return "Запрашиваем доступ к камере…"
        case .scanning: return "Наведите камеру на QR-код"
        case .processing: return "Проверяем QR-код…"
        case .needsPermission: return "Нет доступа к камере"
        case .failed(let message): return message
        }
    }

    var isCameraRunning: Bool {
        switch phase {
        case .scanning, .processing, .failed: return true
        case .requestingPermission, .needsPermission: return false
        }
    }

    func bind(service: FastAuthServiceProtocol) {
        self.fastAuthService = service
    }

    func requestPermissionAndStart() async {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            phase = .scanning
        case .notDetermined:
            let granted = await AVCaptureDevice.requestAccess(for: .video)
            phase = granted ? .scanning : .needsPermission
        case .denied, .restricted:
            phase = .needsPermission
        @unknown default:
            phase = .needsPermission
        }
    }

    func handleQRDetected(_ raw: String) {
        guard case .scanning = phase else { return }
        guard let service = fastAuthService else {
            phase = .failed("Сервис не инициализирован")
            return
        }
        phase = .processing
        Task { [weak self] in
            do {
                let info = try await service.scan(fastAuthID: raw)
                guard let self else { return }
                self.scannedInfo = info
            } catch {
                guard let self else { return }
                self.phase = .failed("Неверный QR-код или сессия истекла")
                try? await Task.sleep(for: .seconds(2))
                if case .failed = self.phase {
                    self.phase = .scanning
                }
            }
        }
    }

    func resetToScanning() {
        scannedInfo = nil
        phase = .scanning
    }
}
