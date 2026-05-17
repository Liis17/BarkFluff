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
                    .navigationTitle("navigation.title.chats")
            case .profile:
                ProfileSidebarView()
                    .navigationTitle("navigation.title.settings")
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
