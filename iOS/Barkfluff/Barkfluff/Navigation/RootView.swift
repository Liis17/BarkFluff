//
//  RootView.swift
//  Barkfluff
//
//  Корневой view для маршрутизации по состоянию приложения (iOS версия)
//

import SwiftUI

struct RootView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

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
        }
        .animation(.easeInOut(duration: 0.25), value: showSplash)
        .preferredColorScheme(container.appearanceSettings.colorScheme)
        .task {
            await coordinator.onAppLaunch(
                serverDiscovery: container.serverDiscoveryService,
                authService: container.authService
            )
        }
        .onChange(of: coordinator.currentState) { _, newState in
            // Загружаем данные пользователя при переходе в main
            if newState == .main {
                Task {
                    await container.loadCurrentUser()
                }
            }
        }
    }

    /// Splash виден на cold start и пока в `.main` не загружена первая партия чатов.
    /// На `.serverSelection` / `.authentication` сплеш не показываем.
    private var showSplash: Bool {
        switch coordinator.currentState {
        case .loading:
            return true
        case .main:
            return !coordinator.isInitialChatsLoaded
        case .serverSelection, .authentication:
            return false
        }
    }
}

#Preview {
    RootView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
