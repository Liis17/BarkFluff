//
//  ProfileTabIcon.swift
//  Barkfluff
//
//  Иконка профиля в панели вкладок сайдбара
//

import SwiftUI
import NukeUI
import BFCore

/// Иконка профиля для панели вкладок
struct ProfileTabIcon: View {
    let avatarURL: String?
    let initials: String
    let isActive: Bool

    var body: some View {
        Group {
            if let url = avatarURL,
               let fileID = S3URLParser.fileID(from: url) {
                CachedImageView(
                    fileID: fileID,
                    type: .avatar,
                    presignedURLHint: url,
                    content: { image in
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    },
                    placeholder: { initialsView }
                )
            } else {
                initialsView
            }
        }
        .frame(width: 22, height: 22)
        .clipShape(Circle())
        .overlay {
            // Кольцо при активном состоянии
            if isActive {
                Circle()
                    .stroke(Color.accentColor, lineWidth: 1.5)
            }
        }
        .opacity(isActive ? 1.0 : 0.6)
    }

    private var initialsView: some View {
        ZStack {
            Circle()
                .fill(Color.accentColor.opacity(0.3))
            Text(initials)
                .font(.system(size: 10, weight: .medium))
                .foregroundStyle(.primary)
        }
    }
}

#Preview {
    HStack(spacing: 20) {
        ProfileTabIcon(avatarURL: nil, initials: "ИИ", isActive: false)
        ProfileTabIcon(avatarURL: nil, initials: "ИИ", isActive: true)
    }
    .padding()
}
