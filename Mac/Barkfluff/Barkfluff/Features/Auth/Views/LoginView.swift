//
//  LoginView.swift
//  Barkfluff
//
//  Экран входа
//

import SwiftUI
import BFCore
import BFNetworking

struct LoginView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.scenePhase) private var scenePhase

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
                    coordinator: coordinator,
                    loadCurrentUser: { await container.loadCurrentUser() }
                )
                if scenePhase == .active { viewModel?.resumeForeground() }
            }

            if fastAuthViewModel == nil {
                let vm = FastAuthViewModel(
                    fastAuthService: container.fastAuthService,
                    admissionService: container.authService
                )
                let coordinatorRef = coordinator
                let containerRef = container
                let navigation = SignInNavigationCompletion(admissionService: container.authService)
                vm.onAuthenticated = { receipt in
                    Task { @MainActor in
                        await navigation.finish(
                            receipt,
                            loadCurrentUser: { await containerRef.loadCurrentUser() },
                            isActive: { coordinatorRef.currentState == .authentication && coordinatorRef.authScreen == .login },
                            navigate: {
                                coordinatorRef.isConnectionReady = true
                                coordinatorRef.currentState = .main
                            }
                        )
                    }
                }
                fastAuthViewModel = vm
            }

            guard !appeared else { return }
            withAnimation(reduceMotion ? nil : .smooth(duration: 0.35)) {
                appeared = true
            }
        }
        .onChange(of: scenePhase) { _, phase in
            if phase == .active {
                viewModel?.resumeForeground()
            } else {
                viewModel?.pauseForeground()
            }
        }
        .onChange(of: viewModel?.usernameOrEmail) { _, value in
            guard let value else { return }
            viewModel?.identifierChanged(value)
        }
        .onDisappear {
            viewModel?.leave()
            fastAuthViewModel?.cancel()
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
            value: viewModel.flow.step
        )
        .animation(
            reduceMotion ? nil : .smooth(duration: 0.2),
            value: viewModel.flow.error
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

    // MARK: - Identifier-first Sign-in Form

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
                        .autocorrectionDisabled()
                        .disabled(viewModel.flow.step == .authenticated)
                        .accessibilityLabel(Text("auth.login.username_or_email"))
                }

                if viewModel.flow.step == .passwordRequired {
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
                    .transition(.opacity.combined(with: .move(edge: .top)))
                }

                if viewModel.flow.step == .challenge, let challenge = viewModel.flow.challenge {
                    challengeInstructions(viewModel, challenge: challenge)

                    if challenge.needsCode {
                        VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                            Text(viewModel.flow.isUsingRecoveryCode ? "auth.login.recovery_code" : "auth.login.otp")
                                .font(.caption)
                                .foregroundStyle(.secondary)

                            TextField("", text: Bindable(viewModel).otpCode)
                                .textFieldStyle(.roundedBorder)
                                .controlSize(.large)
                                .disabled(viewModel.isLoading)
                                .accessibilityLabel(Text(viewModel.flow.isUsingRecoveryCode ? "auth.login.recovery_code" : "auth.login.otp"))
                                .onSubmit { Task { await viewModel.login() } }
                        }
                        .transition(.opacity.combined(with: .move(edge: .top)))
                    }

                    if viewModel.flow.loginMode == .passwordSecondFactor,
                       !viewModel.flow.isUsingRecoveryCode,
                       viewModel.flow.availableFactors.count > 1 {
                        factorChoices(viewModel)
                    }

                    resendControl(viewModel, challenge: challenge)
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

            if viewModel.flow.step == .resolving || viewModel.flow.step == .beginning
                || viewModel.flow.step == .completing || viewModel.flow.step == .authenticated {
                HStack(spacing: Theme.Spacing.sm) {
                    ProgressView().controlSize(.small)
                    Text(viewModel.flow.step == .authenticated ? "auth.signin.finishing" : "auth.signin.working")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .center)
            } else if shouldShowPrimaryAction(viewModel) {
                Button {
                    Task { await viewModel.login() }
                } label: {
                    Text(LocalizedStringKey(primaryActionKey(viewModel)))
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .keyboardShortcut(.defaultAction)
                .disabled(viewModel.isLoading || !canSubmit(viewModel))
            }

            if viewModel.flow.canUseRecoveryCode,
               !viewModel.flow.isUsingRecoveryCode,
               (viewModel.flow.step == .passwordRequired
                   || viewModel.flow.step == .telegramReady
                   || viewModel.flow.step == .challenge) {
                Button("auth.login.use_recovery_code") {
                    Task { await viewModel.useRecoveryCode() }
                }
                .buttonStyle(.plain)
                .foregroundStyle(Color.accentColor)
                .disabled(viewModel.isLoading)
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            if viewModel.flow.step != .identifier && viewModel.flow.step != .resolving
                && viewModel.flow.step != .authenticated {
                Button("auth.login.change_identifier") {
                    viewModel.returnToIdentifier()
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .disabled(viewModel.isLoading)
            }

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
                    viewModel.chooseServer()
                }
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
                .font(.callout)
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
    }

    @ViewBuilder
    private func challengeInstructions(_ viewModel: LoginViewModel, challenge: SignInChallenge) -> some View {
        if viewModel.flow.isUsingRecoveryCode {
            Text("auth.login.recovery_instructions")
                .font(.callout)
                .foregroundStyle(.secondary)
        } else if viewModel.flow.loginMode == .telegramLogin || challenge.factor == .telegram {
            VStack(alignment: .leading, spacing: Theme.Spacing.sm) {
                Label("auth.login.telegram_waiting", systemImage: "paperplane.fill")
                    .font(.callout.weight(.medium))
                    .foregroundStyle(.primary)
                    .accessibilityElement(children: .combine)

                if let url = validatedTelegramURL(challenge.telegramURL) {
                    Link(destination: url) {
                        Label("auth.login.open_telegram", systemImage: "arrow.up.right.square")
                    }
                    .font(.callout)
                }
            }
            .padding(Theme.Spacing.md)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.accentColor.opacity(0.08), in: RoundedRectangle(cornerRadius: Theme.Radius.md))
        } else if challenge.factor == .email {
            Text("auth.login.email_code_sent")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
    }

    private func factorChoices(_ viewModel: LoginViewModel) -> some View {
        HStack(spacing: Theme.Spacing.sm) {
            ForEach(viewModel.flow.availableFactors.filter { factorKey($0) != nil }, id: \.self) { factor in
                Button {
                    Task { await viewModel.chooseFactor(factor) }
                } label: {
                    if let key = factorKey(factor) {
                        Text(LocalizedStringKey(key))
                            .font(.caption.weight(.medium))
                            .padding(.horizontal, Theme.Spacing.sm)
                            .padding(.vertical, Theme.Spacing.xs)
                            .background(
                                factor == viewModel.flow.challenge?.factor ? Color.accentColor.opacity(0.18) : Color.secondary.opacity(0.08),
                                in: Capsule()
                            )
                    }
                }
                .buttonStyle(.plain)
                .disabled(viewModel.isLoading)
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(Text("auth.login.available_factors"))
    }

    @ViewBuilder
    private func resendControl(_ viewModel: LoginViewModel, challenge: SignInChallenge) -> some View {
        if challenge.state == .waiting, challenge.needsCode, !viewModel.flow.isUsingRecoveryCode,
           (challenge.factor == .email || challenge.factor == .telegram) {
            if viewModel.flow.canResend {
                Button("auth.login.resend_code") {
                    Task { await viewModel.resendCode() }
                }
                .buttonStyle(.plain)
                .foregroundStyle(Color.accentColor)
                .disabled(viewModel.isLoading)
            } else if viewModel.flow.resendSecondsRemaining > 0 {
                let _ = viewModel.cooldownTick
                HStack(spacing: Theme.Spacing.xs) {
                    Text("auth.login.resend_cooldown")
                    Text(verbatim: "\(viewModel.flow.resendSecondsRemaining)")
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }
        }
    }

    private func shouldShowPrimaryAction(_ viewModel: LoginViewModel) -> Bool {
        switch viewModel.flow.step {
        case .identifier, .passwordRequired, .telegramReady:
            true
        case .challenge:
            viewModel.flow.challenge?.needsCode == true || viewModel.flow.canRetryChallenge
        default:
            false
        }
    }

    private func canSubmit(_ viewModel: LoginViewModel) -> Bool {
        switch viewModel.flow.step {
        case .identifier:
            !viewModel.usernameOrEmail.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        case .passwordRequired:
            !viewModel.password.isEmpty
        case .telegramReady:
            true
        case .challenge:
            viewModel.flow.canRetryChallenge || !viewModel.otpCode.isEmpty
        default:
            false
        }
    }

    private func primaryActionKey(_ viewModel: LoginViewModel) -> String {
        switch viewModel.flow.step {
        case .identifier: "auth.login.continue"
        case .passwordRequired, .telegramReady: "auth.login.submit"
        case .challenge: viewModel.flow.canRetryChallenge ? "common.retry" : "auth.login.submit_otp"
        default: "auth.login.submit"
        }
    }

    private func factorKey(_ factor: SignInFactor) -> String? {
        switch factor {
        case .email: "auth.login.factor.email"
        case .telegram: "auth.login.factor.telegram"
        case .authenticator: "auth.login.factor.authenticator"
        case .unknown: nil
        }
    }

    private func validatedTelegramURL(_ rawValue: String?) -> URL? {
        guard let rawValue, let components = URLComponents(string: rawValue),
              components.user == nil, components.password == nil,
              let scheme = components.scheme?.lowercased() else { return nil }
        if scheme == "https", components.host?.lowercased() == "t.me" {
            return components.url
        }
        if scheme == "tg", let host = components.host, !host.isEmpty {
            return components.url
        }
        return nil
    }
}

#Preview {
    LoginView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
        .frame(width: 900, height: 700)
}
