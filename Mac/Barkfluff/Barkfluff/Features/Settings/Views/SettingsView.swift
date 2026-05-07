//
//  SettingsView.swift
//  Barkfluff
//
//  Окно настроек macOS (верхнее меню → Settings…). По составу совпадает
//  с сайдбаром профиля: одни и те же категории и одни и те же экраны.
//

import SwiftUI

struct SettingsView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        TabView {
            ForEach(SettingsCategory.allCases) { category in
                SettingsCategoryView(category: category)
                    .tabItem {
                        Label {
                            Text(category.title)
                        } icon: {
                            Image(systemName: category.icon)
                        }
                    }
                    .tag(category)
            }
        }
        .frame(minWidth: 540, minHeight: 440)
    }
}

#Preview {
    SettingsView()
        .environment(DependencyContainer())
        .environment(AppCoordinator())
}
