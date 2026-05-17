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
    case failed(LocalizedStringResource)

    static func == (lhs: FastAuthScannerPhase, rhs: FastAuthScannerPhase) -> Bool {
        switch (lhs, rhs) {
        case (.requestingPermission, .requestingPermission),
             (.scanning, .scanning),
             (.processing, .processing),
             (.needsPermission, .needsPermission):
            return true
        case (.failed(let lhsMessage), .failed(let rhsMessage)):
            return String(localized: lhsMessage) == String(localized: rhsMessage)
        default:
            return false
        }
    }
}

@Observable
@MainActor
final class FastAuthScannerViewModel {

    var phase: FastAuthScannerPhase = .requestingPermission
    var scannedInfo: ScanFastAuthInfo?

    private var fastAuthService: FastAuthServiceProtocol?

    var hintText: LocalizedStringResource {
        switch phase {
        case .requestingPermission: return LocalizedStringResource("auth.fast_auth.scanner.hint.requesting")
        case .scanning: return LocalizedStringResource("auth.fast_auth.scanner.hint.scanning")
        case .processing: return LocalizedStringResource("auth.fast_auth.scanner.hint.processing")
        case .needsPermission: return LocalizedStringResource("auth.fast_auth.scanner.hint.no_permission")
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
            phase = .failed(LocalizedStringResource("auth.fast_auth.scanner.error.service_not_ready"))
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
                self.phase = .failed(LocalizedStringResource("auth.fast_auth.scanner.error.invalid_qr"))
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
