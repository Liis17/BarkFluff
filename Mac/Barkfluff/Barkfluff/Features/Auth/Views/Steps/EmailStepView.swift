//
//  EmailStepView.swift
//  Barkfluff
//
//  Шаг 3: Ввод email
//

import SwiftUI
import BFCore

/// Шаг регистрации: ввод email
struct EmailStepView: View {
    @Bindable var data: RegistrationData
    let userService: UserServiceProtocol

    @State private var validationError: String?
    @State private var isChecking = false
    @State private var isAvailable: Bool?
    @State private var debounceTask: Task<Void, Never>?
    @FocusState private var isFieldFocused: Bool

    var isValid: Bool {
        validationError == nil && isAvailable == true && !data.email.isEmpty
    }

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Иконка
            Image(systemName: "envelope.circle.fill")
                .font(.system(size: 60))
                .foregroundStyle(Color.accentColor)

            // Поле ввода в glass-контейнере
            VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                HStack(spacing: Theme.Spacing.sm) {
                    TextField("Email", text: $data.email)
                        .textFieldStyle(.roundedBorder)
                        .autocorrectionDisabled()
                        .focused($isFieldFocused)
                        .onChange(of: data.email) { _, newValue in
                            handleEmailChange(newValue)
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

                // Ошибка валидации
                if let error = validationError {
                    Text(error)
                        .font(.caption)
                        .foregroundStyle(.red)
                } else if isAvailable == true {
                    Text("Email доступен")
                        .font(.caption)
                        .foregroundStyle(.green)
                }
            }
        }
        .padding(Theme.Spacing.xl)
        .glassEffect(.regular, in: RoundedRectangle(cornerRadius: Theme.Radius.xl))
        .padding(Theme.Spacing.md)
        .onAppear {
            if !data.email.isEmpty {
                Task { await checkEmail(data.email) }
            }
        }
        .onDisappear {
            isFieldFocused = false
        }
    }

    private func handleEmailChange(_ newValue: String) {
        // Отменяем предыдущую задачу
        debounceTask?.cancel()

        // Сбрасываем статус
        isAvailable = nil

        // Валидируем формат
        let result = EmailValidator.validate(newValue)
        validationError = result.error

        // Если формат валиден, проверяем на сервере с debounce
        if validationError == nil && !newValue.isEmpty {
            debounceTask = Task {
                try? await Task.sleep(nanoseconds: 500_000_000) // 500ms
                guard !Task.isCancelled else { return }
                await checkEmail(newValue)
            }
        }
    }

    private func checkEmail(_ email: String) async {
        isChecking = true
        defer { isChecking = false }

        do {
            let exists = try await userService.checkEmailExists(email: email)
            isAvailable = !exists
            if exists {
                validationError = "Этот email уже зарегистрирован"
            }
        } catch {
            // Если сервер недоступен, разрешаем продолжить
            isAvailable = nil
        }
    }
}

#Preview {
    EmailStepView(data: RegistrationData(), userService: DependencyContainer().userService)
        .padding()
}
