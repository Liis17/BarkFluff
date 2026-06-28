//
//  ProfileEditView.swift
//  Barkfluff (iOS)
//
//  Экран редактирования профиля.
//  iOS-адаптация: PhotosPicker вместо fileImporter.
//

import SwiftUI
import PhotosUI
import BFCore
import BFNetworking

struct ProfileEditView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ProfileEditViewModel

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
        @Bindable var vm = viewModel

        ScrollView {
            VStack(spacing: 24) {
                // Аватар по центру сверху
                PhotosPicker(
                    selection: $vm.selectedAvatarItem,
                    matching: .images,
                    photoLibrary: .shared()
                ) {
                    ZStack(alignment: .bottomTrailing) {
                        AvatarView(
                            imageURL: viewModel.avatarURL,
                            initials: viewModel.currentInitials,
                            size: 100
                        )

                        Image(systemName: "camera.fill")
                            .font(.system(size: 14))
                            .foregroundStyle(.white)
                            .padding(7)
                            .background(Circle().fill(Color.accentColor))
                            .offset(x: 6, y: 6)
                    }
                }
                .buttonStyle(.plain)
                .padding(.top, 20)

                // Поля формы
                VStack(alignment: .leading, spacing: 20) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("profile.edit.first_name.label")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        TextField("profile.edit.first_name.label", text: $vm.firstName)
                            .textFieldStyle(.roundedBorder)
                            .textContentType(.givenName)
                    }

                    VStack(alignment: .leading, spacing: 6) {
                        Text("profile.edit.last_name.label")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        TextField("profile.edit.last_name.label", text: $vm.lastName)
                            .textFieldStyle(.roundedBorder)
                            .textContentType(.familyName)
                    }

                    Divider()
                        .padding(.vertical, 4)

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

                        TextField("profile.edit.bio.placeholder", text: $vm.bio, axis: .vertical)
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

                    VStack(alignment: .leading, spacing: 6) {
                        Text("profile.edit.username.label")
                            .font(.caption)
                            .foregroundStyle(.secondary)

                        HStack {
                            Text("@")
                                .foregroundStyle(.secondary)
                            TextField("profile.edit.username.placeholder", text: $vm.username)
                                .textFieldStyle(.roundedBorder)
                                .textInputAutocapitalization(.never)
                                .autocorrectionDisabled()
                                .onChange(of: viewModel.username) { _, _ in
                                    viewModel.validateUsername()
                                }
                        }

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
        .navigationBarTitleDisplayMode(.inline)
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
        .fullScreenCover(isPresented: $vm.showCropper) {
            if let pendingImage = viewModel.pendingAvatarImage {
                ImageCropperView(
                    image: pendingImage,
                    aspectRatio: 1,
                    outputWidth: 1024,
                    onCancel: {
                        vm.showCropper = false
                        viewModel.cancelAvatarCropping()
                    },
                    onCrop: { cropped in
                        vm.showCropper = false
                        Task { await viewModel.uploadCropped(cropped) }
                    }
                )
            }
        }
        .alert("profile.edit.error.title", isPresented: $vm.showError) {
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
