//
//  ImageCropperView.swift
//  Barkfluff (macOS)
//
//  Универсальный кропер картинки с заданным aspectRatio.
//  Чистый SwiftUI: MagnifyGesture (pinch на трекпаде) + DragGesture (pan)
//  поверх картинки, затемнение с прозрачным окном кропа. Картинка
//  всегда покрывает окно (минимальный масштаб подобран так), а offset
//  ограничен, чтобы край картинки не выезжал внутрь окна.
//
//  На «Готово» возвращает NSImage размером outputWidth × outputWidth/aspectRatio.
//

import SwiftUI
import AppKit

struct ImageCropperView: View {
    let image: NSImage
    /// Соотношение ширины к высоте окна кропа: 1.0 — квадрат, 3.0 — 3:1.
    let aspectRatio: CGFloat
    /// Ширина итогового NSImage. Высота = outputWidth / aspectRatio.
    let outputWidth: CGFloat
    let onCancel: () -> Void
    let onCrop: (NSImage) -> Void

    @State private var scale: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var lastScale: CGFloat = 1
    @State private var lastOffset: CGSize = .zero
    @State private var didInitialize: Bool = false

    // Текущий размер окна кропа в screen-pt. Запоминаем в onAppear/onChange
    // GeometryReader, чтобы performCrop из toolbar не зависел от GeometryReader.
    @State private var cropSize: CGSize = .zero
    @State private var minScale: CGFloat = 1
    @State private var maxScale: CGFloat = 5

    private var outputSize: CGSize {
        CGSize(width: outputWidth, height: outputWidth / aspectRatio)
    }

    var body: some View {
        VStack(spacing: 0) {
            toolbar

            GeometryReader { geo in
                let metrics = makeMetrics(in: geo.size)

                ZStack {
                    Color.black

                    Image(nsImage: image)
                        .resizable()
                        .interpolation(.high)
                        .frame(width: image.size.width, height: image.size.height)
                        .scaleEffect(scale, anchor: .center)
                        .offset(offset)

                    overlay(metrics: metrics)
                        .allowsHitTesting(false)
                }
                .contentShape(Rectangle())
                .gesture(dragGesture())
                .simultaneousGesture(magnifyGesture())
                .onTapGesture(count: 2) { handleDoubleTap() }
                .onAppear { apply(metrics: metrics) }
                .onChange(of: geo.size) { _, _ in apply(metrics: metrics) }
            }
        }
        .background(Color.black)
        .frame(minWidth: 720, minHeight: 540)
    }

    // MARK: - Subviews

    private var toolbar: some View {
        HStack {
            Button("Отмена") { onCancel() }
                .keyboardShortcut(.cancelAction)

            Spacer()

            Text("Обрезать фото")
                .font(.headline)
                .foregroundStyle(.white)

            Spacer()

            Button("Готово") { performCrop() }
                .keyboardShortcut(.defaultAction)
                .buttonStyle(.borderedProminent)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .background(Color.black)
        .foregroundStyle(.white)
    }

    @ViewBuilder
    private func overlay(metrics: Metrics) -> some View {
        ZStack {
            Path { path in
                path.addRect(metrics.containerBounds)
                path.addRect(metrics.rect)
            }
            .fill(Color.black.opacity(0.55), style: FillStyle(eoFill: true))

            Rectangle()
                .stroke(Color.white.opacity(0.9), lineWidth: 1)
                .frame(width: metrics.rect.width, height: metrics.rect.height)
                .position(x: metrics.rect.midX, y: metrics.rect.midY)
        }
    }

    // MARK: - Layout helpers

    private struct Metrics {
        let containerSize: CGSize
        let rect: CGRect
        let minScale: CGFloat
        let maxScale: CGFloat

        var containerBounds: CGRect { CGRect(origin: .zero, size: containerSize) }
    }

    private func makeMetrics(in size: CGSize) -> Metrics {
        let padding: CGFloat = 24
        let maxWidth = max(0, size.width - padding * 2)
        let maxHeight = max(0, size.height - padding * 2)
        let cropWidth = min(maxWidth, maxHeight * aspectRatio)
        let cropHeight = cropWidth / aspectRatio
        let rect = CGRect(
            x: (size.width - cropWidth) / 2,
            y: (size.height - cropHeight) / 2,
            width: cropWidth,
            height: cropHeight
        )

        let imgW = max(image.size.width, 1)
        let imgH = max(image.size.height, 1)
        let minScale = max(cropWidth / imgW, cropHeight / imgH)
        let maxScale = max(minScale * 5, 5)

        return Metrics(containerSize: size, rect: rect, minScale: minScale, maxScale: maxScale)
    }

    /// Сохраняет метрики в state и при первом вызове выставляет scale = minScale,
    /// offset = .zero; при ресайзе — переклампливает текущие значения.
    private func apply(metrics: Metrics) {
        cropSize = metrics.rect.size
        minScale = metrics.minScale
        maxScale = metrics.maxScale

        if !didInitialize {
            didInitialize = true
            scale = metrics.minScale
            offset = .zero
            lastScale = scale
            lastOffset = .zero
        } else {
            let clampedScale = min(max(scale, metrics.minScale), metrics.maxScale)
            let clampedOffset = clampOffset(offset, scale: clampedScale, cropSize: metrics.rect.size)
            scale = clampedScale
            offset = clampedOffset
            lastScale = clampedScale
            lastOffset = clampedOffset
        }
    }

    private func clampOffset(_ proposed: CGSize, scale: CGFloat, cropSize: CGSize) -> CGSize {
        let maxX = max(0, (image.size.width * scale - cropSize.width) / 2)
        let maxY = max(0, (image.size.height * scale - cropSize.height) / 2)
        return CGSize(
            width: min(max(proposed.width, -maxX), maxX),
            height: min(max(proposed.height, -maxY), maxY)
        )
    }

    // MARK: - Gestures

    private func dragGesture() -> some Gesture {
        DragGesture()
            .onChanged { value in
                let proposed = CGSize(
                    width: lastOffset.width + value.translation.width,
                    height: lastOffset.height + value.translation.height
                )
                offset = clampOffset(proposed, scale: scale, cropSize: cropSize)
            }
            .onEnded { _ in
                lastOffset = offset
            }
    }

    private func magnifyGesture() -> some Gesture {
        MagnifyGesture()
            .onChanged { value in
                let proposed = lastScale * value.magnification
                let newScale = min(max(proposed, minScale), maxScale)
                scale = newScale
                offset = clampOffset(offset, scale: newScale, cropSize: cropSize)
            }
            .onEnded { _ in
                lastScale = scale
                lastOffset = offset
            }
    }

    private func handleDoubleTap() {
        let target: CGFloat = scale > minScale + 0.01
            ? minScale
            : min(minScale * 2.5, maxScale)
        withAnimation(.easeInOut(duration: 0.2)) {
            scale = target
            offset = clampOffset(offset, scale: target, cropSize: cropSize)
        }
        lastScale = target
        lastOffset = offset
    }

    // MARK: - Crop

    private func performCrop() {
        let imgW = image.size.width
        let imgH = image.size.height
        guard imgW > 0, imgH > 0, scale > 0, cropSize.width > 0, cropSize.height > 0 else {
            onCrop(image)
            return
        }

        // Картинка и окно кропа центрированы в ZStack: после offset центр картинки = центр окна + offset.
        // В системе координат картинки (logical NSImage points):
        //   srcOrigin.x = imgW/2 − offset.x/scale − (cropW/scale)/2
        //   srcOrigin.y = imgH/2 − offset.y/scale − (cropH/scale)/2
        //   srcSize = cropSize / scale
        let srcRect = CGRect(
            x: imgW / 2 - offset.width / scale - cropSize.width / (2 * scale),
            y: imgH / 2 - offset.height / scale - cropSize.height / (2 * scale),
            width: cropSize.width / scale,
            height: cropSize.height / scale
        )

        let result = NSImage(size: outputSize)
        result.lockFocus()
        NSGraphicsContext.current?.imageInterpolation = .high
        image.draw(
            in: CGRect(origin: .zero, size: outputSize),
            from: srcRect,
            operation: .copy,
            fraction: 1.0
        )
        result.unlockFocus()
        onCrop(result)
    }
}
