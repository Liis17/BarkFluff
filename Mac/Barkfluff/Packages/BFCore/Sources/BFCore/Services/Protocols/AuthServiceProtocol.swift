//
//  AuthServiceProtocol.swift
//  BFCore
//
//  Протокол сервиса аутентификации
//

import Foundation
import BFNetworking

/// Протокол сервиса аутентификации
public protocol AuthServiceProtocol: Sendable {
    /// Авторизоваться по username/email и паролю.
    /// Автоматически определяет username vs email по наличию "@".
    /// Бросает BFError.otpRequired, если сервер требует 2FA.
    func login(login: String, password: String, otpCode: String?) async throws

    /// Зарегистрировать новый аккаунт. Возвращает codeID для подтверждения.
    func register(firstName: String, lastName: String, username: String, email: String) async throws -> String

    /// Подтвердить аккаунт по коду. Автоматически получает оба токена.
    func confirmAccount(codeID: String, code: String) async throws

    /// Попытка восстановить сессию (auto-login).
    /// Возвращает true, если удалось восстановить, false если нужна авторизация.
    func tryRestoreSession() async -> Bool

    /// Выйти из аккаунта: уведомить сервер (`Identity.Logout`) и стереть все токены.
    /// Бросает ошибку, если серверный шаг не удался — локальные токены при этом НЕ трогаются,
    /// чтобы вызывающий мог решить (повторить / выйти принудительно через `forceLocalLogout`).
    func logout() async throws

    /// Принудительная локальная очистка токенов без обращения к серверу.
    /// Используется как fallback после неудачного `logout()`.
    func forceLocalLogout() async

    /// Установить пароль после регистрации
    func setPassword(password: String) async throws

    /// Включить 2FA аутентификацию
    func enableOTP() async throws -> OTPSetupInfo

    /// Подтвердить включение 2FA
    func confirmOTP(code: String) async throws

    /// Сохранить пару токенов, выпущенных через FastAuth (QR-логин).
    /// Серверный шаг создания сессии происходит в FastAuth-микросервисе при `AcceptFastAuth`,
    /// здесь только локально применяем уже выданные токены — аналогично сохранению после `login()`.
    func applyFastAuthTokens(
        accessToken: String, accessExpiresAt: Date,
        refreshToken: String, refreshExpiresAt: Date
    ) async
}
