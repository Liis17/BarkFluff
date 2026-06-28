//
//  PrivacySettingsView.swift
//  Barkfluff (iOS)
//

import SwiftUI
import BFCore
import BFNetworking

struct PrivacySettingsView: View {
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel = PrivacySettingsViewModel()

    var body: some View {
        Form {
            if viewModel.isLoading && viewModel.settings == nil {
                HStack { Spacer(); ProgressView(); Spacer() }
            }

            if let err = viewModel.errorMessage {
                Section {
                    Text(err).foregroundStyle(.red).font(.footnote)
                }
            }

            if viewModel.settings != nil {
                Section("settings.privacy.section.profile") {
                    visibilityPicker(
                        title: "settings.privacy.avatar_visibility",
                        binding: visibility(\.avatarVisibility)
                    )
                    visibilityPicker(
                        title: "settings.privacy.bio_visibility",
                        binding: visibility(\.bioVisibility)
                    )
                    visibilityPicker(
                        title: "settings.privacy.email_visibility",
                        binding: visibility(\.emailVisibility)
                    )
                    visibilityPicker(
                        title: "settings.privacy.online_visibility",
                        binding: visibility(\.onlineVisibility)
                    )
                }

                Section("settings.privacy.section.search_online") {
                    Toggle("settings.privacy.profile_visible_on_site", isOn: bool(\.profileVisibleOnSite))
                    Toggle("settings.privacy.search_visible", isOn: bool(\.searchVisible))
                }

                if viewModel.isSaving {
                    HStack {
                        ProgressView().scaleEffect(0.7)
                        Text("common.loading").font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
        }
        .navigationTitle("settings.category.privacy")
        .navigationBarTitleDisplayMode(.inline)
        .task {
            viewModel.dependencyContainer = container
            await viewModel.load()
        }
    }

    private func bool(_ keyPath: WritableKeyPath<PrivacySettingsInfo, Bool>) -> Binding<Bool> {
        Binding(
            get: { viewModel.settings?[keyPath: keyPath] ?? false },
            set: { newValue in
                Task {
                    await viewModel.update { settings in
                        settings[keyPath: keyPath] = newValue
                    }
                }
            }
        )
    }

    private func visibility(_ keyPath: WritableKeyPath<PrivacySettingsInfo, ProfileFieldVisibility>) -> Binding<ProfileFieldVisibility> {
        Binding(
            get: { viewModel.settings?[keyPath: keyPath] ?? .all },
            set: { newValue in
                Task {
                    await viewModel.update { settings in
                        settings[keyPath: keyPath] = newValue
                    }
                }
            }
        )
    }

    @ViewBuilder
    private func visibilityPicker(title: LocalizedStringKey, binding: Binding<ProfileFieldVisibility>) -> some View {
        Picker(title, selection: binding) {
            ForEach(ProfileFieldVisibility.allCases, id: \.self) { value in
                Text(value.displayNameKey).tag(value)
            }
        }
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
