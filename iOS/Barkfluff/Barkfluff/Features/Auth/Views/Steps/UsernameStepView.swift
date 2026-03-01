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

    @State private var validationError: String?
    @State private var isChecking = false
    @State private var isAvailable: Bool?
    @State private var debounceTask: Task<Void, Never>?
    @FocusState private var isFieldFocused: Bool

    var body: some View {
        VStack(spacing: 12) {
            // Поле ввода
            VStack(alignment: .leading, spacing: 4) {
                Text("Имя пользователя")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                HStack(spacing: 8) {
                    TextField("@username", text: $data.username)
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
                Text("Имя доступно")
                    .font(.caption)
                    .foregroundStyle(.green)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            // Подсказка
            VStack(alignment: .leading, spacing: 4) {
                requirementRow("3-30 символов", isMet: data.username.count >= 3 && data.username.count <= 30)
                requirementRow("Буквы, цифры, _ и -", isMet: isValidFormat)
                requirementRow("Не начинается с цифры", isMet: !data.username.isEmpty && !data.username.first!.isNumber)
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
    private func requirementRow(_ text: String, isMet: Bool) -> some View {
        HStack(spacing: 4) {
            Image(systemName: isMet ? "checkmark.circle.fill" : "circle")
                .font(.system(size: 12))
            Text(text)
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
            validationError = "Минимум 3 символа"
            return
        }

        if newValue.count > 30 {
            validationError = "Максимум 30 символов"
            return
        }

        if !isValidFormat {
            validationError = "Только буквы, цифры, _ и -"
            return
        }

        if newValue.first?.isNumber == true {
            validationError = "Не должен начинаться с цифры"
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
                validationError = "Это имя уже занято"
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
