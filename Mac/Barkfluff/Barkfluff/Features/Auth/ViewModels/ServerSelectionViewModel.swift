//
//  ServerSelectionViewModel.swift
//  Barkfluff
//
//  ViewModel для экрана выбора сервера
//

import SwiftUI
import Observation
import BFCore

@Observable
final class ServerSelectionViewModel {
    var serverAddress = ""
    var isLoading = false
    var errorMessage: String?
    var serverName: String?

    private let serverDiscoveryService: ServerDiscoveryServiceProtocol
    private let coordinator: AppCoordinator

    init(serverDiscoveryService: ServerDiscoveryServiceProtocol, coordinator: AppCoordinator) {
        self.serverDiscoveryService = serverDiscoveryService
        self.coordinator = coordinator
    }

    func connect() async {
        isLoading = true
        errorMessage = nil
        serverName = nil

        let (host, port) = parseAddress(serverAddress)

        do {
            let serverInfo = try await serverDiscoveryService.connect(host: host, port: port)
            serverName = serverInfo.name
            coordinator.currentState = .authentication
            coordinator.authScreen = .login
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    private func parseAddress(_ address: String) -> (host: String, port: Int) {
        let trimmed = address.trimmingCharacters(in: .whitespacesAndNewlines)
        let parts = trimmed.split(separator: ":", maxSplits: 1)
        let host = String(parts[0])
        let port = parts.count > 1 ? Int(parts[1]) ?? 7004 : 7004
        return (host, port)
    }
}
