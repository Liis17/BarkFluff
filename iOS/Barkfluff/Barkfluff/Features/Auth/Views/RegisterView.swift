//
//  RegisterView.swift
//  Barkfluff
//
//  Экран регистрации (iOS)
//

import SwiftUI
import BFCore

struct RegisterView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    @State private var viewModel: RegisterViewModel?

    var body: some View {
        ScrollView {
            VStack(spacing: 20) {
                if let viewModel {
                    // Заголовок
                    headerSection(viewModel)
                        .padding(.top, 20)

                    // Контент шага
                    stepContent(viewModel)

                    // Ошибка
                    if let error = viewModel.errorMessage {
                        Text(error)
                            .font(.subheadline)
                            .foregroundStyle(.red)
                            .padding(.horizontal, 20)
                    }

                    // Кнопки навигации
                    footerSection(viewModel)
                        .padding(.top, 8)
                        .padding(.bottom, 40)
                } else {
                    ProgressView()
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
        }
        .background(Color(uiColor: .systemGroupedBackground))
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

    // MARK: - Header

    @ViewBuilder
    private func headerSection(_ viewModel: RegisterViewModel) -> some View {
        VStack(spacing: 8) {
            // Прогресс-бар
            ProgressView(value: viewModel.currentStep.progress)
                .frame(width: 200)

            // Заголовок
            Text(viewModel.currentStep.title)
                .font(.title2)
                .fontWeight(.semibold)

            Text(viewModel.currentStep.subtitle)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .padding(.horizontal, 20)
    }

    // MARK: - Step Content

    @ViewBuilder
    private func stepContent(_ viewModel: RegisterViewModel) -> some View {
        VStack(spacing: 16) {
            switch viewModel.currentStep {
            case .personalInfo:
                PersonalInfoStepView(data: viewModel.data)
                    .onAppear { viewModel.validateCurrentStep() }
                    .onChange(of: viewModel.data.firstName) { _, _ in viewModel.validateCurrentStep() }
                    .onChange(of: viewModel.data.lastName) { _, _ in viewModel.validateCurrentStep() }

            case .username:
                UsernameStepView(data: viewModel.data, userService: container.userService)
                    .onAppear { viewModel.validateCurrentStep() }
                    .onChange(of: viewModel.data.username) { _, _ in viewModel.validateCurrentStep() }
                    .onChange(of: viewModel.data.isUsernameAvailable) { _, _ in viewModel.validateCurrentStep() }

            case .email:
                EmailStepView(data: viewModel.data, userService: container.userService)
                    .onAppear { viewModel.validateCurrentStep() }
                    .onChange(of: viewModel.data.email) { _, _ in viewModel.validateCurrentStep() }

            case .confirmEmail:
                ConfirmEmailStepView(
                    data: viewModel.data,
                    authService: container.authService,
                    onVerified: {
                        viewModel.emailCodeVerified = true
                        viewModel.validateCurrentStep()
                    }
                )

            case .password:
                PasswordStepView(data: viewModel.data)
                    .onAppear { viewModel.validateCurrentStep() }
                    .onChange(of: viewModel.data.password) { _, _ in viewModel.validateCurrentStep() }
                    .onChange(of: viewModel.data.confirmPassword) { _, _ in viewModel.validateCurrentStep() }

            case .avatar:
                AvatarStepView(
                    data: viewModel.data,
                    fileService: container.fileService,
                    userService: container.userService
                )

            case .bio:
                BioStepView(data: viewModel.data, userService: container.userService)

            case .twoFA:
                TwoFAStepView(data: viewModel.data, authService: container.authService)

            case .completion:
                CompletionStepView(data: viewModel.data) {
                    viewModel.completeRegistration()
                }
            }
        }
        .padding(.horizontal, 20)
        .animation(.default, value: viewModel.currentStep)
    }

    // MARK: - Footer

    @ViewBuilder
    private func footerSection(_ viewModel: RegisterViewModel) -> some View {
        VStack(spacing: 12) {
            // Кнопки навигации
            HStack(spacing: 12) {
                // Кнопка "Назад"
                if viewModel.currentStep != .personalInfo && viewModel.currentStep != .completion {
                    Button {
                        viewModel.goToPreviousStep()
                    } label: {
                        HStack(spacing: 4) {
                            Image(systemName: "chevron.left")
                            Text("Назад")
                        }
                    }
                    .buttonStyle(.bordered)
                }

                // Кнопка "Далее" / "Завершить"
                if viewModel.currentStep != .completion {
                    Button {
                        viewModel.goToNextStep()
                    } label: {
                        if viewModel.isLoading {
                            ProgressView()
                                .controlSize(.small)
                        } else {
                            Text(nextButtonTitle(for: viewModel.currentStep))
                        }
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(!canProceed(viewModel) || viewModel.isLoading)
                }
            }

            // Кнопка "Пропустить"
            if viewModel.currentStep.isOptional && viewModel.currentStep != .completion {
                Button("Пропустить") {
                    viewModel.skipCurrentStep()
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .font(.subheadline)
            }

            // Ссылка на логин
            if viewModel.currentStep == .personalInfo {
                Button {
                    viewModel.goToLogin()
                } label: {
                    Text("Уже есть аккаунт? ")
                        .foregroundStyle(.secondary)
                    +
                    Text("Войти")
                        .foregroundStyle(.blue)
                        .fontWeight(.medium)
                }
                .buttonStyle(.plain)
                .font(.subheadline)
            }
        }
        .padding(.horizontal, 20)
    }

    // MARK: - Helpers

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
        if viewModel.currentStep == .confirmEmail {
            return viewModel.emailCodeVerified
        }
        if viewModel.currentStep.isOptional {
            return true
        }
        return viewModel.isCurrentStepValid
    }
}

#Preview {
    NavigationStack {
        RegisterView()
            .environment(AppCoordinator())
            .environment(DependencyContainer())
    }
}
