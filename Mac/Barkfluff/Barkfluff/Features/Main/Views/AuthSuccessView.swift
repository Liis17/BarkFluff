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

            Text("Авторизация прошла успешно!")
                .font(.title)

            Text("Добро пожаловать в BarkFluff")
                .font(.subheadline)
                .foregroundStyle(.secondary)

            Button("Выйти") {
                Task {
                    await coordinator.logout(authService: container.authService, updatesService: container.updatesService)
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
