//
//  PrivacySettingsView.swift
//  Barkfluff
//
//  Настройки приватности: видимость профиля, аватара, описания, email,
//  поиска и онлайн-статуса. Автосохранение через PrivacySettingsViewModel.
//

import SwiftUI
import BFCore
import BFNetworking

struct PrivacySettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = PrivacySettingsViewModel()

    var body: some View {
        Form {
            if viewModel.settings != nil {
                profileSection
                searchOnlineSection
            } else if viewModel.isLoading {
                Section {
                    HStack(spacing: 8) {
                        ProgressView().scaleEffect(0.8)
                        Text("Загрузка…")
                            .foregroundStyle(.secondary)
                    }
                }
            }

            if let error = viewModel.errorMessage {
                Section {
                    HStack(spacing: 8) {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundStyle(.orange)
                        Text(error)
                            .font(.caption)
                            .foregroundStyle(.red)
                    }
                }
            }
        }
        .formStyle(.grouped)
        .padding()
        .task {
            viewModel.dependencyContainer = container
            if viewModel.settings == nil {
                await viewModel.load()
            }
        }
    }

    // MARK: - Sections

    private var profileSection: some View {
        Section {
            Toggle("Профиль на сайте", isOn: boolBinding(\.profileVisibleOnSite))
            visibilityPicker("Видимость аватара", keyPath: \.avatarVisibility)
            visibilityPicker("Видимость описания", keyPath: \.bioVisibility)
            visibilityPicker("Видимость почты", keyPath: \.emailVisibility)
        } header: {
            Text("Профиль")
        } footer: {
            Text("«Профиль на сайте» включает публичную страницу barkfluff.com/{username}.")
        }
    }

    private var searchOnlineSection: some View {
        Section {
            Toggle("Видимость в поиске", isOn: boolBinding(\.searchVisible))
            visibilityPicker("Видимость онлайна", keyPath: \.onlineVisibility)
        } header: {
            Text("Поиск и онлайн")
        } footer: {
            Text("«Видимость в поиске» определяет, появится ли пользователь в выдаче поиска.")
        }
    }

    // MARK: - Subviews

    private func visibilityPicker(
        _ title: String,
        keyPath: WritableKeyPath<PrivacySettingsInfo, ProfileFieldVisibility>
    ) -> some View {
        Picker(title, selection: visibilityBinding(keyPath)) {
            ForEach(ProfileFieldVisibility.allCases, id: \.self) { value in
                Text(value.displayName).tag(value)
            }
        }
    }

    // MARK: - Bindings

    private func boolBinding(
        _ keyPath: WritableKeyPath<PrivacySettingsInfo, Bool>
    ) -> Binding<Bool> {
        Binding(
            get: { viewModel.settings?[keyPath: keyPath] ?? false },
            set: { newValue in
                Task {
                    await viewModel.update { $0[keyPath: keyPath] = newValue }
                }
            }
        )
    }

    private func visibilityBinding(
        _ keyPath: WritableKeyPath<PrivacySettingsInfo, ProfileFieldVisibility>
    ) -> Binding<ProfileFieldVisibility> {
        Binding(
            get: { viewModel.settings?[keyPath: keyPath] ?? .all },
            set: { newValue in
                Task {
                    await viewModel.update { $0[keyPath: keyPath] = newValue }
                }
            }
        )
    }
}

// MARK: - Display names

private extension ProfileFieldVisibility {
    var displayName: String {
        switch self {
        case .all: "Все"
        case .friends: "Друзья"
        case .none: "Никто"
        }
    }
}

#Preview {
    PrivacySettingsView()
        .environment(DependencyContainer())
}
