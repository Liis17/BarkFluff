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
                        Text("Имя")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        TextField("Имя", text: $viewModel.firstName)
                            .textFieldStyle(.roundedBorder)
                    }

                    // Фамилия
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Фамилия")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        TextField("Фамилия", text: $viewModel.lastName)
                            .textFieldStyle(.roundedBorder)
                    }

                    Divider()
                        .padding(.vertical, 4)

                    // О себе
                    VStack(alignment: .leading, spacing: 6) {
                        HStack {
                            Text("О себе")
                                .font(.caption)
                                .foregroundStyle(.secondary)
                            Spacer()
                            Text("\(viewModel.bio.count)/200")
                                .font(.caption2)
                                .foregroundStyle(.tertiary)
                        }

                        TextField("Расскажите о себе", text: $viewModel.bio, axis: .vertical)
                            .textFieldStyle(.roundedBorder)
                            .lineLimit(3...6)
                            .onChange(of: viewModel.bio) { _, newValue in
                                if newValue.count > 200 {
                                    viewModel.bio = String(newValue.prefix(200))
                                }
                            }

                        Text("Любые подробности: возраст, род занятий или город")
                            .font(.caption2)
                            .foregroundStyle(.tertiary)
                    }

                    Divider()
                        .padding(.vertical, 4)

                    // Username
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Имя пользователя")
                            .font(.caption)
                            .foregroundStyle(.secondary)

                        HStack {
                            Text("@")
                                .foregroundStyle(.secondary)
                            TextField("username", text: $viewModel.username)
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
                                Text(status.message)
                            }
                            .font(.caption)
                            .foregroundStyle(status.color)
                        }
                    }

                    Divider()
                        .padding(.vertical, 8)

                    // Выход из аккаунта
                    Button(role: .destructive) {
                        Task {
                            await coordinator.logout(
                                authService: container.authService,
                                updatesService: container.updatesService,
                                onlineStatusService: container.onlineStatusService
                            )
                        }
                    } label: {
                        Text("Выйти из аккаунта")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.bordered)
                    .tint(.red)
                }
                .padding(.horizontal, 24)
            }
        }
        .navigationTitle("Мой профиль")
        .toolbar {
            ToolbarItem(placement: .confirmationAction) {
                Button("Сохранить") {
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
        .alert("Ошибка", isPresented: $viewModel.showError) {
            Button("OK", role: .cancel) { }
        } message: {
            Text(viewModel.errorMessage)
        }
    }
}
