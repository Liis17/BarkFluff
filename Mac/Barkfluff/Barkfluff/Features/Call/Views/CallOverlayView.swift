//
//  CallOverlayView.swift
//  Barkfluff (macOS)
//
//  Плавающий НЕмодальный оверлей звонка поверх чата. Не блокирует чат:
//  клики мимо карточки проходят к интерфейсу под ней. Сворачивается в
//  компактную плашку, разворачивается в полный экран звонка. Перетаскивается.
//

import SwiftUI
import BFCalls

struct CallOverlayView: View {
    let controller: CallController

    @State private var minimized = false
    @State private var dragOffset: CGSize = .zero
    @State private var committedOffset: CGSize = .zero

    var body: some View {
        Group {
            switch controller.phase {
            case .idle, .ended:
                EmptyView()

            case .incoming:
                // Карточка ринга по центру сверху. Без затемнения — чат остаётся доступен.
                IncomingCallView(controller: controller)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                    .padding(.top, 60)
                    .transition(.move(edge: .top).combined(with: .opacity))

            case .outgoing, .connecting, .active:
                floatingCard
                    .offset(x: committedOffset.width + dragOffset.width,
                            y: committedOffset.height + dragOffset.height)
                    .gesture(dragGesture)
                    .frame(maxWidth: .infinity, maxHeight: .infinity,
                           alignment: minimized ? .topTrailing : .center)
                    .padding(minimized ? 16 : 0)
            }
        }
        .animation(.easeInOut(duration: 0.2), value: controller.phase)
        .animation(.easeInOut(duration: 0.2), value: minimized)
    }

    @ViewBuilder
    private var floatingCard: some View {
        if minimized {
            CallMinimizedBar(controller: controller) { minimized = false }
        } else {
            CallScreenView(controller: controller, onMinimize: { minimized = true })
                .frame(width: 540, height: 640)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 20))
                .overlay(RoundedRectangle(cornerRadius: 20).strokeBorder(.white.opacity(0.1)))
                .shadow(radius: 24)
        }
    }

    private var dragGesture: some Gesture {
        DragGesture()
            .onChanged { dragOffset = $0.translation }
            .onEnded { value in
                committedOffset.width += value.translation.width
                committedOffset.height += value.translation.height
                dragOffset = .zero
            }
    }
}
