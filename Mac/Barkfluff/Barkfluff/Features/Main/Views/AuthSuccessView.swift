//
//  AuthSuccessView.swift
//  Barkfluff
//
//  Экран-заглушка после успешной авторизации
//

import SwiftUI

struct AuthSuccessView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        VStack(spacing: Theme.Spacing.xl) {
            Image(systemName: "checkmark.circle.fill")
                .font(.system(size: 72))
                .foregroundStyle(.green)

            Text("auth.success.title")
                .font(.title)

            Text("auth.success.welcome")
                .font(.subheadline)
                .foregroundStyle(.secondary)

            Button("auth.success.logout") {
                Task {
                    do {
                        try await coordinator.logout(container: container)
                    } catch {
                        // На dev-экране не показываем диалог — просто принудительно выходим.
                        await coordinator.forceLogout(container: container)
                    }
                }
            }
            .buttonStyle(.bordered)
            .padding(.top, Theme.Spacing.lg)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

#Preview {
    AuthSuccessView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
