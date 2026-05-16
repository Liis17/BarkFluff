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
                authService: container.authService,
                tokenProvider: container.tokenProvider
            )
        }
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
