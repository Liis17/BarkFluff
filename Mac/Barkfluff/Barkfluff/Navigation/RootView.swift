//
//  RootView.swift
//  Barkfluff
//
//  Корневой view для маршрутизации по состоянию приложения
//

import SwiftUI

struct RootView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        Group {
            switch coordinator.currentState {
            case .loading:
                LoadingView()
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
                MainSplitView()
            }
        }
        .task {
            // Один раз — устанавливаем делегат и спрашиваем разрешение.
            container.notificationDelegate.coordinator = coordinator
            coordinator.notificationService = container.notificationService
            await container.notificationService.setupNotificationCenter(
                delegate: container.notificationDelegate
            )

            await coordinator.onAppLaunch(
                serverDiscovery: container.serverDiscoveryService,
                authService: container.authService,
                tokenProvider: container.tokenProvider
            )
        }
        .onChange(of: coordinator.currentState) { _, newState in
            // `loadCurrentUser` и `notificationService.start` требуют живого
            // соединения и валидного токена — они дёргаются из `ChatListView.task`
            // после `waitForConnectionReady()`. Здесь только глушим уведомления
            // при выходе из `.main`.
            if newState != .main {
                Task { await container.notificationService.stop() }
            }
        }
    }
}

#Preview {
    RootView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
