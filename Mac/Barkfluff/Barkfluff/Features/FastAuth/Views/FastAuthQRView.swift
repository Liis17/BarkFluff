//
//  FastAuthQRView.swift
//  Barkfluff
//
//  Быстрая авторизация через QR-код
//

import SwiftUI
import BFCore

struct FastAuthQRView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var token: FastAuthToken?
    @State private var status: FastAuthStatus = .pending
    @State private var timeRemaining: Int = 0

    var onAuthenticated: ((AuthTokens) -> Void)?

    var body: some View {
        NavigationStack {
            VStack(spacing: 24) {
                if let token {
                    // QR-код
                    VStack(spacing: 16) {
                        Text("Отсканируйте QR-код")
                            .font(.title2)
                            .fontWeight(.semibold)

                        Text("Используйте камеру телефона в приложении BarkFluff")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)

                        // TODO: Показать реальный QR-код
                        QRCodePlaceholder(token: token.token)
                            .frame(width: 200, height: 200)

                        // Код для ручного ввода
                        VStack(spacing: 4) {
                            Text("Или введите код:")
                                .font(.caption)
                                .foregroundStyle(.secondary)

                            Text(token.token)
                                .font(.system(.title2, design: .monospaced))
                                .fontWeight(.medium)
                                .padding(.horizontal, 16)
                                .padding(.vertical, 8)
                                .background(Color.secondary.opacity(0.1))
                                .clipShape(RoundedRectangle(cornerRadius: 8))
                        }

                        // Статус
                        statusView(for: status)

                        // Таймер
                        if timeRemaining > 0 {
                            Text("Код действителен \(timeRemaining) сек")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                        }
                    }
                } else {
                    ProgressView("Генерация кода...")
                }
            }
            .padding()
            .navigationTitle("Быстрый вход")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Отмена") {
                        dismiss()
                    }
                }
            }
            .task {
                await generateToken()
            }
        }
    }

    @ViewBuilder
    private func statusView(for status: FastAuthStatus) -> some View {
        switch status {
        case .pending:
            HStack(spacing: 8) {
                ProgressView()
                    .scaleEffect(0.8)
                Text("Ожидание сканирования...")
            }
            .foregroundStyle(.secondary)

        case .accepted:
            HStack(spacing: 8) {
                Image(systemName: "checkmark.circle.fill")
                    .foregroundStyle(.green)
                Text("Успешно!")
            }
            .font(.headline)

        case .rejected:
            HStack(spacing: 8) {
                Image(systemName: "xmark.circle.fill")
                    .foregroundStyle(.red)
                Text("Отклонено")
            }
            .font(.headline)

        case .expired:
            VStack(spacing: 8) {
                HStack(spacing: 8) {
                    Image(systemName: "clock.fill")
                        .foregroundStyle(.orange)
                    Text("Время истекло")
                }

                Button("Получить новый код") {
                    Task {
                        await generateToken()
                    }
                }
                .buttonStyle(.borderedProminent)
            }
        }
    }

    private func generateToken() async {
        // TODO: Реализовать через FastAuthService
        // token = try? await fastAuthService.generateToken(type: .qr)
    }
}

struct QRCodePlaceholder: View {
    let token: String

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 12)
                .fill(Color.white)

            // Заглушка QR-кода
            Grid(horizontalSpacing: 2, verticalSpacing: 2) {
                ForEach(0..<21, id: \.self) { _ in
                    GridRow {
                        ForEach(0..<21, id: \.self) { _ in
                            Rectangle()
                                .fill(Bool.random() ? Color.black : Color.clear)
                                .frame(width: 8, height: 8)
                        }
                    }
                }
            }
            .padding(8)
        }
        .shadow(radius: 4)
    }
}

#Preview {
    FastAuthQRView()
}
