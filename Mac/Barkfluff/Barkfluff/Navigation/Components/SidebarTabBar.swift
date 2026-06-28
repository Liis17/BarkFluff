//
//  SidebarTabBar.swift
//  Barkfluff
//
//  Панель вкладок внизу сайдбара (стиль Telegram для macOS)
//

import SwiftUI

/// Панель вкладок сайдбара
struct SidebarTabBar: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        HStack(spacing: 0) {
            // Вкладка Чаты
            SidebarTabButton(isActive: coordinator.activeTab == .chats) {
                coordinator.activeTab = .chats
            } label: {
                VStack(spacing: 2) {
                    Image(systemName: "message.fill")
                        .font(.system(size: 20))
                    Text("navigation.tab.chats")
                        .font(.caption)
                }
            }

            // Вкладка Профиль (с аватаром)
            SidebarTabButton(isActive: coordinator.activeTab == .profile) {
                coordinator.activeTab = .profile
            } label: {
                VStack(spacing: 2) {
                    ProfileTabIcon(
                        avatarURL: container.currentUserAvatarURL,
                        initials: container.currentUserInitials,
                        isActive: coordinator.activeTab == .profile
                    )
                    Text("navigation.tab.profile")
                        .font(.caption)
                }
            }
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 6)
        .frame(height: 52)
    }
}

#Preview {
    SidebarTabBar()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
