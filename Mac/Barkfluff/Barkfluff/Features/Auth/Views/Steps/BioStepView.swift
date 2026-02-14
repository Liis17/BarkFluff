//
//  BioStepView.swift
//  Barkfluff
//
//  Шаг 7: Описание профиля (опционально)
//

import SwiftUI
import BFCore

/// Шаг регистрации: био пользователя
struct BioStepView: View {
    @Bindable var data: RegistrationData
    let userService: UserServiceProtocol

    @State private var isSaving = false
    @State private var saveError: String?

    let maxBioLength = 500

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Иконка
            Image(systemName: "text.bubble.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            // Текстовое поле
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                TextEditor(text: $data.bio)
                    .frame(width: 300, height: 150)
                    .padding(4)
                    .background(Color(.textBackgroundColor))
                    .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md))
                    .overlay(
                        RoundedRectangle(cornerRadius: Theme.Radius.md)
                            .strokeBorder(Color.gray.opacity(0.3), lineWidth: 1)
                    )

                HStack {
                    Text("Расскажите о себе")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    Spacer()

                    Text("\(data.bio.count)/\(maxBioLength)")
                        .font(.caption)
                        .foregroundStyle(data.bio.count > maxBioLength ? .red : .secondary)
                }
            }

            // Ошибка
            if let error = saveError {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
            }

            // Подсказка
            Text("Не обязательно — вы можете заполнить это позже в настройках профиля")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .onChange(of: data.bio) { _, newValue in
            if newValue.count > maxBioLength {
                data.bio = String(newValue.prefix(maxBioLength))
            }
        }
    }

    /// Сохраняет био на сервере
    func saveBio() async -> Bool {
        guard !data.bio.isEmpty else { return true } // Пустое био — OK

        isSaving = true
        saveError = nil
        defer { isSaving = false }

        do {
            try await userService.changeBio(newBio: data.bio)
            return true
        } catch {
            saveError = error.localizedDescription
            return false
        }
    }
}

#Preview {
    BioStepView(data: RegistrationData(), userService: DependencyContainer().userService)
        .padding()
}
