//
//  CallOverlayView.swift
//  Barkfluff (iOS)
//
//  Полноэкранный оверлей звонка поверх MainTabView. Работает только при
//  открытом приложении (нет VoIP/CallKit — см. RootView.scenePhase).
//

import SwiftUI
import BFCalls

struct CallOverlayView: View {
    let controller: CallController

    var body: some View {
        switch controller.phase {
        case .idle, .ended:
            EmptyView()

        case .incoming:
            ZStack {
                Color.black.opacity(0.45).ignoresSafeArea()
                IncomingCallView(controller: controller)
            }
            .transition(.opacity)

        case .outgoing, .connecting, .active:
            CallScreenView(controller: controller)
                .background(.regularMaterial)
                .ignoresSafeArea()
                .transition(.move(edge: .bottom))
        }
    }
}
