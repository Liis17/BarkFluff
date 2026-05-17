//
//  LoginView.swift
//  Barkfluff
//
//  Экран входа (iOS)
//

import SwiftUI

struct LoginView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: LoginViewModel?

    var body: some View {
        ScrollView {
            VStack(spacing: 24) {
                // Логотип
                logoSection
                    .padding(.top, 60)

                // Форма
                if let viewModel {
                    formSection(viewModel)
                }

                Spacer()
            }
        }
        .background(Color(uiColor: .systemGroupedBackground))
        .onAppear {
            if viewModel == nil {
                viewModel = LoginViewModel(
                    authService: container.authService,
                    coordinator: coordinator
                )
            }
        }
    }

    // MARK: - Logo

    private var logoSection: some View {
        VStack(spacing: 12) {
            Image(systemName: "bubble.left.and.bubble.right.fill")
                .font(.system(size: 44, weight: .medium))
                .foregroundStyle(.blue)

            Text("BarkFluff")
                .font(.system(size: 24, weight: .bold, design: .rounded))
        }
    }

    // MARK: - Form

    @ViewBuilder
    private func formSection(_ viewModel: LoginViewModel) -> some View {
        VStack(spacing: 16) {
            // Логин
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.login.username_or_email")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                TextField("", text: Bindable(viewModel).usernameOrEmail)
                    .textFieldStyle(.roundedBorder)
                    .textContentType(.username)
                    .autocapitalization(.none)
                    .autocorrectionDisabled()
                    .disabled(viewModel.isLoading)
            }

            // Пароль
            VStack(alignment: .leading, spacing: 4) {
                Text("auth.login.password")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)

                SecureField("", text: Bindable(viewModel).password)
                    .textFieldStyle(.roundedBorder)
                    .textContentType(.password)
                    .disabled(viewModel.isLoading)
            }

            // OTP (если нужен)
            if viewModel.needsOTP {
                VStack(alignment: .leading, spacing: 4) {
                    Text("auth.login.otp")
                        .font(.subheadline)
                        .foregroundStyle(.secondary)

                    TextField("auth.login.otp_placeholder", text: Bindable(viewModel).otpCode)
                        .textFieldStyle(.roundedBorder)
                        .textContentType(.oneTimeCode)
                        .keyboardType(.numberPad)
                        .disabled(viewModel.isLoading)
                }
                .transition(.opacity)
            }

            // Ошибка
            if let errorMessage = viewModel.errorMessage {
                Text(errorMessage)
                    .font(.subheadline)
                    .foregroundStyle(.red)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            // Кнопка входа
            Button {
                Task { await viewModel.login() }
            } label: {
                if viewModel.isLoading {
                    ProgressView()
                        .controlSize(.small)
                } else {
                    Text(viewModel.needsOTP ? "auth.login.submit_otp" : "auth.login.submit")
                }
            }
            .buttonStyle(.borderedProminent)
            .disabled(viewModel.usernameOrEmail.isEmpty || viewModel.password.isEmpty || viewModel.isLoading)
            .frame(maxWidth: .infinity)

            // Дополнительные действия
            VStack(spacing: 8) {
                Button {
                    viewModel.goToRegister()
                } label: {
                    Text("auth.login.create_account")
                        .fontWeight(.medium)
                }
                .buttonStyle(.bordered)

                Button {
                    coordinator.currentState = .serverSelection
                } label: {
                    HStack(spacing: 4) {
                        Image(systemName: "server.rack")
                        Text("auth.login.choose_server")
                    }
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .font(.subheadline)
            }
            .padding(.top, 8)
        }
        .padding(.horizontal, 20)
        .padding(.bottom, 40)
    }
}

#Preview {
    LoginView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
