//
//  SidebarContainerView.swift
//  Barkfluff
//
//  Контейнер сайдбара с переключением между чатами и профилем
//

import SwiftUI

/// Контейнер сайдбара с табами внизу
struct SidebarContainerView: View {
    @Environment(AppCoordinator.self) private var coordinator

    var body: some View {
        VStack(spacing: 0) {
            // Контент в зависимости от активной вкладки
            switch coordinator.activeTab {
            case .chats:
                ChatListView()
                    .navigationTitle("Чаты")
            case .profile:
                ProfileSidebarView()
                    .navigationTitle("Настройки")
            }

            Divider()

            // Панель вкладок внизу
            SidebarTabBar()
        }
    }
}

#Preview {
    SidebarContainerView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
        .frame(width: 320, height: 600)
}
