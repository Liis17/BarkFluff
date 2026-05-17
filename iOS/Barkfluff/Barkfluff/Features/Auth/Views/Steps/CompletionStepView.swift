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
                Text("auth.register.step.completion.title")
                    .font(.title2)
                    .fontWeight(.semibold)

                Text("auth.register.step.completion.subtitle")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }

            // Информация
            VStack(spacing: 8) {
                infoRow(label: "auth.register.step.completion.row.name", value: data.displayName)
                infoRow(label: "auth.register.step.completion.row.username", value: "@\(data.username)")
                infoRow(label: "auth.register.step.completion.row.email", value: data.email)

                if data.is2FAEnabled {
                    infoRow(
                        label: "auth.register.step.completion.row.two_fa",
                        valueKey: "auth.register.step.completion.row.two_fa.enabled"
                    )
                }
            }
            .padding()
            .background(Color(uiColor: .secondarySystemGroupedBackground))
            .clipShape(RoundedRectangle(cornerRadius: 12))

            // Кнопка
            Button {
                onComplete()
            } label: {
                Text("auth.register.step.completion.start")
            }
            .buttonStyle(.borderedProminent)
            .frame(maxWidth: .infinity)
        }
    }

    @ViewBuilder
    private func infoRow(label: LocalizedStringKey, value: String) -> some View {
        HStack {
            Text(label)
                .font(.subheadline)
                .foregroundStyle(.secondary)

            Spacer()

            Text(verbatim: value)
                .font(.subheadline)
                .fontWeight(.medium)
        }
    }

    @ViewBuilder
    private func infoRow(label: LocalizedStringKey, valueKey: LocalizedStringKey) -> some View {
        HStack {
            Text(label)
                .font(.subheadline)
                .foregroundStyle(.secondary)

            Spacer()

            Text(valueKey)
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
