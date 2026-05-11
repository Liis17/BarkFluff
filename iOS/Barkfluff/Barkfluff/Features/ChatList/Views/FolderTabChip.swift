//
//  FolderTabChip.swift
//  Barkfluff (iOS)
//
//  Чип-таб папки чатов над списком чатов.
//

import SwiftUI
import BFCore

struct FolderTabChip: View {
    let icon: String
    let title: String
    let unreadCount: Int
    let isSelected: Bool
    let compact: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 6) {
                if !icon.isEmpty {
                    Text(icon).font(.system(size: 16))
                } else {
                    Image(systemName: "tray.full.fill")
                        .font(.system(size: 14, weight: .semibold))
                }

                if !compact {
                    Text(title)
                        .font(.system(size: 14, weight: isSelected ? .semibold : .regular))
                        .lineLimit(1)
                }

                if unreadCount > 0 {
                    Text("\(unreadCount)")
                        .font(.system(size: 11, weight: .semibold))
                        .foregroundStyle(.white)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(.red, in: Capsule())
                }
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(
                Capsule()
                    .fill(isSelected ? Color.accentColor.opacity(0.18) : Color.gray.opacity(0.12))
            )
            .foregroundStyle(isSelected ? Color.accentColor : Color.primary)
        }
        .buttonStyle(.plain)
    }
}

struct ChatFolderTabsBar: View {
    let folders: [ChatFolder]
    let selectedFolderID: String?
    let allChatsUnread: Int
    let unreadByFolder: (ChatFolder) -> Int
    let compact: Bool
    let onSelect: (String?) -> Void

    var body: some View {
        if folders.isEmpty {
            EmptyView()
        } else {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    FolderTabChip(
                        icon: "",
                        title: "Все чаты",
                        unreadCount: allChatsUnread,
                        isSelected: selectedFolderID == nil,
                        compact: compact,
                        action: { onSelect(nil) }
                    )
                    ForEach(folders) { folder in
                        FolderTabChip(
                            icon: folder.icon,
                            title: folder.name.isEmpty ? "Папка" : folder.name,
                            unreadCount: unreadByFolder(folder),
                            isSelected: selectedFolderID == folder.id,
                            compact: compact,
                            action: { onSelect(folder.id) }
                        )
                    }
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
            }
            .background(.ultraThinMaterial)
        }
    }
}
