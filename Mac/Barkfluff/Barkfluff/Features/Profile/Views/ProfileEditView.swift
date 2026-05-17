//
//  ProfileEditView.swift
//  Barkfluff
//
//  Экран редактирования профиля
//

import SwiftUI
import UniformTypeIdentifiers
import BFCore
import BFNetworking

struct ProfileEditView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ProfileEditViewModel

    /// Состояние диалога подтверждения при ошибке серверного logout.
    @State private var showLogoutFailureAlert: Bool = false
    @State private var logoutFailureMessage: String = ""
    @State private var isLoggingOut: Bool = false

    init(userService: UserServiceProtocol, fileService: FileServiceProtocol, container: DependencyContainer) {
        let vm = ProfileEditViewModel(
            userService: userService,
            fileService: fileService,
            container: container
        )
        _viewModel = State(initialValue: vm)
    }

    var body: some View {
        ScrollView {
            VStack(spacing: 24) {
                // Аватар по центру сверху (как в Telegram)
                Button {
                    viewModel.showAvatarPicker = true
                } label: {
                    ZStack(alignment: .bottomTrailing) {
                        AvatarView(
                            imageURL: viewModel.avatarURL,
                            initials: viewModel.currentInitials,
                            size: 80
                        )

                        // Иконка камеры
                        Image(systemName: "camera.fill")
                            .font(.system(size: 14))
                            .foregroundStyle(.white)
                            .padding(6)
                            .background(Circle().fill(Color.accentColor))
                            .offset(x: 4, y: 4)
                    }
                }
                .buttonStyle(.plain)
                .fileImporter(
                    isPresented: $viewModel.showAvatarPicker,
                    allowedContentTypes: [.image],
                    allowsMultipleSelection: false
                ) { result in
                    Task {
                        await viewModel.handleAvatarSelection(result: result)
                    }
                }
                .padding(.top, 20)

                // Поля формы
                VStack(alignment: .leading, spacing: 20) {
                    // Имя
                    VStack(alignment: .leading, spacing: 6) {
                        Text("profile.edit.first_name.label")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        TextField("profile.edit.first_name.label", text: $viewModel.firstName)
                            .textFieldStyle(.roundedBorder)
                    }

                    // Фамилия
                    VStack(alignment: .leading, spacing: 6) {
                        Text("profile.edit.last_name.label")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        TextField("profile.edit.last_name.label", text: $viewModel.lastName)
                            .textFieldStyle(.roundedBorder)
                    }

                    Divider()
                        .padding(.vertical, 4)

                    // О себе
                    VStack(alignment: .leading, spacing: 6) {
                        HStack {
                            Text("profile.edit.bio.label")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                            Spacer()
                            Text("\(viewModel.bio.count)/200")
                                .font(.caption2)
                                .foregroundStyle(.tertiary)
                        }

                        TextField("profile.edit.bio.placeholder", text: $viewModel.bio, axis: .vertical)
                            .textFieldStyle(.roundedBorder)
                            .lineLimit(3...6)
                            .onChange(of: viewModel.bio) { _, newValue in
                                if newValue.count > 200 {
                                    viewModel.bio = String(newValue.prefix(200))
                                }
                            }

                        Text("profile.edit.bio.hint")
                            .font(.caption2)
                            .foregroundStyle(.tertiary)
                    }

                    Divider()
                        .padding(.vertical, 4)

                    // Username
                    VStack(alignment: .leading, spacing: 6) {
                        Text("profile.edit.username.label")
                            .font(.caption)
                            .foregroundStyle(.secondary)

                        HStack {
                            Text("@")
                                .foregroundStyle(.secondary)
                            TextField("profile.edit.username.placeholder", text: $viewModel.username)
                                .textFieldStyle(.roundedBorder)
                                .onChange(of: viewModel.username) { _, _ in
                                    viewModel.validateUsername()
                                }
                        }

                        // Статус валидации
                        if let status = viewModel.usernameStatus {
                            HStack(spacing: 4) {
                                if status == .checking {
                                    ProgressView()
                                        .scaleEffect(0.6)
                                } else {
                                    Image(systemName: status.icon)
                                }
                                if let key = status.messageKey {
                                    Text(key)
                                }
                            }
                            .font(.caption)
                            .foregroundStyle(status.color)
                        }
                    }

                    Divider()
                        .padding(.vertical, 8)

                    // Выход из аккаунта
                    Button(role: .destructive) {
                        Task { await performLogout() }
                    } label: {
                        Text("profile.edit.logout")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .tint(.red)
                    .disabled(isLoggingOut)
                }
                .padding(.horizontal, 24)
            }
        }
        .navigationTitle("profile.edit.title")
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("common.save") {
                    Task {
                        await viewModel.saveChanges()
                    }
                }
                .disabled(!viewModel.canSave)
            }
        }
        .task {
            await viewModel.loadProfile()
        }
        .overlay {
            if viewModel.isLoading {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(.ultraThinMaterial)
            }
        }
        .sheet(isPresented: $viewModel.showCropper) {
            if let pendingImage = viewModel.pendingAvatarImage {
                ImageCropperView(
                    image: pendingImage,
                    aspectRatio: 1,
                    outputWidth: 1024,
                    onCancel: {
                        viewModel.showCropper = false
                        viewModel.cancelAvatarCropping()
                    },
                    onCrop: { cropped in
                        viewModel.showCropper = false
                        Task { await viewModel.uploadCropped(cropped) }
                    }
                )
            }
        }
        .alert("profile.edit.error.title", isPresented: $viewModel.showError) {
            Button("common.ok", role: .cancel) { }
        } message: {
            Text(viewModel.errorMessage)
        }
        .alert("profile.edit.logout_failure.title", isPresented: $showLogoutFailureAlert) {
            Button("common.retry") {
                Task { await performLogout() }
            }
            Button("profile.edit.logout_failure.force", role: .destructive) {
                Task { await performForceLogout() }
            }
            Button("common.cancel", role: .cancel) { }
        } message: {
            Text("profile.edit.logout_failure.message \(logoutFailureMessage)")
        }
    }

    // MARK: - Logout

    private func performLogout() async {
        guard !isLoggingOut else { return }
        isLoggingOut = true
        defer { isLoggingOut = false }

        do {
            try await coordinator.logout(container: container)
        } catch {
            logoutFailureMessage = (error as? LocalizedError)?.errorDescription ?? "\(error)"
            showLogoutFailureAlert = true
        }
    }

    private func performForceLogout() async {
        guard !isLoggingOut else { return }
        isLoggingOut = true
        defer { isLoggingOut = false }

        await coordinator.forceLogout(container: container)
    }
}
