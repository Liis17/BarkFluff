//
//  LoginView.swift
//  Barkfluff
//
//  Экран входа
//

import SwiftUI

struct LoginView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: LoginViewModel?

    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            Image(systemName: "bubble.left.and.bubble.right.fill")
                .font(.system(size: 64))
                .foregroundStyle(Color.accentColor)

            Text("Вход в BarkFluff")
                .font(.title)

            if let viewModel {
                VStack(spacing: Theme.Spacing.md) {
                    TextField("Имя пользователя или email", text: Bindable(viewModel).usernameOrEmail)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 300)
                        .disabled(viewModel.isLoading)

                    SecureField("Пароль", text: Bindable(viewModel).password)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 300)
                        .disabled(viewModel.isLoading)

                    if viewModel.needsOTP {
                        TextField("Код подтверждения (2FA)", text: Bindable(viewModel).otpCode)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 300)
                            .disabled(viewModel.isLoading)
                    }
                }

                if let errorMessage = viewModel.errorMessage {
                    ErrorBannerView(message: errorMessage)
                        .frame(width: 300)
                }

                Button(viewModel.needsOTP ? "Подтвердить" : "Войти") {
                    Task { await viewModel.login() }
                }
                .buttonStyle(.borderedProminent)
                .disabled(viewModel.usernameOrEmail.isEmpty || viewModel.password.isEmpty || viewModel.isLoading)

                Button("Создать аккаунт") {
                    viewModel.goToRegister()
                }
                .buttonStyle(.plain)

                Button("Выбрать другой сервер") {
                    coordinator.currentState = .serverSelection
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)

                if viewModel.isLoading {
                    ProgressView()
                }
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear {
            if viewModel == nil {
                viewModel = LoginViewModel(
                    authService: container.authService,
                    coordinator: coordinator
                )
            }
        }
    }
}

#Preview {
    LoginView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
