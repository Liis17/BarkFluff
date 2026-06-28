//
//  MainSplitView.swift
//  Barkfluff
//
//  Главный split view с sidebar и detail
//

import SwiftUI
import BFCore

struct MainSplitView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var columnVisibility: NavigationSplitViewVisibility = .all

    var body: some View {
        NavigationSplitView(columnVisibility: $columnVisibility) {
            // Sidebar: контейнер с табами
            SidebarContainerView()
                .navigationSplitViewColumnWidth(min: 280, ideal: 320, max: 420)
        } detail: {
            // Detail: зависит от активной вкладки и выбранной категории
            switch coordinator.activeTab {
            case .chats:
                // Чаты: выбранная переписка или placeholder
                if let chat = coordinator.selectedChat {
                    ConversationView(chat: chat)
                        .navigationTitle("")
                } else {
                    ContentUnavailableView(
                        "main.detail.no_chat.title",
                        systemImage: "message",
                        description: Text("main.detail.no_chat.description")
                    )
                    .navigationTitle("")
                }

            case .profile:
                // Профиль: зависит от выбранной категории настроек
                if let category = coordinator.selectedSettingsCategory {
                    SettingsCategoryView(category: category)
                        .navigationTitle("")
                } else {
                    ContentUnavailableView(
                        "main.detail.no_settings.title",
                        systemImage: "sidebar.left",
                        description: Text("main.detail.no_settings.description")
                    )
                    .navigationTitle("")
                }
            }
        }
        .navigationSplitViewStyle(.balanced)
        // Inspector для панели профиля
        .inspector(isPresented: Binding(
            get: { coordinator.showProfilePanel },
            set: { isPresented in
                if !isPresented {
                    coordinator.closeProfilePanel()
                }
            }
        )) {
            if let chat = coordinator.profilePanelChat {
                UserProfilePanelView(chat: chat)
                    .inspectorColumnWidth(min: 280, ideal: 320, max: 400)
            }
        }
        // Модальные окна (создание группового чата, поиск пользователей, выбор чата для пересылки)
        .sheet(item: Bindable(coordinator).presentedSheet) { sheet in
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
    MainSplitView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
