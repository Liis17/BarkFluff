//
//  AvatarView.swift
//  Barkfluff
//
//  Аватар пользователя
//

import SwiftUI
import Nuke
import NukeUI
import BFCore

struct AvatarView: View {
    let imageURL: String?
    let initials: String
    let size: CGFloat
    var isOnline: Bool = false
    var showOnlineIndicator: Bool = false

    init(imageURL: String? = nil, initials: String, size: CGFloat = 40, isOnline: Bool = false, showOnlineIndicator: Bool = false) {
        self.imageURL = imageURL
        self.initials = initials
        self.size = size
        self.isOnline = isOnline
        self.showOnlineIndicator = showOnlineIndicator
    }

    var body: some View {
        ZStack(alignment: .bottomTrailing) {
            Group {
                if let urlString = imageURL,
                   let fileID = S3URLParser.fileID(from: urlString) {
                    CachedImageView(
                        fileID: fileID,
                        type: .avatar,
                        presignedURLHint: urlString,
                        processors: [.resize(size: CGSize(width: size * 2, height: size * 2))],
                        content: { image in
                            image
                                .resizable()
                                .aspectRatio(contentMode: .fill)
                        },
                        placeholder: { placeholderView }
                    )
                } else {
                    placeholderView
                }
            }
            .frame(width: size, height: size)
            .clipShape(Circle())

            // Онлайн-индикатор
            if showOnlineIndicator, isOnline {
                Circle()
                    .fill(.green)
                    .frame(width: indicatorSize, height: indicatorSize)
                    .overlay(
                        Circle()
                            .strokeBorder(Color(nsColor: .windowBackgroundColor), lineWidth: indicatorBorderWidth)
                    )
                    .offset(x: 1, y: 1)
                    .animation(.easeInOut(duration: 0.2), value: isOnline)
            }
        }
    }

    private var placeholderView: some View {
        ZStack {
            Circle()
                .fill(initialsColor.opacity(0.2))
            Text(initials)
                .font(.system(size: size * 0.4, weight: .medium))
                .foregroundStyle(initialsColor)
        }
    }

    private var initialsColor: Color {
        let colors: [Color] = [
            .blue, .green, .orange, .purple, .pink, .teal, .indigo, .mint, .cyan, .brown
        ]
        let hash = initials.unicodeScalars.reduce(0) { $0 + Int($1.value) }
        return colors[abs(hash) % colors.count]
    }

    /// Размер индикатора пропорционален размеру аватарки
    private var indicatorSize: CGFloat {
        max(8, size * 0.25)
    }

    /// Толщина обводки индикатора
    private var indicatorBorderWidth: CGFloat {
        size > 40 ? 2.5 : 2
    }
}

#Preview {
    HStack(spacing: 16) {
        AvatarView(initials: "ИИ", size: 40)
        AvatarView(initials: "АБ", size: 60, isOnline: true, showOnlineIndicator: true)
        AvatarView(initials: "ВГ", size: 80, isOnline: true, showOnlineIndicator: true)
    }
    .padding()
}
