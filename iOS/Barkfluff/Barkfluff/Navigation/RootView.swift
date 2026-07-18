//
//  RootView.swift
//  Barkfluff
//
//  Корневой view для маршрутизации по состоянию приложения (iOS версия)
//

import SwiftUI
import BFCore
import BFCalls

struct RootView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ZStack {
            Group {
                switch coordinator.currentState {
                case .loading:
                    // Под сплешем — `LoadingView` больше не используется.
                    Color.clear
                case .serverSelection:
                    ServerSelectionView()
                case .authentication:
                    switch coordinator.authScreen {
                    case .login:
                        LoginView()
                    case .register:
                        RegisterView()
                    }
                case .main:
                    MainTabView()
                }
            }

            if showSplash {
                SplashView()
                    .transition(.opacity)
                    .zIndex(1)
            }

            // Полноэкранный оверлей звонка (только при открытом приложении).
            if coordinator.currentState == .main {
                CallOverlayView(controller: container.callController)
                    .zIndex(2)
            }
        }
        .animation(.easeInOut(duration: 0.25), value: showSplash)
        .animation(reduceMotion ? nil : .easeInOut(duration: 0.2), value: container.callController.phase)
        .preferredColorScheme(container.appearanceSettings.colorScheme)
        .task {
            await coordinator.onAppLaunch(
                serverDiscovery: container.serverDiscoveryService,
                authService: container.authService,
                tokenProvider: container.tokenProvider
            )
        }
        .onChange(of: coordinator.currentState) { _, newState in
            if newState == .main {
                configureCalls()
            } else {
                Task { await container.callController.stop() }
            }
        }
        .onChange(of: scenePhase) { _, phase in
            // iOS-ограничение: звонки живут только при активном приложении
            // (нет VoIP-push/CallKit). В фоне — завершаем и глушим стрим,
            // при возврате — поднимаем заново.
            guard coordinator.currentState == .main else { return }
            switch phase {
            case .background:
                Task { await container.callController.stop() }
            case .active:
                configureCalls()
            default:
                break
            }
        }
    }

    /// Настраивает резолвер имён/аватаров участников и запускает приём событий звонков.
    @MainActor
    private func configureCalls() {
        let userService = container.userService
        container.callController.participantResolver = { userID in
            guard let user = try? await userService.getUser(userID: userID) else { return nil }
            return CallParticipantDisplay(
                name: user.displayName,
                initials: user.initials,
                avatarURL: user.profilePicturePreviewURL
            )
        }
        container.callController.start()
    }

    /// Splash виден только пока `AppCoordinator.onAppLaunch` определяет, куда
    /// маршрутизировать (cold start, состояние `.loading`). Как только мы попали
    /// в `.main`, сплеш снимается — пользователь видит `MainTabView` с пустым
    /// `ChatListView` и крутилкой/плейсхолдерами вместо чёрного сплеша.
    private var showSplash: Bool {
        coordinator.currentState == .loading
    }
}

#Preview {
    RootView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
