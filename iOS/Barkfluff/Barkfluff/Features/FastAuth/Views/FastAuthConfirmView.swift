//
//  FastAuthConfirmView.swift
//  Barkfluff (iOS)
//
//  Экран подтверждения / отклонения входа нового устройства после
//  успешного сканирования QR.
//

import SwiftUI
import BFCore

struct FastAuthConfirmView: View {
    @Environment(DependencyContainer.self) private var container
    @Environment(\.dismiss) private var dismiss

    let info: ScanFastAuthInfo
    /// Бинд, управляющий показом всего FastAuth-флоу из SessionsView.
    /// Выставление в `false` закрывает и Scanner, и Confirm одновременно.
    @Binding var isFlowPresented: Bool
    /// Колбэк для возврата сканера в режим сканирования (например, после отклонения).
    let onReturnToScanning: () -> Void

    @State private var isProcessing = false
    @State private var errorMessage: LocalizedStringResource?

    var body: some View {
        List {
            Section("auth.fast_auth.confirm.section.device") {
                row("auth.fast_auth.confirm.field.name", info.deviceName)
                row("auth.fast_auth.confirm.field.os", info.operationSystem)
                row("auth.fast_auth.confirm.field.app", "\(info.appName) \(info.appVersion)".trimmingCharacters(in: .whitespaces))
                if !info.ipAddress.isEmpty {
                    row("auth.fast_auth.confirm.field.ip", info.ipAddress)
                }
            }

            if let errorMessage {
                Section {
                    Text(errorMessage)
                        .foregroundStyle(.red)
                        .font(.footnote)
                }
            }

            Section {
                Button {
                    Task { await accept() }
                } label: {
                    HStack {
                        Spacer()
                        Text("auth.fast_auth.confirm.accept")
                            .fontWeight(.semibold)
                        Spacer()
                    }
                }
                .disabled(isProcessing)

                Button(role: .destructive) {
                    Task { await reject() }
                } label: {
                    HStack {
                        Spacer()
                        Text("auth.fast_auth.confirm.reject")
                        Spacer()
                    }
                }
                .disabled(isProcessing)
            }
        }
        .navigationTitle("auth.fast_auth.confirm.title")
        .navigationBarTitleDisplayMode(.inline)
        .overlay {
            if isProcessing {
                ProgressView()
                    .progressViewStyle(.circular)
                    .padding(20)
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
            }
        }
    }

    @ViewBuilder
    private func row(_ titleKey: LocalizedStringKey, _ value: String) -> some View {
        HStack {
            Text(titleKey).foregroundStyle(.secondary)
            Spacer()
            Text(verbatim: value.isEmpty ? "—" : value)
                .multilineTextAlignment(.trailing)
        }
    }

    private func accept() async {
        isProcessing = true
        errorMessage = nil
        defer { isProcessing = false }
        do {
            try await container.fastAuthService.accept(
                fastAuthID: info.fastAuthID,
                confirmationCode: info.confirmationCode
            )
            // Закрываем весь FastAuth-флоу (и сам Confirm, и Scanner с активной камерой).
            isFlowPresented = false
        } catch {
            errorMessage = LocalizedStringResource("auth.fast_auth.confirm.error.accept \(error.localizedDescription)")
        }
    }

    private func reject() async {
        isProcessing = true
        errorMessage = nil
        defer { isProcessing = false }
        do {
            try await container.fastAuthService.reject(
                fastAuthID: info.fastAuthID,
                confirmationCode: info.confirmationCode
            )
            // После отклонения возвращаемся в режим сканирования (камера остаётся открытой).
            onReturnToScanning()
            dismiss()
        } catch {
            errorMessage = LocalizedStringResource("auth.fast_auth.confirm.error.reject \(error.localizedDescription)")
        }
    }
}
