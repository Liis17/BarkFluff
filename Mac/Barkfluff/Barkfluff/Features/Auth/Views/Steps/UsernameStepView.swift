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

    @State private var validationError: String?
    @State private var isChecking = false
    @State private var isAvailable: Bool?
    @State private var debounceTask: Task<Void, Never>?

    var isValid: Bool {
        validationError == nil && isAvailable == true && !data.username.isEmpty
    }

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Иконка
            Image(systemName: "at.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            // Поле ввода
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                HStack {
                    TextField("Имя пользователя", text: $data.username)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 300)
                        .autocorrectionDisabled()
                        .onChange(of: data.username) { _, newValue in
                            handleUsernameChange(newValue)
                        }

                    // Индикатор статуса
                    if isChecking {
                        ProgressView()
                            .scaleEffect(0.8)
                    } else if let available = isAvailable {
                        Image(systemName: available ? "checkmark.circle.fill" : "xmark.circle.fill")
                            .foregroundStyle(available ? .green : .red)
                    }
                }

                // Ошибка валидации
                if let error = validationError {
                    Text(error)
                        .font(.caption)
                        .foregroundStyle(.red)
                } else if isAvailable == true {
                    Text("Имя доступно")
                        .font(.caption)
                        .foregroundStyle(.green)
                }

                // Подсказка
                Text("3-30 символов: буквы, цифры, _ и -")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .onAppear {
            if !data.username.isEmpty {
                Task { await checkUsername(data.username) }
            }
        }
    }

    private func handleUsernameChange(_ newValue: String) {
        // Отменяем предыдущую задачу
        debounceTask?.cancel()

        // Сбрасываем статус
        isAvailable = nil

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
            if exists {
                validationError = "Это имя уже занято"
            }
        } catch {
            // Если сервер недоступен, разрешаем продолжить
            // (проверка будет на сервере при регистрации)
            isAvailable = nil
        }
    }
}

#Preview {
    UsernameStepView(data: RegistrationData(), userService: DependencyContainer().userService)
        .padding()
}
