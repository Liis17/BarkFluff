//
//  ProfileInfoSection.swift
//  Barkfluff
//
//  Секция информации: био, бейджи, дата регистрации
//

import SwiftUI
import BFCore

/// Информация профиля: био, бейджи, дата регистрации, лимит хранилища
struct ProfileInfoSection: View {
    let viewModel: UserProfilePanelViewModel

    // Форматтер даты с русской локалью
    private var russianDateFormatter: DateFormatter {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "ru_RU")
        formatter.dateStyle = .long
        return formatter
    }

    var body: some View {
        VStack(alignment: .leading, spacing: Theme.Spacing.md) {
            // О себе
            if let bio = viewModel.bio, !bio.isEmpty {
                VStack(alignment: .leading, spacing: Theme.Spacing.xxs) {
                    Label("О себе", systemImage: "text.quote")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    Text(bio)
                        .font(.body)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            // Бейджи
            if !viewModel.badges.isEmpty {
                VStack(alignment: .leading, spacing: Theme.Spacing.xs) {
                    Label("Значки", systemImage: "star.fill")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    FlowLayout(spacing: Theme.Spacing.xs) {
                        ForEach(viewModel.badges) { badge in
                            BadgeChipView(badge: badge)
                        }
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            // ID пользователя
            if let userID = viewModel.userID {
                VStack(alignment: .leading, spacing: Theme.Spacing.xxs) {
                    Label("ID пользователя", systemImage: "number")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    Text(String(userID))
                        .font(.body)
                        .textSelection(.enabled)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            // Дата регистрации
            if let registrationDate = viewModel.registrationDate {
                VStack(alignment: .leading, spacing: Theme.Spacing.xxs) {
                    Label("Дата регистрации", systemImage: "calendar")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    Text(russianDateFormatter.string(from: registrationDate))
                        .font(.body)
                        .foregroundStyle(.primary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            // Лимит хранилища
            if let storageLimitGB = viewModel.storageLimitGB, storageLimitGB > 0 {
                HStack(spacing: Theme.Spacing.xs) {
                    Label("Хранилище", systemImage: "externaldrive")
                        .font(.caption)
                        .foregroundStyle(.secondary)

                    Spacer()

                    Text("\(storageLimitGB) ГБ")
                        .font(.callout)
                        .foregroundStyle(.primary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, Theme.Spacing.md)
        .padding(.vertical, Theme.Spacing.md)
    }
}

// MARK: - Badge Chip View

/// Чип баджа — иконка + название
struct BadgeChipView: View {
    let badge: UserBadge

    @State private var isHovering = false

    var body: some View {
        HStack(spacing: 6) {
            // Иконка баджа
            if let imageURL = badge.iconURL, let url = URL(string: imageURL) {
                AsyncImage(url: url) { phase in
                    switch phase {
                    case .success(let image):
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fit)
                            .frame(width: 18, height: 18)
                    default:
                        Image(systemName: "star.fill")
                            .font(.caption)
                            .foregroundStyle(.yellow)
                    }
                }
                .frame(width: 18, height: 18)
            } else {
                Image(systemName: "star.fill")
                    .font(.caption)
                    .foregroundStyle(.yellow)
            }

            Text(badge.name)
                .font(.caption)
                .lineLimit(1)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background {
            RoundedRectangle(cornerRadius: 12)
                .fill(.ultraThinMaterial)
        }
        .overlay {
            RoundedRectangle(cornerRadius: 12)
                .strokeBorder(Color.primary.opacity(0.1), lineWidth: 0.5)
        }
        .onHover { hovering in
            isHovering = hovering
        }
        .help(badge.description)
    }
}

#Preview {
    let container = DependencyContainer()
    let chat = Chat(id: "1", title: "Тест", isGroupChat: false, members: [])

    ProfileInfoSection(
        viewModel: UserProfilePanelViewModel(
            chat: chat,
            userService: container.userService,
            chatService: container.chatService,
            sharedMediaService: container.sharedMediaService,
            fileService: container.fileService
        )
    )
    .padding()
    .frame(width: 300)
}
