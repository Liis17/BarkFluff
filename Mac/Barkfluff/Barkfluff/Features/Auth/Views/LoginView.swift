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
    @State private var fastAuthViewModel: FastAuthViewModel?
    @State private var appeared = false

    var body: some View {
        ZStack {
            loginBackground

            VStack(spacing: Theme.Spacing.xxl) {
                logoSection
                if let viewModel {
                    HStack(alignment: .center, spacing: Theme.Spacing.xxl) {
                        formCard(viewModel)
                        if let fastAuthViewModel {
                            QRPanelView(viewModel: fastAuthViewModel)
                                .opacity(appeared ? 1 : 0)
                                .offset(y: appeared ? 0 : 12)
                                .animation(.easeOut(duration: 0.4).delay(0.25), value: appeared)
                        }
                    }
                }
            }
            .padding(Theme.Spacing.xxl)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear {
            if viewModel == nil {
                viewModel = LoginViewModel(
                    authService: container.authService,
                    coordinator: coordinator
                )
            }
            if fastAuthViewModel == nil {
                let vm = FastAuthViewModel(
                    fastAuthService: container.fastAuthService,
                    authService: container.authService
                )
                let coordinatorRef = coordinator
                let containerRef = container
                vm.onAuthenticated = {
                    Task { @MainActor in
                        await containerRef.loadCurrentUser()
                        coordinatorRef.isConnectionReady = true
                        coordinatorRef.currentState = .main
                    }
                }
                fastAuthViewModel = vm
            }
            appeared = false
            Task {
                try? await Task.sleep(for: .milliseconds(40))
                appeared = true
            }
        }
    }

    // MARK: - Background

    private var loginBackground: some View {
        LinearGradient(
            colors: [
                Color.accentColor.opacity(0.18),
                Color.accentColor.opacity(0.05),
                Color(nsColor: .windowBackgroundColor)
            ],
            startPoint: .topLeading,
            endPoint: .bottomTrailing
        )
        .ignoresSafeArea()
    }

    // MARK: - Logo

    private var logoSection: some View {
        VStack(spacing: Theme.Spacing.md) {
            ZStack {
                Circle()
                    .fill(Color.accentColor.opacity(0.12))
                    .frame(width: 96, height: 96)
                Circle()
                    .fill(Color.accentColor.opacity(0.07))
                    .frame(width: 76, height: 76)
                Image(systemName: "bubble.left.and.bubble.right.fill")
                    .font(.system(size: 38, weight: .medium))
                    .foregroundStyle(Color.accentColor)
                    .symbolRenderingMode(.hierarchical)
            }
            .scaleEffect(appeared ? 1 : 0.7)
            .opacity(appeared ? 1 : 0)
            .animation(.spring(response: 0.5, dampingFraction: 0.7), value: appeared)

            VStack(spacing: 3) {
                Text(verbatim: "BarkFluff")
                    .font(.title.bold())
                Text("auth.login.subtitle")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .opacity(appeared ? 1 : 0)
            .offset(y: appeared ? 0 : 6)
            .animation(.easeOut(duration: 0.4).delay(0.1), value: appeared)
        }
    }

    // MARK: - Form Card

    @ViewBuilder
    private func formCard(_ viewModel: LoginViewModel) -> some View {
        VStack(spacing: Theme.Spacing.lg) {
            // Поля ввода
            VStack(spacing: Theme.Spacing.sm) {
                TextField("auth.login.username_or_email", text: Bindable(viewModel).usernameOrEmail)
                    .textFieldStyle(.roundedBorder)
                    .controlSize(.large)
                    .disabled(viewModel.isLoading)

                SecureField("auth.login.password", text: Bindable(viewModel).password)
                    .textFieldStyle(.roundedBorder)
                    .controlSize(.large)
                    .disabled(viewModel.isLoading)

                if viewModel.needsOTP {
                    TextField("auth.login.otp", text: Bindable(viewModel).otpCode)
                        .textFieldStyle(.roundedBorder)
                        .controlSize(.large)
                        .disabled(viewModel.isLoading)
                        .transition(.move(edge: .top).combined(with: .opacity))
                }
            }

            if let errorMessage = viewModel.errorMessage {
                ErrorBannerView(message: errorMessage)
                    .transition(.move(edge: .top).combined(with: .opacity))
            }

            // Кнопка входа — показывается только когда оба поля заполнены
            let canAttemptLogin = !viewModel.usernameOrEmail.isEmpty && !viewModel.password.isEmpty
            if canAttemptLogin || viewModel.isLoading {
                ZStack {
                    Button(viewModel.needsOTP ? "auth.login.submit_otp" : "auth.login.submit") {
                        Task { await viewModel.login() }
                    }
                    .buttonStyle(.glassProminent)
                    .controlSize(.large)
                    .frame(maxWidth: .infinity)
                    .disabled(viewModel.isLoading)
                    .opacity(viewModel.isLoading ? 0 : 1)

                    if viewModel.isLoading {
                        ProgressView()
                            .progressViewStyle(.circular)
                            .controlSize(.regular)
                    }
                }
                .frame(height: 36)
                .transition(.move(edge: .bottom).combined(with: .opacity))
            }

            Divider()

            // Нижние действия
            HStack {
                Button("auth.login.create_account") { viewModel.goToRegister() }
                    .buttonStyle(.plain)
                    .foregroundStyle(Color.accentColor)
                    .font(.callout.weight(.medium))

                Spacer()

                Button("auth.login.choose_server") { coordinator.currentState = .serverSelection }
                    .buttonStyle(.plain)
                    .foregroundStyle(Color.accentColor.opacity(0.7))
                    .font(.callout)
            }
        }
        .padding(Theme.Spacing.xxl)
        .animation(.easeInOut(duration: 0.22), value: viewModel.usernameOrEmail.isEmpty || viewModel.password.isEmpty)
        .background(
            RoundedRectangle(cornerRadius: 18)
                .fill(.regularMaterial)
                .shadow(color: .black.opacity(0.08), radius: 24, x: 0, y: 8)
                .shadow(color: .black.opacity(0.04), radius: 4, x: 0, y: 2)
        )
        .frame(width: 360)
        .opacity(appeared ? 1 : 0)
        .offset(y: appeared ? 0 : 12)
        .animation(.easeOut(duration: 0.4).delay(0.2), value: appeared)
    }
}

#Preview {
    LoginView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
