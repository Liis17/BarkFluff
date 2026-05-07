//
//  UserMapper.swift
//  BFCore
//
//  Маппинг пользователя Proto ↔ Domain
//

import Foundation
import BFProto

/// Маппер для User
public enum UserMapper {

    /// Преобразовать Proto User в Domain User
    public static func toDomain(_ proto: Barkfluff_Users_User) -> User {
        User(
            id: proto.id,
            firstName: proto.firstName,
            lastName: proto.lastName,
            username: proto.username,
            bio: proto.bio.isEmpty ? nil : proto.bio,
            registrationDate: proto.registrationDate.date,
            profilePictureURL: proto.profilePicture.isEmpty ? nil : proto.profilePicture,
            profilePicturePreviewURL: proto.profilePicturePreview.isEmpty ? nil : proto.profilePicturePreview,
            profilePosterFileID: proto.profilePosterFileID.isEmpty ? nil : proto.profilePosterFileID,
            badges: proto.badges.map { toDomain($0) },
            storageLimitBytes: Int64(proto.storageLimitGb) * 1_073_741_824
        )
    }

    /// Преобразовать Proto UserBadge в Domain UserBadge
    public static func toDomain(_ proto: Barkfluff_Users_UserBadge) -> UserBadge {
        let badge = proto.badge
        return UserBadge(
            id: String(badge.id),
            name: badge.name,
            description: badge.description_p,
            iconURL: badge.imageURL.isEmpty ? nil : badge.imageURL
        )
    }
}
