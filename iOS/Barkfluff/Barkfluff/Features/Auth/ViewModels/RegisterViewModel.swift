//
//  RegisterViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана регистрации (iOS версия)
//

import SwiftUI
import Observation
import BFCore
import BFNetworking

@Observable
final class RegisterViewModel {
    // MARK: - State

    /// Текущий шаг регистрации
    var currentStep: RegistrationStep = .personalInfo

    /// Данные регистрации
    var data: RegistrationData

    /// Состояние загрузки
    var isLoading = false

    /// Сообщение об ошибке
    var errorMessage: String?

    /// Валидность текущего шага
    var isCurrentStepValid: Bool = false

    /// Email подтверждён
    var emailCodeVerified = false

    // MARK: - Dependencies

    private let authService: AuthServiceProtocol
    private let userService: UserServiceProtocol
    private let fileService: FileServiceProtocol
    private let coordinator: AppCoordinator

    // MARK: - Init

    init(
        authService: AuthServiceProtocol,
        userService: UserServiceProtocol,
        fileService: FileServiceProtocol,
        coordinator: AppCoordinator
    ) {
        self.authService = authService
        self.userService = userService
        self.fileService = fileService
        self.coordinator = coordinator
        self.data = RegistrationData()
    }

    // MARK: - Navigation

    func goToNextStep() {
        guard let next = currentStep.next else { return }

        Task {
            if await handleStepAction(currentStep) {
                await MainActor.run {
                    withAnimation(.smooth) {
                        currentStep = next
                        errorMessage = nil
                    }
                }
            }
        }
    }

    func goToPreviousStep() {
        guard let previous = currentStep.previous else { return }
        withAnimation(.smooth) {
            currentStep = previous
            errorMessage = nil
        }
    }

    func skipCurrentStep() {
        guard currentStep.isOptional else { return }
        goToNextStep()
    }

    func completeRegistration() {
        coordinator.currentState = .main
    }

    func goToLogin() {
        coordinator.authScreen = .login
    }

    // MARK: - Step Actions

    @MainActor
    private func handleStepAction(_ step: RegistrationStep) async -> Bool {
        switch step {
        case .personalInfo:
            return handlePersonalInfoStep()

        case .username:
            return await handleUsernameStep()

        case .email:
            return await handleEmailStep()

        case .confirmEmail:
            return emailCodeVerified

        case .password:
            return await handlePasswordStep()

        case .avatar:
            return true

        case .bio:
            return await handleBioStep()

        case .twoFA:
            return true

        case .completion:
            return true
        }
    }

    private func handlePersonalInfoStep() -> Bool {
        if data.firstName.count < 2 {
            errorMessage = "Имя должно содержать минимум 2 символа"
            return false
        }
        if data.lastName.count < 2 {
            errorMessage = "Фамилия должна содержать минимум 2 символа"
            return false
        }
        return true
    }

    private func handleUsernameStep() async -> Bool {
        if data.username.count < 3 {
            errorMessage = "Минимум 3 символа"
            return false
        }

        do {
            let exists = try await userService.checkUsernameExists(username: data.username)
            if exists {
                errorMessage = "Это имя пользователя уже занято"
                return false
            }
            return true
        } catch {
            errorMessage = "Не удалось проверить имя: \(error.localizedDescription)"
            return false
        }
    }

    private func handleEmailStep() async -> Bool {
        let emailPattern = "[A-Z0-9a-z._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,64}"
        let isValid = NSPredicate(format: "SELF MATCHES %@", emailPattern).evaluate(with: data.email)

        if !isValid {
            errorMessage = "Некорректный формат email"
            return false
        }

        isLoading = true
        defer { isLoading = false }

        do {
            // Проверяем email
            let exists = try await userService.checkEmailExists(email: data.email)
            if exists {
                errorMessage = "Этот email уже зарегистрирован"
                return false
            }

            // Создаём аккаунт
            let codeID = try await authService.register(
                firstName: data.firstName,
                lastName: data.lastName,
                username: data.username,
                email: data.email
            )
            data.codeID = codeID
            return true

        } catch {
            errorMessage = "Не удалось создать аккаунт: \(error.localizedDescription)"
            return false
        }
    }

    private func handlePasswordStep() async -> Bool {
        if data.password.count < 8 {
            errorMessage = "Пароль должен содержать минимум 8 символов"
            return false
        }

        if data.password != data.confirmPassword {
            errorMessage = "Пароли не совпадают"
            return false
        }

        let result = PasswordValidator.validate(data.password)
        if !result.strength.isAcceptable {
            errorMessage = "Пароль слишком слабый"
            return false
        }

        isLoading = true
        defer { isLoading = false }

        do {
            try await authService.setPassword(password: data.password)
            return true
        } catch {
            errorMessage = "Не удалось сохранить пароль: \(error.localizedDescription)"
            return false
        }
    }

    private func handleBioStep() async -> Bool {
        guard !data.bio.isEmpty else { return true }

        isLoading = true
        defer { isLoading = false }

        do {
            try await userService.changeBio(newBio: data.bio)
            return true
        } catch {
            return true // Не критично
        }
    }

    // MARK: - Validation

    func validateCurrentStep() {
        isCurrentStepValid = data.isValid(for: currentStep)
    }

    // MARK: - Reset

    func reset() {
        currentStep = .personalInfo
        data.reset()
        isLoading = false
        errorMessage = nil
        emailCodeVerified = false
    }
}
