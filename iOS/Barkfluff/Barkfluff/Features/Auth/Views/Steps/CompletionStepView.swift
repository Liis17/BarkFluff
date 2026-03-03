//
//  CompletionStepView.swift
//  Barkfluff
//
//  Шаг 9: Завершение регистрации (iOS)
//

import SwiftUI

struct CompletionStepView: View {
    let data: RegistrationData
    let onComplete: () -> Void

    var body: some View {
        VStack(spacing: 24) {
            // Иконка успеха
            Image(systemName: "checkmark.circle.fill")
                .font(.system(size: 64))
                .foregroundStyle(.green)

            // Текст
            VStack(spacing: 4) {
                Text("Добро пожаловать!")
                    .font(.title2)
                    .fontWeight(.semibold)

                Text("Аккаунт успешно создан")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }

            // Информация
            VStack(spacing: 8) {
                infoRow(label: "Имя", value: data.displayName)
                infoRow(label: "Username", value: "@\(data.username)")
                infoRow(label: "Email", value: data.email)

                if data.is2FAEnabled {
                    infoRow(label: "2FA", value: "Включена")
                }
            }
            .padding()
            .background(Color(uiColor: .secondarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: 12))

            // Кнопка
            Button {
                onComplete()
            } label: {
                Text("Начать")
            }
            .buttonStyle(.borderedProminent)
            .frame(maxWidth: .infinity)
        }
    }

    @ViewBuilder
    private func infoRow(label: String, value: String) -> some View {
        HStack {
            Text(label)
                .font(.subheadline)
                .foregroundStyle(.secondary)

            Spacer()

            Text(value)
                .font(.subheadline)
                .fontWeight(.medium)
        }
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
}
