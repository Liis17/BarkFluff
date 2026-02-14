//
//  ServerSelectionView.swift
//  Barkfluff
//
//  Экран выбора сервера
//

import SwiftUI

struct ServerSelectionView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container
    @State private var viewModel: ServerSelectionViewModel?

    var body: some View {
        VStack(spacing: Theme.Spacing.xl) {
            Image(systemName: "server.rack")
                .font(.system(size: 64))
                .foregroundStyle(.secondary)

            Text("Подключение к серверу")
                .font(.title)

            Text("Введите адрес Beacon сервера")
                .font(.subheadline)
                .foregroundStyle(.secondary)

            if let viewModel {
                TextField("beacon.example.com:7004", text: Bindable(viewModel).serverAddress)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 300)
                    .onSubmit {
                        guard !viewModel.serverAddress.isEmpty, !viewModel.isLoading else { return }
                        Task { await viewModel.connect() }
                    }

                if let errorMessage = viewModel.errorMessage {
                    ErrorBannerView(message: errorMessage)
                        .frame(width: 300)
                }

                Button("Подключиться") {
                    Task { await viewModel.connect() }
                }
                .buttonStyle(.borderedProminent)
                .disabled(viewModel.serverAddress.isEmpty || viewModel.isLoading)

                if viewModel.isLoading {
                    ProgressView()
                        .padding(.top, Theme.Spacing.sm)
                }
            }
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
    }
}

#Preview {
    ServerSelectionView()
        .environment(AppCoordinator())
        .environment(DependencyContainer())
}
