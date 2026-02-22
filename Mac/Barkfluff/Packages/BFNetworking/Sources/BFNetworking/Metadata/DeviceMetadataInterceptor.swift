//
//  DeviceMetadataInterceptor.swift
//  BFNetworking
//
//  gRPC ClientInterceptor для добавления метаданных устройства во все запросы
//

import Foundation
import GRPCCore

/// Добавляет метаданные устройства (Base64) в каждый gRPC-запрос.
/// Применяется ко всем клиентам — и публичным, и авторизованным.
public struct DeviceMetadataInterceptor: ClientInterceptor, Sendable {

    private let tokenProvider: TokenProvider
    private let deviceMetadata: DeviceMetadataProvider

    public init(
        tokenProvider: TokenProvider,
        deviceMetadata: DeviceMetadataProvider = .shared
    ) {
        self.tokenProvider = tokenProvider
        self.deviceMetadata = deviceMetadata
    }

    public func intercept<Input: Sendable, Output: Sendable>(
        request: StreamingClientRequest<Input>,
        context: ClientContext,
        next: (
            _ request: StreamingClientRequest<Input>,
            _ context: ClientContext
        ) async throws -> StreamingClientResponse<Output>
    ) async throws -> StreamingClientResponse<Output> {

        let deviceID = await tokenProvider.deviceID
        var metadata = request.metadata

        metadata.addString(base64Encode(deviceMetadata.deviceName), forKey: "x-device-name")
        metadata.addString(base64Encode(deviceMetadata.osName), forKey: "x-os-name")
        metadata.addString(base64Encode(deviceMetadata.appName), forKey: "x-app-name")
        metadata.addString(base64Encode(deviceMetadata.appVersion), forKey: "x-app-version")
        metadata.addString(base64Encode(deviceID), forKey: "x-device-id")

        let modifiedRequest = StreamingClientRequest<Input>(
            metadata: metadata,
            producer: request.producer
        )

        return try await next(modifiedRequest, context)
    }

    private func base64Encode(_ value: String) -> String {
        Data(value.utf8).base64EncodedString()
    }
}
