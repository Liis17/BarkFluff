//
//  QRCameraPreview.swift
//  Barkfluff (iOS)
//
//  AVFoundation-обёртка для распознавания QR-кодов через заднюю камеру.
//

import SwiftUI
import UIKit
import AVFoundation

struct QRCameraPreview: UIViewControllerRepresentable {
    let isActive: Bool
    let onQRDetected: (String) -> Void

    func makeCoordinator() -> Coordinator {
        Coordinator(onQRDetected: onQRDetected)
    }

    func makeUIViewController(context: Context) -> QRCameraViewController {
        let controller = QRCameraViewController()
        controller.coordinator = context.coordinator
        return controller
    }

    func updateUIViewController(_ uiViewController: QRCameraViewController, context: Context) {
        context.coordinator.onQRDetected = onQRDetected
        uiViewController.setRunning(isActive)
    }

    final class Coordinator: NSObject, AVCaptureMetadataOutputObjectsDelegate {
        var onQRDetected: (String) -> Void
        private var lastEmittedValue: String?
        private var lastEmittedAt: Date = .distantPast

        init(onQRDetected: @escaping (String) -> Void) {
            self.onQRDetected = onQRDetected
        }

        func metadataOutput(
            _ output: AVCaptureMetadataOutput,
            didOutput metadataObjects: [AVMetadataObject],
            from connection: AVCaptureConnection
        ) {
            guard
                let object = metadataObjects.first as? AVMetadataMachineReadableCodeObject,
                object.type == .qr,
                let value = object.stringValue,
                !value.isEmpty
            else { return }

            // Дебаунс: не дёргать колбэк чаще раза в 1.5 секунды на одно и то же значение.
            let now = Date()
            if value == lastEmittedValue, now.timeIntervalSince(lastEmittedAt) < 1.5 {
                return
            }
            lastEmittedValue = value
            lastEmittedAt = now

            let callback = onQRDetected
            DispatchQueue.main.async {
                callback(value)
            }
        }
    }
}

final class QRCameraViewController: UIViewController {
    fileprivate weak var coordinator: QRCameraPreview.Coordinator?

    private let session = AVCaptureSession()
    private let sessionQueue = DispatchQueue(label: "barkfluff.qr-camera.session")
    private var previewLayer: AVCaptureVideoPreviewLayer?
    private var isConfigured = false
    private var shouldRun = false

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer?.frame = view.bounds
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        setRunning(false)
    }

    func setRunning(_ run: Bool) {
        shouldRun = run
        if run {
            configureIfNeeded { [weak self] in
                self?.sessionQueue.async {
                    guard let self, self.shouldRun, !self.session.isRunning else { return }
                    self.session.startRunning()
                }
            }
        } else {
            sessionQueue.async { [weak self] in
                guard let self, self.session.isRunning else { return }
                self.session.stopRunning()
            }
        }
    }

    private func configureIfNeeded(completion: @escaping () -> Void) {
        if isConfigured {
            completion()
            return
        }
        isConfigured = true

        sessionQueue.async { [weak self] in
            guard let self else { return }
            self.session.beginConfiguration()
            self.session.sessionPreset = .high

            guard
                let device = AVCaptureDevice.default(for: .video),
                let input = try? AVCaptureDeviceInput(device: device),
                self.session.canAddInput(input)
            else {
                self.session.commitConfiguration()
                return
            }
            self.session.addInput(input)

            let metadataOutput = AVCaptureMetadataOutput()
            guard self.session.canAddOutput(metadataOutput) else {
                self.session.commitConfiguration()
                return
            }
            self.session.addOutput(metadataOutput)
            metadataOutput.setMetadataObjectsDelegate(self.coordinator, queue: .main)
            metadataOutput.metadataObjectTypes = [.qr]

            self.session.commitConfiguration()

            DispatchQueue.main.async {
                let layer = AVCaptureVideoPreviewLayer(session: self.session)
                layer.videoGravity = .resizeAspectFill
                layer.frame = self.view.bounds
                self.view.layer.addSublayer(layer)
                self.previewLayer = layer
                completion()
            }
        }
    }
}
