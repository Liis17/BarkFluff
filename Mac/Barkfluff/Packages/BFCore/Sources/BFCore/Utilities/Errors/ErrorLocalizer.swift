//
//  ErrorLocalizer.swift
//  BFCore
//
//  Локализация ошибок
//

import Foundation

/// Локализатор ошибок
public enum ErrorLocalizer {

    /// Локализовать любую ошибку в строку для отображения пользователю.
    /// - Parameter locale: локаль для BFError; для системных LocalizedError используется системная локаль.
    public static func localize(_ error: Error, locale: Locale = .current) -> String {
        // Если это уже BFError — используем его описание с явной локалью
        if let bfError = error as? BFError {
            return bfError.errorDescription(in: locale) ?? String(localized: "bfcore.error.unknown", bundle: .module, locale: locale)
        }

        // Если это LocalizedError — используем errorDescription
        if let localized = error as? LocalizedError {
            return localized.errorDescription ?? error.localizedDescription
        }

        // Иначе — системное описание
        return error.localizedDescription
    }

    /// Получить предложение по исправлению ошибки.
    public static func recoverySuggestion(for error: Error, locale: Locale = .current) -> String? {
        if let bfError = error as? BFError {
            return bfError.recoverySuggestion(in: locale)
        }

        if let localized = error as? LocalizedError {
            return localized.recoverySuggestion
        }

        return nil
    }

    /// Проверить, требуется ли повторная авторизация
    public static func requiresReauth(_ error: Error) -> Bool {
        if let bfError = error as? BFError {
            return bfError.requiresReauth
        }
        return false
    }
}
