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
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    @State private var viewModel: LoginViewModel?
    @State private var fastAuthViewModel: FastAuthViewModel?
    @State private var appeared = false

    private enum Metrics {
        static let contentWidth: CGFloat = 760
        static let panelPadding: CGFloat = 28
        static let columnSpacing: CGFloat = 28
        static let formWidth: CGFloat = 320
        static let qrWidth: CGFloat = 280
    }

    var body: some View {
        ZStack {
            loginBackground

            ScrollView {
                VStack(spacing: Theme.Spacing.xl) {
                    logoSection

                    if let viewModel {
                        authPanel(viewModel)
                    }
                }
                .frame(maxWidth: Metrics.contentWidth)
                .frame(maxWidth: .infinity)
                .padding(.horizontal, Theme.Spacing.xxl)
                .padding(.vertical, 36)
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

            guard !appeared else { return }
            withAnimation(reduceMotion ? nil : .smooth(duration: 0.35)) {
                appeared = true
            }
        }
    }

    // MARK: - Background

    private var loginBackground: some View {
        ZStack {
            Color(nsColor: .windowBackgroundColor)

            RadialGradient(
                colors: [
                    Color.accentColor.opacity(0.08),
                    Color.clear
                ],
                center: .top,
                startRadius: 0,
                endRadius: 520
            )
        }
        .ignoresSafeArea()
    }

    // MARK: - Header

    private var logoSection: some View {
        VStack(spacing: Theme.Spacing.md) {
            Image(systemName: "bubble.left.and.bubble.right.fill")
                .font(.system(size: 28, weight: .semibold))
                .foregroundStyle(Color.accentColor)
                .frame(width: 64, height: 64)
                .background(
                    Color.accentColor.opacity(0.11),
                    in: RoundedRectangle(cornerRadius: 18, style: .continuous)
                )
                .overlay {
                    RoundedRectangle(cornerRadius: 18, style: .continuous)
                        .strokeBorder(Color.accentColor.opacity(0.12), lineWidth: 1)
                }
                .accessibilityHidden(true)

            VStack(spacing: Theme.Spacing.xs) {
                Text(verbatim: "BarkFluff")
                    .font(.largeTitle)
                    .fontWeight(.semibold)

                Text("auth.login.subtitle")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        }
        .opacity(appeared ? 1 : 0)
        .offset(y: appeared ? 0 : 8)
        .animation(reduceMotion ? nil : .smooth(duration: 0.35), value: appeared)
    }

    // MARK: - Auth Panel

    @ViewBuilder
    private func authPanel(_ viewModel: LoginViewModel) -> some View {
        ViewThatFits(in: .horizontal) {
            wideAuthContent(viewModel)
            narrowAuthContent(viewModel)
        }
        .padding(Metrics.panelPadding)
        .frame(maxWidth: Metrics.contentWidth)
        .background {
            RoundedRectangle(cornerRadius: 24, style: .continuous)
                .fill(.regularMaterial)
        }
        .overlay {
            RoundedRectangle(cornerRadius: 24, style: .continuous)
                .strokeBorder(Color.primary.opacity(0.08), lineWidth: 1)
        }
        .shadow(color: .black.opacity(0.08), radius: 24, x: 0, y: 10)
        .opacity(appeared ? 1 : 0)
        .offset(y: appeared ? 0 : 8)
        .animation(reduceMotion ? nil : .smooth(duration: 0.35), value: appeared)
        .animation(
            reduceMotion ? nil : .smooth(duration: 0.2),
            value: viewModel.needsOTP
        )
        .animation(
            reduceMotion ? nil : .smooth(duration: 0.2),
            value: viewModel.errorMessage
        )
    }

    private func wideAuthContent(_ viewModel: LoginViewModel) -> some View {
        HStack(alignment: .top, spacing: Metrics.columnSpacing) {
            formColumn(viewModel)
                .frame(width: Metrics.formWidth, alignment: .topLeading)

            Divider()
                .frame(minHeight: 368)

            if let fastAuthViewModel {
                QRPanelView(viewModel: fastAuthViewModel)
                    .frame(width: Metrics.qrWidth, alignment: .top)
            }
        }
    }

    private func narrowAuthContent(_ viewModel: LoginViewModel) -> some View {
        VStack(spacing: Theme.Spacing.xl) {
            formColumn(viewModel)

            if fastAuthViewModel != nil {
                Divider()
            }

            if let fastAuthViewModel {
                QRPanelView(viewModel: fastAuthViewModel)
            }
        }
        .frame(maxWidth: .infinity)
    }

    // MARK: - Password Form

    private func formColumn(_ viewModel: LoginViewModel) -> some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.lg) {
            Text("auth.login.title")
                .font(.title3)
                .fontWeight(.semibold)

            VStack(alignment: .leading, spacing: Theme.Spacing.md) {
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    Text("auth.login.username_or_email")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    TextField("", text: Bindable(viewModel).usernameOrEmail)
                        .textFieldStyle(.roundedBorder)
                        .controlSize(.large)
                        .disabled(viewModel.isLoading)
                        .accessibilityLabel(Text("auth.login.username_or_email"))
                }

                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    Text("auth.login.password")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    SecureField("", text: Bindable(viewModel).password)
                        .textFieldStyle(.roundedBorder)
                        .controlSize(.large)
                        .disabled(viewModel.isLoading)
                        .accessibilityLabel(Text("auth.login.password"))
                }

                if viewModel.needsOTP {
                    VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                        Text("auth.login.otp")
                            .font(.caption)
                            .foregroundStyle(.secondary)

                        TextField("", text: Bindable(viewModel).otpCode)
                            .textFieldStyle(.roundedBorder)
                            .controlSize(.large)
                            .disabled(viewModel.isLoading)
                            .accessibilityLabel(Text("auth.login.otp"))
                            .transition(.opacity.combined(with: .move(edge: .top)))
                    }
                }
            }

            if let errorMessage = viewModel.errorMessage {
                Label {
                    Text(errorMessage)
                        .font(.callout)
                        .multilineTextAlignment(.leading)
                } icon: {
                    Image(systemName: "exclamationmark.circle.fill")
                        .foregroundStyle(.red)
                }
                .foregroundStyle(.primary)
                .padding(.horizontal, Theme.Spacing.md)
                .padding(.vertical, Theme.Spacing.sm)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(
                    Color.red.opacity(0.1),
                    in: RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous)
                )
                .transition(.opacity.combined(with: .move(edge: .top)))
            }

            let canAttemptLogin = !viewModel.usernameOrEmail.isEmpty
                && !viewModel.password.isEmpty

            Button {
                Task { await viewModel.login() }
            } label: {
                ZStack {
                    Text(viewModel.needsOTP ? "auth.login.submit_otp" : "auth.login.submit")
                        .opacity(viewModel.isLoading ? 0 : 1)

                    if viewModel.isLoading {
                        ProgressView()
                            .controlSize(.small)
                    }
                }
                .frame(maxWidth: .infinity)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .keyboardShortcut(.defaultAction)
            .disabled(viewModel.isLoading || !canAttemptLogin)

            Divider()

            HStack {
                Button("auth.login.create_account") {
                    viewModel.goToRegister()
                }
                .buttonStyle(.plain)
                .foregroundStyle(Color.accentColor)
                .font(.callout.weight(.medium))

                Spacer()

                Button("auth.login.choose_server") {
                    coordinator.currentState = .serverSelection
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .font(.callout)
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
    }
}

#Preview {
    LoginView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
        .frame(width: 900, height: 700)
}
