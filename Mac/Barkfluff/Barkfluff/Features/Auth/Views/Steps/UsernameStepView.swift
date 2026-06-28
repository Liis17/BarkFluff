//
//  UsernameStepView.swift
//  Barkfluff
//
//  Шаг 2: Выбор имени пользователя с проверкой на сервере
//

import SwiftUI
import BFCore

/// Шаг регистрации: выбор username
struct UsernameStepView: View {
    @Bindable var data: RegistrationData
    let userService: UserServiceProtocol

    @State private var validationError: LocalizedStringResource?
    @State private var isChecking = false
    @State private var isAvailable: Bool?
    @State private var debounceTask: Task<Void, Never>?
    @FocusState private var isFieldFocused: Bool

    var isValid: Bool {
        validationError == nil && isAvailable == true && !data.username.isEmpty
    }

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Иконка
            Image(systemName: "at.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            // Поле ввода в glass-контейнере
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                HStack(spacing: Theme.Spacing.sm) {
                    TextField("auth.register.step.username.label", text: $data.username)
                        .textFieldStyle(.roundedBorder)
                        .autocorrectionDisabled()
                        .focused($isFieldFocused)
                        .onChange(of: data.username) { _, newValue in
                            handleUsernameChange(newValue)
                        }

                    // Индикатор статуса с фиксированной шириной (без layout shift)
                    ZStack {
                        if isChecking {
                            ProgressView()
                                .scaleEffect(0.8)
                        } else if let available = isAvailable {
                            Image(systemName: available ? "checkmark.circle.fill" : "xmark.circle.fill")
                                .foregroundStyle(available ? .green : .red)
                        }
                    }
                    .frame(width: 28)
                }
                .frame(maxWidth: 320)

                // Ошибка валидации или сообщение о доступности
                if let error = validationError {
                    Text(error)
                        .font(.caption)
                        .foregroundStyle(.red)
                } else if isAvailable == true {
                    Text("auth.register.step.username.available")
                        .font(.caption)
                        .foregroundStyle(.green)
                }

                // Подсказка
                Text("auth.register.step.username.hint")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(Theme.Spacing.xl)
        .glassEffect(.regular, in: RoundedRectangle(cornerRadius: Theme.Radius.xl))
        .padding(Theme.Spacing.md) // Отступ для тени glass
        .onAppear {
            if !data.username.isEmpty {
                Task { await checkUsername(data.username) }
            }
        }
        .onDisappear {
            isFieldFocused = false
        }
    }

    private func handleUsernameChange(_ newValue: String) {
        // Отменяем предыдущую задачу
        debounceTask?.cancel()

        // Сбрасываем статус
        isAvailable = nil
        data.isUsernameAvailable = nil

        // Валидируем формат
        let result = UsernameValidator.validate(newValue)
        validationError = result.error

        // Если формат валиден, проверяем на сервере с debounce
        if validationError == nil && !newValue.isEmpty {
            debounceTask = Task {
                try? await Task.sleep(nanoseconds: 500_000_000) // 500ms
                guard !Task.isCancelled else { return }
                await checkUsername(newValue)
            }
        }
    }

    private func checkUsername(_ username: String) async {
        isChecking = true
        defer { isChecking = false }

        do {
            let exists = try await userService.checkUsernameExists(username: username)
            isAvailable = !exists
            data.isUsernameAvailable = !exists
            if exists {
                validationError = LocalizedStringResource("auth.register.step.username.taken")
            }
        } catch {
            // Если сервер недоступен, разрешаем продолжить
            // (проверка будет на сервере при регистрации)
            print("⚠️ Username check failed: \(error.localizedDescription)")
            isAvailable = true // Разрешаем продолжить при ошибке сети
            data.isUsernameAvailable = true
        }
    }
}

#Preview {
    UsernameStepView(data: RegistrationData(), userService: DependencyContainer().userService)
        .padding()
}
