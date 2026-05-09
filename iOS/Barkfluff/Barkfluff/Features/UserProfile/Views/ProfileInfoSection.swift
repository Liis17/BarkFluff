//
//  ProfileInfoSection.swift
//  Barkfluff (iOS)
//
//  Секция с информацией о пользователе: bio, баджи, дата регистрации.
//

import SwiftUI
import BFCore

struct ProfileInfoSection: View {
    @Bindable var viewModel: UserProfilePanelViewModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            if let bio = viewModel.bio, !bio.isEmpty {
                infoRow(title: "О себе", value: bio)
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
                infoRow(title: "Зарегистрирован", value: dateString(date))
            }

            if let userID = viewModel.userID {
                infoRow(title: "ID", value: "\(userID)")
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(16)
        .background(Color(uiColor: .secondarySystemGroupedBackground))
        .clipShape(RoundedRectangle(cornerRadius: 12))
        .padding(.horizontal, 16)
    }

    private func infoRow(title: String, value: String) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title)
                .font(.caption)
                .foregroundStyle(.secondary)
            Text(value)
                .font(.body)
                .textSelection(.enabled)
        }
    }

    private func dateString(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "ru_RU")
        formatter.dateStyle = .medium
        return formatter.string(from: date)
    }
}
