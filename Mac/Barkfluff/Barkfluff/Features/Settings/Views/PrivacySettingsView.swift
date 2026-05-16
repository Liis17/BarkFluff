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
                        Text("common.loading")
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
            Toggle("settings.privacy.profile_visible_on_site", isOn: boolBinding(\.profileVisibleOnSite))
            visibilityPicker("settings.privacy.avatar_visibility", keyPath: \.avatarVisibility)
            visibilityPicker("settings.privacy.bio_visibility", keyPath: \.bioVisibility)
            visibilityPicker("settings.privacy.email_visibility", keyPath: \.emailVisibility)
        } header: {
            Text("settings.privacy.section.profile")
        } footer: {
            Text("settings.privacy.profile_on_site.footer")
        }
    }

    private var searchOnlineSection: some View {
        Section {
            Toggle("settings.privacy.search_visible", isOn: boolBinding(\.searchVisible))
            visibilityPicker("settings.privacy.online_visibility", keyPath: \.onlineVisibility)
        } header: {
            Text("settings.privacy.section.search_online")
        } footer: {
            Text("settings.privacy.search_visible.footer")
        }
    }

    // MARK: - Subviews

    private func visibilityPicker(
        _ titleKey: LocalizedStringKey,
        keyPath: WritableKeyPath<PrivacySettingsInfo, ProfileFieldVisibility>
    ) -> some View {
        Picker(titleKey, selection: visibilityBinding(keyPath)) {
            ForEach(ProfileFieldVisibility.allCases, id: \.self) { value in
                Text(value.displayNameKey).tag(value)
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
    var displayNameKey: LocalizedStringKey {
        switch self {
        case .all: "enum.profile_visibility.all"
        case .friends: "enum.profile_visibility.friends"
        case .none: "enum.profile_visibility.none"
        }
    }
}

#Preview {
    PrivacySettingsView()
        .environment(DependencyContainer())
}
