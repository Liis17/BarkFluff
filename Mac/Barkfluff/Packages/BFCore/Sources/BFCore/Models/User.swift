//
//  User.swift
//  BFCore
//
//  Доменная модель пользователя
//

import Foundation

/// Пользователь BarkFluff
public struct User: Identifiable, Hashable, Sendable {
    public let id: Int64
    public var firstName: String
    public var lastName: String
    public var username: String
    public var email: String?
    public var bio: String?
    public var registrationDate: Date
    public var profilePictureURL: String?
    public var profilePicturePreviewURL: String?
    public var profilePosterFileID: String?
    public var badges: [UserBadge]
    public var storageLimitBytes: Int64

    public init(
        id: Int64,
        firstName: String,
        lastName: String,
        username: String,
        email: String? = nil,
        bio: String? = nil,
        registrationDate: Date,
        profilePictureURL: String? = nil,
        profilePicturePreviewURL: String? = nil,
        profilePosterFileID: String? = nil,
        badges: [UserBadge] = [],
        storageLimitBytes: Int64 = 0
    ) {
        self.id = id
        self.firstName = firstName
        self.lastName = lastName
        self.username = username
        self.email = email
        self.bio = bio
        self.registrationDate = registrationDate
        self.profilePictureURL = profilePictureURL
        self.profilePicturePreviewURL = profilePicturePreviewURL
        self.profilePosterFileID = profilePosterFileID
        self.badges = badges
        self.storageLimitBytes = storageLimitBytes
    }

    /// Отображаемое имя: "Имя Фамилия"
    public var displayName: String {
        [firstName, lastName]
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .joined(separator: " ")
    }

    /// Инициалы для аватара: "ИФ"
    public var initials: String {
        let first = firstName.trimmingCharacters(in: .whitespacesAndNewlines).first.map(String.init) ?? ""
        let last = lastName.trimmingCharacters(in: .whitespacesAndNewlines).first.map(String.init) ?? ""
        return first + last
    }

    /// Полное имя с username: "Имя Фамилия (@username)"
    public var fullNameWithUsername: String {
        "\(displayName) (@\(username))"
    }
}

/// Бейдж пользователя
public struct UserBadge: Identifiable, Hashable, Sendable {
    public let id: String
    public let name: String
    public let description: String
    public let iconURL: String?

    public init(id: String, name: String, description: String, iconURL: String? = nil) {
        self.id = id
        self.name = name
        self.description = description
        self.iconURL = iconURL
    }
}
