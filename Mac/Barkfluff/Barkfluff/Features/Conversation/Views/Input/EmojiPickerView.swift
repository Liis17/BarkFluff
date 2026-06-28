//
//  EmojiPickerView.swift
//  Barkfluff
//
//  Попавер с выбором эмодзи: поиск, категории, сетка
//

import SwiftUI

struct EmojiPickerView: View {
    let onEmojiSelected: (String) -> Void

    @State private var searchText = ""
    @State private var selectedCategory: EmojiCategory = .smileys
    @State private var recentEmojis: [String] = {
        UserDefaults.standard.stringArray(forKey: "recentEmojis") ?? []
    }()

    private let columns = Array(repeating: GridItem(.fixed(36), spacing: 4), count: 8)
    private let maxRecent = 32

    var body: some View {
        VStack(spacing: 0) {
            // Поиск
            searchField

            Divider()

            // Полоса категорий
            categoryBar

            Divider()

            // Сетка эмодзи
            emojiGrid
        }
        .frame(width: 320, height: 400)
    }

    // MARK: - Search

    private var searchField: some View {
        HStack {
            Image(systemName: "magnifyingglass")
                .foregroundStyle(.secondary)
            TextField("conversation.emoji.search_placeholder", text: $searchText)
                .textFieldStyle(.plain)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    // MARK: - Categories

    private var categoryBar: some View {
        HStack(spacing: 2) {
            ForEach(EmojiCategory.allCases) { category in
                // Скрываем "Недавние" если пусто
                if category != .recent || !recentEmojis.isEmpty {
                    Button {
                        withAnimation(.easeInOut(duration: 0.15)) {
                            selectedCategory = category
                        }
                    } label: {
                        Image(systemName: category.icon)
                            .font(.system(size: 14))
                            .frame(width: 28, height: 28)
                            .foregroundStyle(selectedCategory == category ? .primary : .secondary)
                            .background(
                                RoundedRectangle(cornerRadius: 6)
                                    .fill(selectedCategory == category ? Color.accentColor.opacity(0.15) : .clear)
                            )
                    }
                    .buttonStyle(.plain)
                    .help(category.displayName)
                }
            }
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
    }

    // MARK: - Emoji Grid

    private var emojiGrid: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVGrid(columns: columns, spacing: 4) {
                    if searchText.isEmpty {
                        // Обычный режим — показываем выбранную категорию
                        if selectedCategory == .recent {
                            ForEach(recentEmojis, id: \.self) { emoji in
                                emojiButton(emoji)
                            }
                        } else {
                            ForEach(selectedCategory.emojis, id: \.self) { emoji in
                                emojiButton(emoji)
                            }
                        }
                    } else {
                        // Режим поиска — фильтруем по всем категориям
                        ForEach(filteredEmojis, id: \.self) { emoji in
                            emojiButton(emoji)
                        }
                    }
                }
                .padding(8)
            }
            .onChange(of: selectedCategory) {
                proxy.scrollTo(selectedCategory.emojis.first, anchor: .top)
            }
        }
    }

    private func emojiButton(_ emoji: String) -> some View {
        Button {
            onEmojiSelected(emoji)
            addToRecent(emoji)
        } label: {
            Text(emoji)
                .font(.system(size: 24))
                .frame(width: 36, height: 36)
        }
        .buttonStyle(.plain)
        .clipShape(RoundedRectangle(cornerRadius: 6))
        .contentShape(Rectangle())
    }

    // MARK: - Search Filter

    private var filteredEmojis: [String] {
        let query = searchText.lowercased()
        var result: [String] = []
        for category in EmojiCategory.allCases where category != .recent {
            result.append(contentsOf: category.emojis.filter { emoji in
                emoji.unicodeScalars.contains { scalar in
                    let name = scalar.properties.name?.lowercased() ?? ""
                    return name.contains(query)
                }
            })
        }
        return result
    }

    // MARK: - Recent

    private func addToRecent(_ emoji: String) {
        recentEmojis.removeAll { $0 == emoji }
        recentEmojis.insert(emoji, at: 0)
        if recentEmojis.count > maxRecent {
            recentEmojis = Array(recentEmojis.prefix(maxRecent))
        }
        UserDefaults.standard.set(recentEmojis, forKey: "recentEmojis")
    }
}
