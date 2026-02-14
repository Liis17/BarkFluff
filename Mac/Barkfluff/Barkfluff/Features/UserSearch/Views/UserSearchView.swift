//
//  UserSearchView.swift
//  Barkfluff
//
//  Поиск пользователей
//

import SwiftUI
import BFCore

struct UserSearchView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var searchQuery: String = ""
    @State private var users: [User] = []
    @State private var isLoading: Bool = false

    var onSelectUser: ((User) -> Void)?

    var body: some View {
        NavigationStack {
            List {
                if searchQuery.isEmpty {
                    ContentUnavailableView(
                        "Поиск пользователей",
                        systemImage: "person.magnifyingglass",
                        description: Text("Введите имя или username для поиска")
                    )
                } else if isLoading {
                    HStack {
                        Spacer()
                        ProgressView()
                        Spacer()
                    }
                } else if users.isEmpty {
                    ContentUnavailableView(
                        "Ничего не найдено",
                        systemImage: "person.slash",
                        description: Text("Попробуйте изменить запрос")
                    )
                } else {
                    ForEach(users) { user in
                        Button {
                            onSelectUser?(user)
                            dismiss()
                        } label: {
                            UserSearchRow(user: user)
                        }
                        .buttonStyle(.plain)
                    }
                }
            }
            .searchable(text: $searchQuery, prompt: "Имя или username")
            .onChange(of: searchQuery) { _, newValue in
                Task {
                    await searchUsers(query: newValue)
                }
            }
            .navigationTitle("Поиск")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Отмена") {
                        dismiss()
                    }
                }
            }
        }
    }

    private func searchUsers(query: String) async {
        // TODO: Реализовать через UserService
        isLoading = true
        defer { isLoading = false }

        // Имитация задержки
        try? await Task.sleep(for: .milliseconds(500))

        // Заглушка
        if !query.isEmpty {
            users = []
        }
    }
}

struct UserSearchRow: View {
    let user: User

    var body: some View {
        HStack(spacing: 12) {
            // Аватар
            Circle()
                .fill(Color.accentColor.opacity(0.2))
                .frame(width: 44, height: 44)
                .overlay {
                    Text(user.initials)
                        .font(.headline)
                        .foregroundStyle(.secondary)
                }

            VStack(alignment: .leading, spacing: 2) {
                Text(user.displayName)
                    .font(.headline)
                Text("@\(user.username)")
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }

            Spacer()
        }
        .padding(.vertical, 4)
    }
}

#Preview {
    UserSearchView()
}
