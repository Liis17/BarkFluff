//
//  ProfileView.swift
//  Barkfluff
//
//  Экран профиля (iOS версия)
//

import SwiftUI
import BFCore

struct ProfileView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        List {
            // Секция профиля
            Section {
                HStack(spacing: Theme.Spacing.md) {
                    AvatarView(
                        imageURL: container.currentUserAvatarURL,
                        initials: container.currentUserInitials,
                        size: 60
                    )

                    VStack(alignment: .leading, spacing: 4) {
                        Text(container.currentUser?.displayName ?? "Пользователь")
                            .font(.headline)

                        Text("@\(container.currentUser?.username ?? "username")")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(.vertical, Theme.Spacing.sm)
            }

            // Секция настроек
            Section {
                NavigationLink {
                    ProfileEditView()
                } label: {
                    Label("Редактировать профиль", systemImage: "pencil")
                }

                NavigationLink {
                    SettingsView()
                } label: {
                    Label("Настройки", systemImage: "gear")
                }
            }

            // Секция аккаунта
            Section {
                Button(role: .destructive) {
                    Task {
                        try? await coordinator.logout(
                            authService: container.authService,
                            updatesService: container.updatesService
                        )
                        await container.reset()
                    }
                } label: {
                    Label("Выйти из аккаунта", systemImage: "rectangle.portrait.and.arrow.right")
                }
            }
        }
        .navigationTitle("Профиль")
    }
}

// MARK: - Profile Edit View

struct ProfileEditView: View {
    @Environment(DependencyContainer.self) private var container
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        List {
            Section {
                HStack {
                    Spacer()
                    AvatarView(
                        imageURL: container.currentUserAvatarURL,
                        initials: container.currentUserInitials,
                        size: 100
                    )
                    Spacer()
                }
                .padding(.vertical, Theme.Spacing.lg)

                Button("Изменить фото") {
                    // TODO: Изменение фото профиля
                }
                .frame(maxWidth: .infinity)
                .buttonStyle(.bordered)
            }

            Section("Личная информация") {
                HStack {
                    Text("Имя")
                    Spacer()
                    Text(container.currentUser?.firstName ?? "")
                        .foregroundStyle(.secondary)
                }

                HStack {
                    Text("Фамилия")
                    Spacer()
                    Text(container.currentUser?.lastName ?? "")
                        .foregroundStyle(.secondary)
                }

                HStack {
                    Text("Username")
                    Spacer()
                    Text("@\(container.currentUser?.username ?? "")")
                        .foregroundStyle(.secondary)
                }
            }

            Section("О себе") {
                Text(container.currentUser?.bio ?? "Не указано")
                    .foregroundStyle(container.currentUser?.bio == nil ? .tertiary : .primary)
            }
        }
        .navigationTitle("Редактирование")
        .navigationBarTitleDisplayMode(.inline)
    }
}

// MARK: - Settings View

struct SettingsView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        List {
            Section("Уведомления") {
                Toggle("Push-уведомления", isOn: .constant(false))
                Toggle("Звук", isOn: .constant(true))
            }

            Section("Конфиденциальность") {
                NavigationLink("Активные сессии") {
                    SessionsPlaceholderView()
                }
            }

            Section("Данные") {
                HStack {
                    Text("Версия приложения")
                    Spacer()
                    Text("1.0.0")
                        .foregroundStyle(.secondary)
                }
            }
        }
        .navigationTitle("Настройки")
    }
}

// MARK: - Sessions Placeholder View

struct SessionsPlaceholderView: View {
    var body: some View {
        VStack(spacing: Theme.Spacing.lg) {
            Image(systemName: "laptopcomputer.and.iphone")
                .font(.system(size: 48))
                .foregroundStyle(.secondary)

            Text("Активные сессии")
                .font(.headline)

            Text("Функция в разработке")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .navigationTitle("Сессии")
        .navigationBarTitleDisplayMode(.inline)
    }
}

#Preview {
    NavigationStack {
        ProfileView()
            .environment(AppCoordinator())
            .environment(DependencyContainer())
    }
}
