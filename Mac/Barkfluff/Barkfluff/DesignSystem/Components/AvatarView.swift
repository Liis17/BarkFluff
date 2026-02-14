//
//  AvatarView.swift
//  Barkfluff
//
//  Аватар пользователя
//

import SwiftUI
import Nuke
import NukeUI

struct AvatarView: View {
    let imageURL: String?
    let initials: String
    let size: CGFloat

    init(imageURL: String? = nil, initials: String, size: CGFloat = 40) {
        self.imageURL = imageURL
        self.initials = initials
        self.size = size
    }

    var body: some View {
        Group {
            if let urlString = imageURL, let url = URL(string: urlString) {
                LazyImage(url: url) { state in
                    if let image = state.image {
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    } else if state.isLoading {
                        placeholderView
                    } else {
                        placeholderView
                    }
                }
                .processors([.resize(size: CGSize(width: size * 2, height: size * 2))])
            } else {
                placeholderView
            }
        }
        .frame(width: size, height: size)
        .clipShape(Circle())
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
}

#Preview {
    HStack(spacing: 16) {
        AvatarView(initials: "ИИ", size: 40)
        AvatarView(initials: "АБ", size: 60)
        AvatarView(initials: "ВГ", size: 80)
    }
    .padding()
}
