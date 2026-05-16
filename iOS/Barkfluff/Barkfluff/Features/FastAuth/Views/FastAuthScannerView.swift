//
//  FastAuthScannerView.swift
//  Barkfluff (iOS)
//
//  Экран сканирования QR-кода для подключения нового устройства.
//

import SwiftUI
import UIKit
import BFCore

struct FastAuthScannerView: View {
    @Environment(DependencyContainer.self) private var container
    @Binding var isPresented: Bool
    @State private var viewModel = FastAuthScannerViewModel()
    @State private var showOpenSettingsAlert = false

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            if viewModel.isCameraRunning {
                QRCameraPreview(
                    isActive: viewModel.isCameraRunning,
                    onQRDetected: viewModel.handleQRDetected
                )
                .ignoresSafeArea()

                ScannerOverlay()
                    .ignoresSafeArea()
            }

            VStack {
                Spacer()
                Text(viewModel.hintText)
                    .font(.callout)
                    .foregroundStyle(.white)
                    .padding(.horizontal, 24)
                    .padding(.vertical, 12)
                    .background(.black.opacity(0.55), in: Capsule())
                    .padding(.bottom, 48)

                if case .processing = viewModel.phase {
                    ProgressView()
                        .progressViewStyle(.circular)
                        .tint(.white)
                        .padding(.bottom, 32)
                }
            }
        }
        .navigationTitle("Сканирование QR")
        .navigationBarTitleDisplayMode(.inline)
        .toolbarColorScheme(.dark, for: .navigationBar)
        .task {
            viewModel.bind(service: container.fastAuthService)
            await viewModel.requestPermissionAndStart()
            if case .needsPermission = viewModel.phase {
                showOpenSettingsAlert = true
            }
        }
        .navigationDestination(item: $viewModel.scannedInfo) { info in
            FastAuthConfirmView(
                info: info,
                isFlowPresented: $isPresented,
                onReturnToScanning: { viewModel.resetToScanning() }
            )
        }
        .alert("Нет доступа к камере", isPresented: $showOpenSettingsAlert) {
            Button("Открыть настройки") {
                if let url = URL(string: UIApplication.openSettingsURLString) {
                    UIApplication.shared.open(url)
                }
            }
            Button("Отмена", role: .cancel) {}
        } message: {
            Text("Чтобы сканировать QR-код, разрешите доступ к камере в Настройках iOS.")
        }
    }
}

private struct ScannerOverlay: View {
    var body: some View {
        GeometryReader { proxy in
            let side = min(proxy.size.width, proxy.size.height) * 0.7
            let frame = CGRect(
                x: (proxy.size.width - side) / 2,
                y: (proxy.size.height - side) / 2,
                width: side,
                height: side
            )

            ZStack {
                Color.black.opacity(0.5)
                    .mask(
                        Rectangle()
                            .overlay(
                                RoundedRectangle(cornerRadius: 24, style: .continuous)
                                    .frame(width: side, height: side)
                                    .blendMode(.destinationOut)
                            )
                            .compositingGroup()
                    )

                RoundedRectangle(cornerRadius: 24, style: .continuous)
                    .stroke(Color.white.opacity(0.9), lineWidth: 3)
                    .frame(width: frame.width, height: frame.height)
                    .position(x: frame.midX, y: frame.midY)
            }
        }
    }
}
