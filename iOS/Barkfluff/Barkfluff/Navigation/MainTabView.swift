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
            NavigationSplitView {
                ChatListView()
                    .navigationSplitViewColumnWidth(min: 280, ideal: 320, max: 420)
            } detail: {
                NavigationStack(path: $coordinator.chatNavigationPath) {
                    Group {
                        if let chat = coordinator.selectedChat {
                            ConversationView(chat: chat)
                        } else {
                            ContentUnavailableView(
                                "chat_list.empty.title",
                                systemImage: "message",
                                description: Text("chat_list.empty.description")
                            )
                        }
                    }
                    .navigationDestination(for: ConversationDestination.self) { destination in
                        switch destination {
                        case .userProfile(let chat):
                            UserProfilePanelView(chat: chat)
                        }
                    }
                }
            }
            .navigationSplitViewStyle(.balanced)
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
