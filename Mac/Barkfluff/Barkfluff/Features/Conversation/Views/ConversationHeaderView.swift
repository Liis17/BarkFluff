//
//  ConversationHeaderView.swift
//  Barkfluff
//
//  Заголовок чата в стиле iMessage - аватарка по центру, имя под ней
//  Плавает над сообщениями с градиентным blur-эффектом
//

import SwiftUI
import BFCore

/// Заголовок чата - центрированный, плавает над сообщениями с градиентным blur
struct ConversationHeaderView: View {
    let chat: Chat

    @Environment(AppCoordinator.self) private var coordinator

    var body: some View {
        VStack(spacing: Theme.Spacing.xxs) {
            // Аватарка по центру — тоже кликабельная
            Button {
                coordinator.toggleProfilePanel(for: chat)
            } label: {
                AvatarView(
                    imageURL: chat.pictureURL,
                    initials: chat.avatarInitials,
                    size: 52
                )
            }
            .buttonStyle(.plain)

            // Имя под аватаркой со стрелкой
            Button {
                coordinator.toggleProfilePanel(for: chat)
            } label: {
                HStack(spacing: 4) {
                    Text(chat.title)
                        .font(.headline)
                        .lineLimit(1)

                    Image(systemName: "chevron.right")
                        .font(.caption2)
                        .fontWeight(.semibold)
                        .rotationEffect(coordinator.showProfilePanel ? .degrees(90) : .degrees(0))
                        .animation(.easeInOut(duration: 0.2), value: coordinator.showProfilePanel)
                }
                .foregroundStyle(.primary)
            }
            .buttonStyle(.plain)

            // Подзаголовок
            if chat.isGroupChat {
                Text("\(chat.members.count) участников")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            } else if let member = chat.members.first {
                Text("@\(member.username)")
                    .font(.caption2)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .padding(.top, 8)
        .padding(.bottom, 40)
        .frame(maxWidth: .infinity)
        .background {
            Rectangle()
                .fill(
                    LinearGradient(
                        gradient: Gradient(stops: [
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.85), location: 0),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.7), location: 0.3),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0.4), location: 0.6),
                            .init(color: Color(nsColor: .windowBackgroundColor).opacity(0), location: 1.0)
                        ]),
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )
        }
    }
}

#Preview {
    ZStack {
        // Имитация сообщений под заголовком
        ScrollView {
            VStack(spacing: 20) {
                ForEach(0..<20, id: \.self) { i in
                    HStack {
                        if i % 2 == 0 {
                            Text("Сообщение \(i)")
                                .padding()
                                .background(Color.gray.opacity(0.2))
                                .cornerRadius(16)
                            Spacer()
                        } else {
                            Spacer()
                            Text("Сообщение \(i)")
                                .padding()
                                .background(Color.blue)
                                .foregroundColor(.white)
                                .cornerRadius(16)
                        }
                    }
                    .padding(.horizontal)
                }
            }
            .padding(.top, 150)
        }

        // Заголовок сверху
        VStack {
            ConversationHeaderView(
                chat: Chat(
                    id: "1",
                    title: "Иван Иванов",
                    isGroupChat: false,
                    members: [
                        ChatMember(
                            userID: 2,
                            username: "ivan_ivanov",
                            firstName: "Иван",
                            lastName: "Иванов",
                            role: .member
                        )
                    ]
                )
            )
            Spacer()
        }
    }
    .frame(width: 400, height: 600)
}
