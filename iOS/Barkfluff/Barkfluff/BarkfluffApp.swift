//
//  BarkfluffApp.swift
//  Barkfluff
//
//  Точка входа приложения (iOS версия)
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
        }
    }
}
