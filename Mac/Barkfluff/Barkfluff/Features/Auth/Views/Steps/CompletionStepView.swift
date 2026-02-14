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
                    .frame(width: 120, height: 120)

                Image(systemName: "checkmark.circle.fill")
                    .font(.system(size: 80))
                    .foregroundStyle(.green)
                    .symbolEffect(.bounce, options: .repeating, value: showConfetti)
            }

            // Текст
            VStack(spacing: Theme.Spacing.sm) {
                Text("Добро пожаловать!")
                    .font(.largeTitle)
                    .fontWeight(.bold)

                Text("Аккаунт успешно создан")
                    .font(.title3)
                    .foregroundStyle(.secondary)
            }

            // Краткая информация о созданном аккаунте
            VStack(spacing: Theme.Spacing.md) {
                infoRow(icon: "person.fill", label: "Имя", value: "\(data.firstName) \(data.lastName)")
                infoRow(icon: "at", label: "Username", value: data.username)
                infoRow(icon: "envelope.fill", label: "Email", value: data.email)

                if data.is2FAEnabled {
                    infoRow(icon: "shield.checkered", label: "2FA", value: "Включена")
                }
            }
            .padding(Theme.Spacing.md)
            .background(Color(.windowBackgroundColor))
            .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))

            // Кнопка продолжения
            Button(action: onComplete) {
                Text("Начать общение")
                    .font(.headline)
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .frame(width: 250)
            .padding(.top, Theme.Spacing.lg)
        }
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
