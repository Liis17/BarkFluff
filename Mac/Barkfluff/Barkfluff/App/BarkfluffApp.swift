//
//  BarkfluffApp.swift
//  Barkfluff
//
//  Точка входа приложения
//

import SwiftUI
import BFCore

@main
struct BarkfluffApp: App {
    @State private var coordinator = AppCoordinator()
    @State private var container = DependencyContainer()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(coordinator)
                .environment(container)
                .environment(\.locale, container.localizationSettings.appliedLocale)
        }
        .windowStyle(.automatic)
        .windowToolbarStyle(.unified)

        Settings {
            SettingsView()
                .environment(container)
                .environment(coordinator)
                .environment(\.locale, container.localizationSettings.appliedLocale)
        }
        .commands {
            // Keyboard shortcuts для навигации по сайдбару
            CommandGroup(after: .sidebar) {
                Button("commands.chats") {
                    coordinator.activeTab = .chats
                }
                .keyboardShortcut("1", modifiers: .command)

                Button("commands.profile") {
                    coordinator.activeTab = .profile
                }
                .keyboardShortcut("2", modifiers: .command)
            }
        }
    }
}
