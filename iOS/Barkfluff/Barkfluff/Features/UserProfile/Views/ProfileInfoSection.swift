//
//  ProfileInfoSection.swift
//  Barkfluff (iOS)
//
//  Секция с информацией о пользователе: bio, баджи, дата регистрации,
//  ID пользователя и чата (видимость управляется DeveloperSettings).
//  Тап по ID копирует значение в буфер обмена.
//

import SwiftUI
import UIKit
import BFCore

struct ProfileInfoSection: View {
    @Environment(DependencyContainer.self) private var container
    @Bindable var viewModel: UserProfilePanelViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let bio = viewModel.bio, !bio.isEmpty {
                infoRow(title: "О себе", value: bio, copyable: false)
            }

            if !viewModel.badges.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Награды")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    BadgesRowView(badges: viewModel.badges, showNames: true)
                }
            }

            if let date = viewModel.registrationDate {
                infoRow(title: "Зарегистрирован", value: dateString(date), copyable: false)
            }

            if container.developerSettings.showUserIDs, let userID = viewModel.userID {
                infoRow(title: "ID пользователя", value: "\(userID)", copyable: true)
            }

            if container.developerSettings.showChatIDs {
                infoRow(title: "ID чата", value: viewModel.chatID, copyable: true)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(16)
        .background(Color(uiColor: .secondarySystemGroupedBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .padding(.horizontal, 16)
    }

    @ViewBuilder
    private func infoRow(title: String, value: String, copyable: Bool) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(.caption)
                .foregroundStyle(.secondary)

            if copyable {
                Button {
                    copyToPasteboard(value)
                } label: {
                    Text(value)
                        .font(.body)
                        .foregroundStyle(.primary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                .buttonStyle(.plain)
                .contentShape(Rectangle())
                .contextMenu {
                    Button {
                        copyToPasteboard(value)
                    } label: {
                        Label("Скопировать", systemImage: "doc.on.doc")
                    }
                }
            } else {
                Text(value)
                    .font(.body)
                    .textSelection(.enabled)
            }
        }
    }

    private func copyToPasteboard(_ value: String) {
        UIPasteboard.general.string = value
        UINotificationFeedbackGenerator().notificationOccurred(.success)
    }

    private func dateString(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "ru_RU")
        formatter.dateStyle = .medium
        return formatter.string(from: date)
    }
}
