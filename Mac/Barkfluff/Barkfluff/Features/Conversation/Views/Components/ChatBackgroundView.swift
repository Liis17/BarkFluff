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

        // Color.clear имеет нулевой intrinsic size — это гарантирует, что
        // ChatBackgroundView никогда не претендует на больший фрейм, чем ему
        // предложили (иначе intrinsic size картинки внутри ZStack может
        // растянуть родителя и вытолкнуть инпут/шапку за пределы экрана).
        Color.clear
            .overlay {
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
                    .blur(
                        radius: settings.backgroundBlurEnabled
                            ? CGFloat(settings.backgroundBlurRadius)
                            : 0,
                        opaque: true
                    )
                }
            }
            .overlay {
                if settings.backgroundDimPercent > 0 {
                    Color(nsColor: .windowBackgroundColor)
                        .opacity(Double(settings.backgroundDimPercent) / 100.0)
                }
            }
            .clipped()
    }
}
