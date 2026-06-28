//
//  UsernameStepView.swift
//  Barkfluff
//
//  Шаг 2: Выбор имени пользователя (iOS)
//

import SwiftUI
import BFCore

struct UsernameStepView: View {
    @Bindable var data: RegistrationData
    let userService: UserServiceProtocol

    @State private var validationError: LocalizedStringResource?
    @State private var isChecking = false
    @State private var isAvailable: Bool?
    @State private var debounceTask: Task<Void, Never>?
    @FocusState private var isFieldFocused: Bool

    var body: some View {
        VStack(spacing: 12) {
            // Поле ввода
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.register.step.username.label")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                HStack(spacing: 8) {
                    TextField("auth.register.step.username.placeholder", text: $data.username)
                        .textFieldStyle(.roundedBorder)
                        .textContentType(.username)
                        .autocapitalization(.none)
                        .autocorrectionDisabled()
                        .focused($isFieldFocused)
                        .onChange(of: data.username) { _, newValue in
                            handleUsernameChange(newValue)
                        }

                    // Индикатор статуса
                    ZStack {
                        if isChecking {
                            ProgressView()
                                .controlSize(.small)
                        } else if let available = isAvailable {
                            Image(systemName: available ? "checkmark.circle.fill" : "xmark.circle.fill")
                                .foregroundStyle(available ? .green : .red)
                        }
                    }
                    .frame(width: 24)
                }
            }

            // Ошибка или сообщение о доступности
            if let error = validationError {
                Text(error)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .frame(maxWidth: .infinity, alignment: .leading)
            } else if isAvailable == true {
                Text("auth.register.step.username.available")
                    .font(.caption)
                    .foregroundStyle(.green)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            // Подсказка
            VStack(alignment: .leading, spacing: 4) {
                requirementRow("auth.register.step.username.req.length", isMet: data.username.count >= 3 && data.username.count <= 30)
                requirementRow("auth.register.step.username.req.chars", isMet: isValidFormat)
                requirementRow("auth.register.step.username.req.no_digit_start", isMet: !data.username.isEmpty && !data.username.first!.isNumber)
            }
            .font(.caption)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .onAppear {
            isFieldFocused = true
            if !data.username.isEmpty {
                Task { await checkUsername(data.username) }
            }
        }
    }

    // MARK: - Requirement Row

    @ViewBuilder
    private func requirementRow(_ key: LocalizedStringKey, isMet: Bool) -> some View {
        HStack(spacing: 4) {
            Image(systemName: isMet ? "checkmark.circle.fill" : "circle")
                .font(.system(size: 12))
            Text(key)
        }
        .foregroundStyle(isMet ? .green : .secondary)
    }

    // MARK: - Computed

    private var isValidFormat: Bool {
        let pattern = "^[a-zA-Z0-9_\\-]+$"
        return NSPredicate(format: "SELF MATCHES %@", pattern).evaluate(with: data.username)
    }

    // MARK: - Actions

    private func handleUsernameChange(_ newValue: String) {
        debounceTask?.cancel()
        isAvailable = nil
        data.isUsernameAvailable = nil

        if newValue.isEmpty {
            validationError = nil
            return
        }

        if newValue.count < 3 {
            validationError = LocalizedStringResource("auth.validation.username.too_short \(3)")
            return
        }

        if newValue.count > 30 {
            validationError = LocalizedStringResource("auth.validation.username.too_long \(30)")
            return
        }

        if !isValidFormat {
            validationError = LocalizedStringResource("auth.validation.username.invalid_chars")
            return
        }

        if newValue.first?.isNumber == true {
            validationError = LocalizedStringResource("auth.validation.username.starts_with_digit")
            return
        }

        validationError = nil

        debounceTask = Task {
            try? await Task.sleep(nanoseconds: 500_000_000)
            guard !Task.isCancelled else { return }
            await checkUsername(newValue)
        }
    }

    private func checkUsername(_ username: String) async {
        isChecking = true

        do {
            let exists = try await userService.checkUsernameExists(username: username)
            isAvailable = !exists
            data.isUsernameAvailable = !exists
            if exists {
                validationError = LocalizedStringResource("auth.register.step.username.taken")
            }
        } catch {
            isAvailable = true
            data.isUsernameAvailable = true
        }

        isChecking = false
    }
}

#Preview {
    UsernameStepView(data: RegistrationData(), userService: DependencyContainer().userService)
        .padding()
}
