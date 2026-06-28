//
//  ProfileSidebarView.swift
//  Barkfluff
//
//  Сайдбар профиля с карточкой пользователя и списком категорий настроек
//

import SwiftUI

struct ProfileSidebarView: View {
    @Environment(AppCoordinator.self) private var coordinator

    var body: some View {
        @Bindable var coordinator = coordinator

        List(selection: $coordinator.selectedSettingsCategory) {
            // Секция 1: Карточка пользователя (без выделения)
            Section {
                ProfileCardView()
                    .listRowBackground(Color.clear)
                    .listRowSeparator(.hidden)
            }

            // Секция 2: Категории настроек
            Section("profile.sidebar.settings") {
                ForEach(SettingsCategory.allCases) { category in
                    Label {
                        Text(category.title)
                    } icon: {
                        Image(systemName: category.icon)
                            .foregroundStyle(category.iconColor)
                    }
                    .tag(category)
                }
            }
        }
        .listStyle(.sidebar)
    }
}

#Preview {
    ProfileSidebarView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
        .frame(width: 320, height: 600)
}
