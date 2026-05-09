//
//  ChatBackgroundView.swift
//  Barkfluff (iOS)
//
//  Фон чата: картинка пользователя + опциональное размытие + затемнение.
//  Подключается как самый нижний слой ZStack в ConversationView.
//

import SwiftUI
import BFCore

struct ChatBackgroundView: View {
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        @Bindable var settings = container.personalizationSettings

        ZStack {
            if settings.currentBackgroundFileID.isEmpty {
                Color(uiColor: .systemBackground)
            } else {
                CachedImageView(
                    fileID: settings.currentBackgroundFileID,
                    type: .image,
                    content: { image in
                        image
                            .resizable()
                            .aspectRatio(contentMode: .fill)
                    },
                    placeholder: {
                        Color(uiColor: .systemBackground)
                    }
                )
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .blur(
                    radius: settings.backgroundBlurEnabled
                        ? CGFloat(settings.backgroundBlurRadius)
                        : 0,
                    opaque: true
                )
                .clipped()
            }

            if settings.backgroundDimPercent > 0 {
                Color(uiColor: .systemBackground)
                    .opacity(Double(settings.backgroundDimPercent) / 100.0)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .clipped()
    }
}
