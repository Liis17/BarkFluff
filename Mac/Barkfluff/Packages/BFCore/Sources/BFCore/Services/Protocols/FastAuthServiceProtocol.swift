//
//  FastAuthServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса быстрой авторизации
//

import Foundation

/// Протокол сервиса быстрой авторизации
public protocol FastAuthServiceProtocol: Sendable {
    /// Сгенерировать токен для быстрой авторизации
    func generateToken(type: FastAuthType) async throws -> FastAuthToken

    /// Проверить статус быстрой авторизации
    func checkStatus(fastAuthID: String) async throws -> FastAuthStatus

    /// Принять запрос быстрой авторизации
    func accept(fastAuthID: String) async throws

    /// Подписаться на результат быстрой авторизации
    func subscribeToResult(fastAuthID: String) async throws -> AsyncThrowingStream<FastAuthResult, Error>

    /// Подписаться на запросы быстрой авторизации
    func subscribeToRequests() async throws -> AsyncThrowingStream<FastAuthRequest, Error>

    /// Подключить устройство
    func connectDevice(deviceToken: String) async throws

    /// Принять подключение устройства
    func acceptDevice(deviceID: String) async throws

    /// Подписаться на статус подключения устройства
    func subscribeToConnectStatus() async throws -> AsyncThrowingStream<ConnectDeviceStatus, Error>

    /// Получить список подключённых устройств
    func listConnectedDevices() async throws -> [ConnectedDevice]

    /// Сгенерировать токен для подключения устройства
    func generateConnectDeviceToken() async throws -> String
}
