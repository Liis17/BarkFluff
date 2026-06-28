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
    @Environment(\.locale) private var locale
    @Bindable var viewModel: UserProfilePanelViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let bio = viewModel.bio, !bio.isEmpty {
                infoRow(titleKey: "user_profile.info.bio", value: bio, copyable: false)
            }

            if !viewModel.badges.isEmpty {
                VStack(alignment: .leading, spacing: 6) {
                    Text("user_profile.info.badges")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                    BadgesRowView(badges: viewModel.badges, showNames: true)
                }
            }

            if let date = viewModel.registrationDate {
                infoRow(titleKey: "user_profile.info.registered", value: dateString(date), copyable: false)
            }

            if container.developerSettings.showUserIDs, let userID = viewModel.userID {
                infoRow(titleKey: "user_profile.info.user_id", value: "\(userID)", copyable: true)
            }

            if container.developerSettings.showChatIDs {
                infoRow(titleKey: "user_profile.info.chat_id", value: viewModel.chatID, copyable: true)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(16)
        .background(Color(uiColor: .secondarySystemGroupedBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .padding(.horizontal, 16)
    }

    @ViewBuilder
    private func infoRow(titleKey: LocalizedStringKey, value: String, copyable: Bool) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(titleKey)
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
                        Label("user_profile.copy", systemImage: "doc.on.doc")
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
        formatter.locale = locale
        formatter.dateStyle = .medium
        return formatter.string(from: date)
    }
}
