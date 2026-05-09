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
                Section("Видимость профиля") {
                    visibilityPicker(
                        title: "Аватар",
                        binding: visibility(\.avatarVisibility)
                    )
                    visibilityPicker(
                        title: "Био",
                        binding: visibility(\.bioVisibility)
                    )
                    visibilityPicker(
                        title: "Email",
                        binding: visibility(\.emailVisibility)
                    )
                    visibilityPicker(
                        title: "Был(а) в сети",
                        binding: visibility(\.onlineVisibility)
                    )
                }

                Section("Прочее") {
                    Toggle("Профиль виден на сайте", isOn: bool(\.profileVisibleOnSite))
                    Toggle("Показывать в поиске", isOn: bool(\.searchVisible))
                }

                if viewModel.isSaving {
                    HStack {
                        ProgressView().scaleEffect(0.7)
                        Text("Сохранение…").font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
        }
        .navigationTitle("Приватность")
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
    private func visibilityPicker(title: String, binding: Binding<ProfileFieldVisibility>) -> some View {
        Picker(title, selection: binding) {
            Text("Все").tag(ProfileFieldVisibility.all)
            Text("Друзья").tag(ProfileFieldVisibility.friends)
            Text("Никто").tag(ProfileFieldVisibility.none)
        }
    }
}
