//
//  RegisterViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана регистрации (9 шагов)
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

    /// Валидность текущего шага (для UI)
    var isCurrentStepValid: Bool = false

    // MARK: - Dependencies

    private let authService: AuthServiceProtocol
    private let userService: UserServiceProtocol
    private let fileService: FileServiceProtocol
    private let coordinator: AppCoordinator

    // MARK: - Step View States

    /// Состояние шага подтверждения email
    var emailCodeVerified = false

    /// Состояние шага пароля
    var passwordSaved = false

    /// Состояние шага био
    var bioSaved = false

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

    /// Перейти к следующему шагу
    func goToNextStep() {
        guard let next = currentStep.next else { return }

        // Асинхронные действия перед переходом
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

    /// Перейти к предыдущему шагу
    func goToPreviousStep() {
        guard let previous = currentStep.previous else { return }
        withAnimation(.smooth) {
            currentStep = previous
            errorMessage = nil
        }
    }

    /// Пропустить текущий шаг (только для опциональных)
    func skipCurrentStep() {
        guard currentStep.isOptional else { return }
        goToNextStep()
    }

    /// Завершить регистрацию
    func completeRegistration() {
        coordinator.currentState = .main
    }

    /// Перейти к логину
    func goToLogin() {
        coordinator.authScreen = .login
    }

    // MARK: - Step Actions

    /// Выполнить действие для шага (возвращает true если можно переходить дальше)
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
            return true // Аватар загружается сразу при выборе

        case .bio:
            return await handleBioStep()

        case .twoFA:
            return true // 2FA обрабатывается внутри шага

        case .completion:
            return true
        }
    }

    private func handlePersonalInfoStep() -> Bool {
        let firstNameResult = NameValidator.validateFirstName(data.firstName)
        let lastNameResult = NameValidator.validateLastName(data.lastName)

        if let error = firstNameResult.error {
            errorMessage = error
            return false
        }

        if let error = lastNameResult.error {
            errorMessage = error
            return false
        }

        return true
    }

    private func handleUsernameStep() async -> Bool {
        let result = UsernameValidator.validate(data.username)
        if let error = result.error {
            errorMessage = error
            return false
        }

        // Проверяем доступность на сервере
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
        let result = EmailValidator.validate(data.email)
        if let error = result.error {
            errorMessage = error
            return false
        }

        // Проверяем доступность на сервере
        do {
            let exists = try await userService.checkEmailExists(email: data.email)
            if exists {
                errorMessage = "Этот email уже зарегистрирован"
                return false
            }
        } catch {
            // Игнорируем ошибку проверки — сервер проверит при создании
        }

        // Создаём аккаунт на сервере
        isLoading = true
        defer { isLoading = false }

        do {
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
        let result = PasswordValidator.validateConfirmation(data.password, confirmation: data.confirmPassword)
        if let error = result.error {
            errorMessage = error
            return false
        }

        if !result.strength.isAcceptable {
            errorMessage = "Пароль слишком слабый"
            return false
        }

        // Сохраняем пароль на сервере
        isLoading = true
        defer { isLoading = false }

        do {
            try await authService.setPassword(password: data.password)
            passwordSaved = true
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
            bioSaved = true
            return true
        } catch {
            // Не критично — можно продолжить
            return true
        }
    }

    // MARK: - Validation

    /// Проверить валидность текущего шага
    func validateCurrentStep() {
        isCurrentStepValid = data.isValid(for: currentStep)
    }

    // MARK: - Reset

    /// Сбросить состояние регистрации
    func reset() {
        currentStep = .personalInfo
        data.reset()
        isLoading = false
        errorMessage = nil
        emailCodeVerified = false
        passwordSaved = false
        bioSaved = false
    }
}
