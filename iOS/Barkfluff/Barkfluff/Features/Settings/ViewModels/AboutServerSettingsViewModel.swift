//
//  AboutServerSettingsViewModel.swift
//  Barkfluff (iOS)
//

import SwiftUI
import Observation
import BFCore
import BFNetworking

@MainActor
@Observable
final class AboutServerSettingsViewModel {
    var services: [ServiceEntry] = []
    var beaconHost: String?
    var beaconPort: Int?

    var lastPingMillis: Int?
    var pingError: String?
    var isPinging: Bool = false

    weak var dependencyContainer: DependencyContainer?

    struct ServiceEntry: Identifiable, Hashable {
        let kind: ServiceKind
        let host: String
        let port: Int
        let useTLS: Bool

        var id: String { kind.rawValue }
        var address: String { "\(host):\(port)" }
    }

    func refresh() async {
        guard let dc = dependencyContainer else { return }

        let endpoint = await dc.serverDiscoveryService.currentServerEndpoint()
        beaconHost = endpoint?.host
        beaconPort = endpoint?.port

        let endpoints = await dc.connectionManager.endpoints()
        services = ServiceKind.allCases.compactMap { kind -> ServiceEntry? in
            guard let ep = endpoints[kind] else { return nil }
            return ServiceEntry(kind: kind, host: ep.host, port: ep.port, useTLS: ep.useTLS)
        }
    }

    func ping() async {
        guard let dc = dependencyContainer else { return }
        isPinging = true
        pingError = nil
        do {
            let seconds = try await dc.serverDiscoveryService.pingCurrentServer()
            lastPingMillis = Int((seconds * 1000).rounded())
        } catch {
            pingError = error.localizedDescription
            lastPingMillis = nil
        }
        isPinging = false
    }
}

extension ServiceKind {
    var displayName: String {
        switch self {
        case .beacon: return "Beacon"
        case .identity: return "Identity"
        case .users: return "Users"
        case .messages: return "Messages"
        case .files: return "Files"
        case .updates: return "Updates"
        case .fastauth: return "Fast Auth"
        case .navigator: return "Navigator"
        case .configuration: return "Configuration"
        case .onliner: return "Onliner"
        }
    }

    var systemImage: String {
        switch self {
        case .beacon: return "antenna.radiowaves.left.and.right"
        case .identity: return "person.badge.key.fill"
        case .users: return "person.2.fill"
        case .messages: return "message.fill"
        case .files: return "doc.fill"
        case .updates: return "arrow.triangle.2.circlepath"
        case .fastauth: return "bolt.fill"
        case .navigator: return "map.fill"
        case .configuration: return "slider.horizontal.3"
        case .onliner: return "circle.fill"
        }
    }
}
