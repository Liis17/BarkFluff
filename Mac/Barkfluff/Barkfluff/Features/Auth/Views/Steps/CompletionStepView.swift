//
//  CompletionStepView.swift
//  Barkfluff
//
//  Шаг 9: Завершение регистрации
//

import SwiftUI

/// Шаг регистрации: завершение
struct CompletionStepView: View {
    let data: RegistrationData
    let onComplete: () -> Void

    @State private var showConfetti = false

    var body: some View {
        VStack(spacing: Theme.Spacing.xl) {
            // Иконка успеха
            ZStack {
                Circle()
                    .fill(Color.green.opacity(0.1))
                    .frame(width: 100, height: 100)

                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 60))
                    .foregroundStyle(.green)
                    .symbolEffect(.bounce, options: .repeating, value: showConfetti)
            }

            // Текст
            VStack(spacing: Theme.Spacing.sm) {
                Text("Добро пожаловать!")
                    .font(.title)
                    .fontWeight(.bold)

                Text("Аккаунт успешно создан")
                    .font(.body)
                    .foregroundStyle(.secondary)
            }

            // Краткая информация о созданном аккаунте в glass-контейнере
            VStack(spacing: Theme.Spacing.md) {
                infoRow(icon: "person.fill", label: "Имя", value: "\(data.firstName) \(data.lastName)")
                infoRow(icon: "at", label: "Username", value: data.username)
                infoRow(icon: "envelope.fill", label: "Email", value: data.email)

                if data.is2FAEnabled {
                    infoRow(icon: "shield.checkered", label: "2FA", value: "Включена")
                }
            }
            .padding(Theme.Spacing.lg)
            .frame(maxWidth: 280)
            .glassEffect(.regular, in: RoundedRectangle(cornerRadius: Theme.Radius.xl))
            .padding(Theme.Spacing.md) // Отступ для тени glass

            // Кнопка продолжения
            Button(action: onComplete) {
                Text("Начать общение")
                    .font(.headline)
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .frame(width: 220)
            .padding(.top, Theme.Spacing.md)
        }
        .frame(maxWidth: 400)
        .onAppear {
            showConfetti = true
        }
    }

    @ViewBuilder
    private func infoRow(icon: String, label: String, value: String) -> some View {
        HStack {
            Image(systemName: icon)
                .frame(width: 24)
                .foregroundStyle(.secondary)

            Text(label)
                .foregroundStyle(.secondary)

            Spacer()

            Text(value)
                .fontWeight(.medium)
        }
        .font(.subheadline)
    }
}

#Preview {
    let data = RegistrationData()
    data.firstName = "Иван"
    data.lastName = "Иванов"
    data.username = "ivan_ivanov"
    data.email = "ivan@example.com"
    data.is2FAEnabled = true

    return CompletionStepView(data: data, onComplete: {
        print("Complete!")
    })
    .padding()
    .frame(width: 400, height: 500)
}
