//
//  RegisterView.swift
//  Barkfluff
//
//  Экран регистрации (9 шагов)
//

import SwiftUI
import BFCore

struct RegisterView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: RegisterViewModel?

    var body: some View {
        VStack(spacing: 0) {
            if let viewModel {
                mainContent(viewModel)
            } else {
                ProgressView()
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(.windowBackgroundColor))
        .onAppear {
            if viewModel == nil {
                viewModel = RegisterViewModel(
                    authService: container.authService,
                    userService: container.userService,
                    fileService: container.fileService,
                    coordinator: coordinator
                )
            }
        }
    }

    // MARK: - Main Content

    @ViewBuilder
    private func mainContent(_ viewModel: RegisterViewModel) -> some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Заголовок
            headerView(viewModel)

            // Прогресс
            StepProgressIndicator(currentStep: viewModel.currentStep)

            // Контент текущего шага
            stepContent(viewModel)

            // Ошибка
            if let error = viewModel.errorMessage {
                ErrorBannerView(message: error)
                    .frame(width: 340)
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }

            // Кнопки навигации
            navigationButtons(viewModel)

            // Ссылка на логин
            loginLink(viewModel)
        }
        .padding(Theme.Spacing.xl)
        .animation(.smooth, value: viewModel.currentStep)
        .animation(.smooth, value: viewModel.errorMessage)
    }

    // MARK: - Header

    @ViewBuilder
    private func headerView(_ viewModel: RegisterViewModel) -> some View {
        VStack(spacing: Theme.Spacing.xs) {
            Text(viewModel.currentStep.title)
                .font(.title2)
                .fontWeight(.semibold)

            Text(viewModel.currentStep.subtitle)
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Step Content

    @ViewBuilder
    private func stepContent(_ viewModel: RegisterViewModel) -> some View {
        ScrollView {
            VStack {
                switch viewModel.currentStep {
                case .personalInfo:
                    PersonalInfoStepView(data: viewModel.data)
                        .onAppear { viewModel.isCurrentStepValid = viewModel.data.isValid(for: .personalInfo) }
                        .onChange(of: viewModel.data.firstName) { _, _ in viewModel.isCurrentStepValid = viewModel.data.isValid(for: .personalInfo) }
                        .onChange(of: viewModel.data.lastName) { _, _ in viewModel.isCurrentStepValid = viewModel.data.isValid(for: .personalInfo) }

                case .username:
                    UsernameStepView(
                        data: viewModel.data,
                        userService: container.userService
                    )
                    .onAppear { viewModel.isCurrentStepValid = viewModel.data.isValid(for: .username) }
                    .onChange(of: viewModel.data.username) { _, _ in viewModel.isCurrentStepValid = viewModel.data.isValid(for: .username) }
                    .onChange(of: viewModel.data.isUsernameAvailable) { _, _ in viewModel.isCurrentStepValid = viewModel.data.isValid(for: .username) }

                case .email:
                    EmailStepView(
                        data: viewModel.data,
                        userService: container.userService
                    )
                    .onAppear { viewModel.isCurrentStepValid = viewModel.data.isValid(for: .email) }
                    .onChange(of: viewModel.data.email) { _, _ in viewModel.isCurrentStepValid = viewModel.data.isValid(for: .email) }

                case .confirmEmail:
                    ConfirmEmailStepView(
                        data: viewModel.data,
                        authService: container.authService,
                        onVerified: { viewModel.emailCodeVerified = true }
                    )
                    .onAppear { viewModel.isCurrentStepValid = false }

                case .password:
                    PasswordStepView(
                        data: viewModel.data,
                        authService: container.authService
                    )
                    .onAppear { viewModel.isCurrentStepValid = viewModel.data.isValid(for: .password) }
                    .onChange(of: viewModel.data.password) { _, _ in viewModel.isCurrentStepValid = viewModel.data.isValid(for: .password) }
                    .onChange(of: viewModel.data.confirmPassword) { _, _ in viewModel.isCurrentStepValid = viewModel.data.isValid(for: .password) }

                case .avatar:
                    AvatarStepView(
                        data: viewModel.data,
                        fileService: container.fileService,
                        userService: container.userService
                    )

                case .bio:
                    BioStepView(
                        data: viewModel.data,
                        userService: container.userService
                    )

                case .twoFA:
                    TwoFAStepView(
                        data: viewModel.data,
                        authService: container.authService
                    )

                case .completion:
                    CompletionStepView(
                        data: viewModel.data,
                        onComplete: { viewModel.completeRegistration() }
                    )
                }
            }
            .frame(maxWidth: 450) // Ограничение ширины контента
            .frame(minHeight: 350)
            .padding(.horizontal, Theme.Spacing.xl) // Дополнительный отступ для теней
        }
        .frame(maxHeight: 450)
    }

    // MARK: - Navigation Buttons

    @ViewBuilder
    private func navigationButtons(_ viewModel: RegisterViewModel) -> some View {
        HStack(spacing: Theme.Spacing.md) {
            // Кнопка "Назад"
            if viewModel.currentStep != .personalInfo && viewModel.currentStep != .completion {
                Button(action: { viewModel.goToPreviousStep() }) {
                    Label("Назад", systemImage: "chevron.left")
                }
                .buttonStyle(.bordered)
            }

            Spacer()

            // Кнопка "Пропустить" (для опциональных шагов)
            if viewModel.currentStep.isOptional && viewModel.currentStep != .completion {
                Button("Пропустить") {
                    viewModel.skipCurrentStep()
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
            }

            // Кнопка "Далее" или "Завершить"
            if viewModel.currentStep != .completion {
                Button(action: { viewModel.goToNextStep() }) {
                    HStack(spacing: Theme.Spacing.xs) {
                        if viewModel.isLoading {
                            ProgressView()
                                .controlSize(.small)
                                .tint(.white)
                        } else {
                            Text(nextButtonTitle(for: viewModel.currentStep))
                        }

                        Image(systemName: "chevron.right")
                    }
                }
                .buttonStyle(.borderedProminent)
                .disabled(!canProceed(viewModel) || viewModel.isLoading)
            }
        }
        .frame(width: 340)
    }

    private func nextButtonTitle(for step: RegistrationStep) -> String {
        switch step {
        case .twoFA:
            return "Завершить"
        case .confirmEmail:
            return "Подтвердить"
        default:
            return "Далее"
        }
    }

    private func canProceed(_ viewModel: RegisterViewModel) -> Bool {
        // Для подтверждения email кнопка активируется при успешной верификации
        if viewModel.currentStep == .confirmEmail {
            return viewModel.emailCodeVerified
        }

        // Для опциональных шагов кнопка всегда активна
        if viewModel.currentStep.isOptional {
            return true
        }

        // Для остальных — по валидации
        return viewModel.isCurrentStepValid
    }

    // MARK: - Login Link

    @ViewBuilder
    private func loginLink(_ viewModel: RegisterViewModel) -> some View {
        if viewModel.currentStep == .personalInfo {
            Button("Уже есть аккаунт? Войти") {
                viewModel.goToLogin()
            }
            .buttonStyle(.plain)
            .foregroundStyle(.secondary)
        }
    }
}

// MARK: - Preview

#Preview {
    RegisterView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
        .frame(width: 500, height: 700)
}
