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
                        "Выберите чат",
                        systemImage: "message",
                        description: Text("Выберите чат из списка слева")
                    )
                    .navigationTitle("")
                }

            case .profile:
                // Профиль: зависит от выбранной категории настроек
                if let category = coordinator.selectedSettingsCategory {
                    switch category {
                    case .editProfile:
                        ProfileEditView(
                            userService: container.userService,
                            fileService: container.fileService,
                            container: container
                        )
                        .navigationTitle("")
                    case .privacy:
                        // Конфиденциальность - настройки безопасности и хранилища токенов
                        SecuritySettingsView(viewModel: container.settingsViewModel)
                            .navigationTitle("")
                    case .activeSessions:
                        SessionsView(viewModel: container.settingsViewModel)
                            .navigationTitle("")
                    case .general, .notifications, .dataAndStorage:
                        SettingsPlaceholderView(category: category)
                            .navigationTitle("")
                    }
                } else {
                    ContentUnavailableView(
                        "Выберите раздел",
                        systemImage: "sidebar.left",
                        description: Text("Выберите раздел настроек из списка слева")
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
        // Скрыть sidebar когда открыта панель профиля
        .onChange(of: coordinator.showProfilePanel) { _, isPresented in
            if isPresented {
                columnVisibility = .detailOnly
            } else {
                columnVisibility = .all
            }
        }
    }
}

#Preview {
    MainSplitView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
