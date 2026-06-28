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
        @Bindable var coordinator = coordinator

        TabView(selection: $coordinator.activeTab) {
            // Вкладка чатов
            NavigationStack(path: $coordinator.chatNavigationPath) {
                ChatListView()
                    .navigationDestination(for: Chat.self) { chat in
                        ConversationView(chat: chat)
                    }
                    .navigationDestination(for: ConversationDestination.self) { destination in
                        switch destination {
                        case .userProfile(let chat):
                            UserProfilePanelView(chat: chat)
                        }
                    }
            }
            .tabItem {
                Label("navigation.tab.chats", systemImage: "message")
            }
            .tag(AppCoordinator.Tab.chats)

            // Вкладка профиля (включает экраны настроек)
            NavigationStack(path: $coordinator.profileNavigationPath) {
                ProfileView()
                    .navigationDestination(for: SettingsCategory.self) { category in
                        SettingsCategoryView(category: category)
                    }
            }
            .tabItem {
                Label("navigation.tab.profile", systemImage: "person")
            }
            .tag(AppCoordinator.Tab.profile)
        }
        .sheet(item: $coordinator.presentedSheet) { sheet in
            switch sheet {
            case .createGroupChat:
                CreateGroupChatView()
            case .userSearch:
                UserSearchView()
            case .forwardMessage(let messageID, let sourceChatID):
                ForwardChatPickerView(messageID: messageID, sourceChatID: sourceChatID)
            }
        }
    }
}

#Preview {
    MainTabView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
