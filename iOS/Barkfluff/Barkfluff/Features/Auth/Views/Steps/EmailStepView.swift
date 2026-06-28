//
//  EmailStepView.swift
//  Barkfluff
//
//  Шаг 3: Ввод email (iOS)
//

import SwiftUI
import BFCore

struct EmailStepView: View {
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
                Text("auth.register.step.email.label")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                HStack(spacing: 8) {
                    TextField("auth.register.step.email.placeholder", text: $data.email)
                        .textFieldStyle(.roundedBorder)
                        .textContentType(.emailAddress)
                        .keyboardType(.emailAddress)
                        .autocapitalization(.none)
                        .autocorrectionDisabled()
                        .focused($isFieldFocused)
                        .onChange(of: data.email) { _, newValue in
                            handleEmailChange(newValue)
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
                Text("auth.register.step.email.available")
                    .font(.caption)
                    .foregroundStyle(.green)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .onAppear {
            isFieldFocused = true
            if !data.email.isEmpty {
                Task { await checkEmail(data.email) }
            }
        }
    }

    // MARK: - Actions

    private func handleEmailChange(_ newValue: String) {
        debounceTask?.cancel()
        isAvailable = nil

        if newValue.isEmpty {
            validationError = nil
            return
        }

        let emailPattern = "[A-Z0-9a-z._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,64}"
        let isValid = NSPredicate(format: "SELF MATCHES %@", emailPattern).evaluate(with: newValue)

        if !isValid {
            validationError = LocalizedStringResource("auth.validation.email.invalid_format")
            return
        }

        validationError = nil

        debounceTask = Task {
            try? await Task.sleep(nanoseconds: 500_000_000)
            guard !Task.isCancelled else { return }
            await checkEmail(newValue)
        }
    }

    private func checkEmail(_ email: String) async {
        isChecking = true

        do {
            let exists = try await userService.checkEmailExists(email: email)
            isAvailable = !exists
            if exists {
                validationError = LocalizedStringResource("auth.register.step.email.taken")
            }
        } catch {
            isAvailable = nil
        }

        isChecking = false
    }
}

#Preview {
    EmailStepView(data: RegistrationData(), userService: DependencyContainer().userService)
        .padding()
}
