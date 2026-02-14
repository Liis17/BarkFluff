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
}

#Preview {
    RootView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
