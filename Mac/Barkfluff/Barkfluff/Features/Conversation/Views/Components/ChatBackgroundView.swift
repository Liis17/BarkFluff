//
//  ChatBackgroundView.swift
//  Barkfluff
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
                Color(nsColor: .windowBackgroundColor)
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
                        Color(nsColor: .windowBackgroundColor)
                    }
                )
                // Явно зажимаем картинку в фрейм родителя, иначе её
                // intrinsic size может «вытянуть» родительский ZStack.
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .blur(
                    radius: settings.backgroundBlurEnabled
                        ? CGFloat(settings.backgroundBlurRadius)
                        : 0,
                    opaque: true
                )
                .clipped()
            }

            // Затемняющий оверлей цветом фона окна (адаптируется к теме).
            if settings.backgroundDimPercent > 0 {
                Color(nsColor: .windowBackgroundColor)
                    .opacity(Double(settings.backgroundDimPercent) / 100.0)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .clipped()
        .ignoresSafeArea()
    }
}
