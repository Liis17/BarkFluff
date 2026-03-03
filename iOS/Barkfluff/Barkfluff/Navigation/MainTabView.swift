//
//  MainTabView.swift
//  Barkfluff
//
//  Главный таб-бар приложения (iOS версия)
//

import SwiftUI
import BFCore

struct MainTabView: View {
    @Environment(AppCoordinator.self) private var coordinator

    var body: some View {
        TabView(selection: Binding(
            get: { coordinator.activeTab },
            set: { coordinator.activeTab = $0 }
        )) {
            // Вкладка чатов
            NavigationStack(path: Binding(
                get: { coordinator.chatNavigationPath },
                set: { coordinator.chatNavigationPath = $0 }
            )) {
                ChatListView()
                    .navigationDestination(for: Chat.self) { chat in
                        ConversationView(chat: chat)
                    }
            }
            .tabItem {
                Label("Чаты", systemImage: "message")
            }
            .tag(AppCoordinator.Tab.chats)

            // Вкладка профиля
            NavigationStack(path: Binding(
                get: { coordinator.profileNavigationPath },
                set: { coordinator.profileNavigationPath = $0 }
            )) {
                ProfileView()
            }
            .tabItem {
                Label("Профиль", systemImage: "person")
            }
            .tag(AppCoordinator.Tab.profile)
        }
    }
}

#Preview {
    MainTabView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
