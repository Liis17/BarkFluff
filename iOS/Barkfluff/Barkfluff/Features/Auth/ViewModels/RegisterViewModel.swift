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
    var errorMessage: LocalizedStringResource?

    /// Валидность текущего шага
    var isCurrentStepValid: Bool = false

    /// Email подтверждён
    var emailCodeVerified = false

    /// Пароль был отправлен на сервер (для индикации в UI)
    var passwordSaved = false

    /// Bio было сохранено на сервере (для индикации в UI)
    var bioSaved = false

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
        coordinator.isConnectionReady = true
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
        let firstResult = NameValidator.validateFirstName(data.firstName)
        if !firstResult.isValid {
            errorMessage = firstResult.error
            return false
        }
        let lastResult = NameValidator.validateLastName(data.lastName)
        if !lastResult.isValid {
            errorMessage = lastResult.error
            return false
        }
        return true
    }

    private func handleUsernameStep() async -> Bool {
        let format = UsernameValidator.validate(data.username)
        if !format.isValid {
            errorMessage = format.error
            return false
        }

        do {
            let exists = try await userService.checkUsernameExists(username: data.username)
            if exists {
                errorMessage = UsernameValidationResult.unavailable.error
                return false
            }
            return true
        } catch {
            errorMessage = LocalizedStringResource("auth.errors.username_check_failed \(error.localizedDescription)")
            return false
        }
    }

    private func handleEmailStep() async -> Bool {
        let format = EmailValidator.validate(data.email)
        if !format.isValid {
            errorMessage = format.error
            return false
        }

        isLoading = true
        defer { isLoading = false }

        do {
            // Проверяем email
            let exists = try await userService.checkEmailExists(email: data.email)
            if exists {
                errorMessage = LocalizedStringResource("auth.register.step.email.taken")
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
            errorMessage = LocalizedStringResource("auth.errors.register_failed \(error.localizedDescription)")
            return false
        }
    }

    private func handlePasswordStep() async -> Bool {
        let result = PasswordValidator.validate(data.password)
        if !result.isValid {
            errorMessage = result.error
            return false
        }

        if data.password != data.confirmPassword {
            errorMessage = LocalizedStringResource("auth.validation.password.mismatch")
            return false
        }

        if !result.strength.isAcceptable {
            errorMessage = LocalizedStringResource("auth.validation.password.too_weak_short")
            return false
        }

        isLoading = true
        defer { isLoading = false }

        do {
            try await authService.setPassword(password: data.password)
            passwordSaved = true
            return true
        } catch {
            errorMessage = LocalizedStringResource("auth.errors.password_save_failed \(error.localizedDescription)")
            return false
        }
    }

    private func handleBioStep() async -> Bool {
        guard !data.bio.isEmpty else { return true }

        isLoading = true
        defer { isLoading = false }

        do {
            try await userService.changeBio(newBio: data.bio)
            bioSaved = true
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
