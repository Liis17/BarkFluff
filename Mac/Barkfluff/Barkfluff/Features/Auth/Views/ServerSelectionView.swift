//
//  ServerSelectionView.swift
//  Barkfluff
//
//  Экран выбора сервера
//

import SwiftUI
import BFCore

struct ServerSelectionView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ServerSelectionViewModel?

    var body: some View {
        ScrollView {
            VStack(spacing: Theme.Spacing.xl) {
                // Заголовок
                headerSection

                // Основной контент
                if let viewModel {
                    // Секция списка серверов
                    serverListSection(viewModel)

                    // Разделитель
                    dividerSection

                    // Секция ручного ввода
                    manualInputSection(viewModel)

                    // Общая ошибка подключения
                    if let errorMessage = viewModel.errorMessage {
                        ErrorBannerView(message: errorMessage)
                            .frame(maxWidth: 400)
                    }
                }
            }
            .padding(.vertical, Theme.Spacing.xxl)
            .frame(maxWidth: .infinity)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear {
            if viewModel == nil {
                viewModel = ServerSelectionViewModel(
                    serverDiscoveryService: container.serverDiscoveryService,
                    coordinator: coordinator
                )
            }
        }
        .task {
            // Загрузить список серверов при появлении
            await viewModel?.loadServers()
        }
    }

    // MARK: - Заголовок

    private var headerSection: some View {
        VStack(spacing: Theme.Spacing.sm) {
            Image(systemName: "server.rack")
                .font(.system(size: 48))
                .foregroundStyle(.secondary)

            Text("Добро пожаловать в BarkFluff")
                .font(.title)
                .fontWeight(.semibold)

            Text("Выберите сервер для подключения")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
    }

    // MARK: - Секция списка серверов

    @ViewBuilder
    private func serverListSection(_ viewModel: ServerSelectionViewModel) -> some View {
        switch viewModel.serverListState {
        case .loading:
            VStack(spacing: Theme.Spacing.md) {
                ProgressView()
                Text("Загрузка серверов...")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: 400)
            .padding(.vertical, Theme.Spacing.lg)

        case .loaded(let servers):
            VStack(spacing: Theme.Spacing.sm) {
                ForEach(servers) { server in
                    ServerCardView(
                        server: server,
                        isConnecting: viewModel.connectingServerID == server.id,
                        isDisabled: viewModel.isAnyConnectionInProgress
                            && viewModel.connectingServerID != server.id
                    ) {
                        Task { await viewModel.connectToServer(server) }
                    }
                }
            }
            .frame(maxWidth: 400)

        case .empty:
            VStack(spacing: Theme.Spacing.sm) {
                Image(systemName: "server.rack")
                    .font(.title2)
                    .foregroundStyle(.tertiary)
                Text("Нет доступных серверов")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Text("Подключитесь к серверу вручную")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }
            .frame(maxWidth: 400)
            .padding(.vertical, Theme.Spacing.md)

        case .error(let message):
            VStack(spacing: Theme.Spacing.md) {
                Image(systemName: "exclamationmark.triangle")
                    .font(.title2)
                    .foregroundStyle(.orange)
                Text(message)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                Button("Попробовать снова") {
                    Task { await viewModel.loadServers() }
                }
                .buttonStyle(.bordered)
            }
            .frame(maxWidth: 400)
            .padding(.vertical, Theme.Spacing.md)
        }
    }

    // MARK: - Разделитель

    @ViewBuilder
    private var dividerSection: some View {
        HStack {
            Rectangle()
                .fill(.separator)
                .frame(height: 1)
            Text("или")
                .font(.caption)
                .foregroundStyle(.tertiary)
            Rectangle()
                .fill(.separator)
                .frame(height: 1)
        }
        .frame(maxWidth: 400)
    }

    // MARK: - Секция ручного ввода

    @ViewBuilder
    private func manualInputSection(_ viewModel: ServerSelectionViewModel) -> some View {
        DisclosureGroup(
            isExpanded: Bindable(viewModel).isManualSectionExpanded
        ) {
            VStack(spacing: Theme.Spacing.md) {
                TextField("beacon.example.com:7004", text: Bindable(viewModel).serverAddress)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit {
                        guard !viewModel.serverAddress.isEmpty,
                              !viewModel.isAnyConnectionInProgress else { return }
                        Task { await viewModel.connectManually() }
                    }

                Button("Подключиться") {
                    Task { await viewModel.connectManually() }
                }
                .buttonStyle(.borderedProminent)
                .disabled(viewModel.serverAddress.isEmpty || viewModel.isAnyConnectionInProgress)

                if viewModel.isLoading {
                    ProgressView()
                }
            }
            .padding(.top, Theme.Spacing.sm)
        } label: {
            Text("Подключиться вручную")
                .font(.subheadline)
                .foregroundStyle(.secondary)
        }
        .frame(maxWidth: 400)
    }
}

#Preview {
    ServerSelectionView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
