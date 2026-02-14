//
//  ProfileTabIcon.swift
//  Barkfluff
//
//  Иконка профиля в панели вкладок сайдбара
//

import SwiftUI
import NukeUI

/// Иконка профиля для панели вкладок
struct ProfileTabIcon: View {
    let avatarURL: String?
    let initials: String
    let isActive: Bool

    var body: some View {
        Group {
            if let url = avatarURL, let imageURL = URL(string: url) {
                LazyImage(url: imageURL) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    } else {
                        initialsView
                    }
                }
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
