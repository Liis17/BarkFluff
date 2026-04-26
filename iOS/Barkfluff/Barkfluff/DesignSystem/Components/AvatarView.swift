//
//  AvatarView.swift
//  Barkfluff
//
//  Аватар пользователя (iOS версия)
//

import SwiftUI
import BFCore
import NukeUI
import Nuke

struct AvatarView: View {
    let imageURL: String?
    let initials: String
    let size: CGFloat
    var isOnline: Bool = false
    var showOnlineIndicator: Bool = false

    @Environment(DependencyContainer.self) private var container

    @State private var resolvedURL: URL?
    @State private var fileID: String?

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
                if let url = resolvedURL {
                    let request: ImageRequest = {
                        if let id = fileID {
                            return ImageRequest(url: url, userInfo: [.imageIdKey: id])
                        }
                        return ImageRequest(url: url)
                    }()
                    LazyImage(request: request) { state in
                        if let image = state.image {
                            image
                                .resizable()
                                .aspectRatio(contentMode: .fill)
                        } else {
                            placeholderView
                        }
                    }
                    .pipeline(container.imagePipeline)
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
                            .strokeBorder(Color(uiColor: .systemBackground), lineWidth: indicatorBorderWidth)
                    )
                    .offset(x: 1, y: 1)
                    .animation(.easeInOut(duration: 0.2), value: isOnline)
            }
        }
        .task(id: imageURL) {
            await resolveURL()
        }
    }

    private func resolveURL() async {
        guard let imageURL = imageURL, !imageURL.isEmpty else {
            resolvedURL = nil
            fileID = nil
            return
        }

        // Если это валидный URL — используем напрямую
        if let url = URL(string: imageURL), url.scheme != nil {
            resolvedURL = url
            fileID = nil
            return
        }

        // Иначе это fileID — получаем URL через fileService (использует FileURLCache)
        do {
            let urlString = try await container.fileService.getDownloadURL(fileID: imageURL)
            resolvedURL = URL(string: urlString)
            fileID = imageURL
        } catch {
            resolvedURL = nil
            fileID = nil
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
    .environment(DependencyContainer())
}
