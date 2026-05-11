//
//  ImageCropperView.swift
//  Barkfluff (iOS)
//
//  Универсальный кропер картинки с заданным aspectRatio.
//  Pinch-zoom + pan через UIScrollView в UIViewControllerRepresentable,
//  поверх — затемнение с вырезанным окном кропа по центру.
//  На «Готово» возвращает UIImage размером outputWidth × outputWidth/aspectRatio
//  из выделенной области, картинка не может выезжать за пределы окна.
//

import SwiftUI
import UIKit

struct ImageCropperView: View {
    let image: UIImage
    /// Соотношение ширины к высоте окна кропа. 1.0 — квадрат (аватар),
    /// 3.0 — постер 3:1.
    let aspectRatio: CGFloat
    /// Ширина итогового UIImage. Высота вычисляется как outputWidth / aspectRatio.
    let outputWidth: CGFloat
    let onCancel: () -> Void
    let onCrop: (UIImage) -> Void

    private var outputSize: CGSize {
        CGSize(width: outputWidth, height: outputWidth / aspectRatio)
    }

    var body: some View {
        NavigationStack {
            GeometryReader { geo in
                let cropRect = makeCropRect(in: geo.size)

                ZStack {
                    Color.black.ignoresSafeArea()

                    CroppableScrollView(
                        image: image,
                        cropRect: cropRect,
                        containerSize: geo.size,
                        onCropReady: { cropped in
                            let resized = resize(cropped, to: outputSize)
                            onCrop(resized)
                        }
                    )

                    overlay(cropRect: cropRect, in: geo.size)
                        .allowsHitTesting(false)
                }
            }
            .navigationTitle("Обрезать фото")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Отмена") { onCancel() }
                        .foregroundStyle(.white)
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Готово") {
                        NotificationCenter.default.post(name: .imageCropperRequestCrop, object: nil)
                    }
                    .foregroundStyle(.white)
                    .bold()
                }
            }
            .toolbarBackground(.black, for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
            .toolbarColorScheme(.dark, for: .navigationBar)
        }
        .preferredColorScheme(.dark)
    }

    /// Вписать прямоугольник заданного aspectRatio в экранную область с отступом 16
    /// со всех сторон. Берём минимум по ширине и по высоте, чтобы окно гарантированно
    /// влезло на портретный/ландшафтный экран.
    private func makeCropRect(in size: CGSize) -> CGRect {
        let padding: CGFloat = 16
        let maxWidth = size.width - padding * 2
        let maxHeight = size.height - padding * 2
        let widthByHeight = maxHeight * aspectRatio
        let cropWidth = min(maxWidth, widthByHeight)
        let cropHeight = cropWidth / aspectRatio
        return CGRect(
            x: (size.width - cropWidth) / 2,
            y: (size.height - cropHeight) / 2,
            width: cropWidth,
            height: cropHeight
        )
    }

    @ViewBuilder
    private func overlay(cropRect: CGRect, in size: CGSize) -> some View {
        ZStack {
            // Затемнение с вырезанным окном кропа (even-odd fill).
            Path { path in
                path.addRect(CGRect(origin: .zero, size: size))
                path.addRect(cropRect)
            }
            .fill(Color.black.opacity(0.55), style: FillStyle(eoFill: true))

            // Белая рамка по окну кропа.
            Rectangle()
                .stroke(Color.white.opacity(0.9), lineWidth: 1)
                .frame(width: cropRect.width, height: cropRect.height)
                .position(x: cropRect.midX, y: cropRect.midY)
        }
    }

    private func resize(_ image: UIImage, to size: CGSize) -> UIImage {
        let renderer = UIGraphicsImageRenderer(size: size)
        return renderer.image { _ in
            image.draw(in: CGRect(origin: .zero, size: size))
        }
    }
}

extension Notification.Name {
    static let imageCropperRequestCrop = Notification.Name("com.barkfluff.imageCropper.requestCrop")
}

// MARK: - UIScrollView с зумом, который умеет отдавать обрезанный UIImage

private struct CroppableScrollView: UIViewControllerRepresentable {
    let image: UIImage
    let cropRect: CGRect
    let containerSize: CGSize
    let onCropReady: (UIImage) -> Void

    func makeUIViewController(context: Context) -> CroppableScrollViewController {
        let vc = CroppableScrollViewController(image: image, cropRect: cropRect, containerSize: containerSize)
        vc.onCropReady = onCropReady
        return vc
    }

    func updateUIViewController(_ uiViewController: CroppableScrollViewController, context: Context) {
        uiViewController.update(cropRect: cropRect, containerSize: containerSize)
    }
}

private final class CroppableScrollViewController: UIViewController, UIScrollViewDelegate {

    private let image: UIImage
    private var cropRect: CGRect
    private var containerSize: CGSize

    private let scrollView = UIScrollView()
    private let imageView: UIImageView
    private var observer: NSObjectProtocol?

    var onCropReady: ((UIImage) -> Void)?

    init(image: UIImage, cropRect: CGRect, containerSize: CGSize) {
        self.image = image
        self.cropRect = cropRect
        self.containerSize = containerSize
        self.imageView = UIImageView(image: image)
        super.init(nibName: nil, bundle: nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { nil }

    override func viewDidLoad() {
        super.viewDidLoad()

        view.backgroundColor = .clear

        scrollView.delegate = self
        scrollView.backgroundColor = .clear
        scrollView.showsHorizontalScrollIndicator = false
        scrollView.showsVerticalScrollIndicator = false
        scrollView.bouncesZoom = true
        scrollView.alwaysBounceVertical = true
        scrollView.alwaysBounceHorizontal = true
        scrollView.contentInsetAdjustmentBehavior = .never
        view.addSubview(scrollView)

        imageView.contentMode = .scaleAspectFit
        imageView.frame = CGRect(origin: .zero, size: image.size)
        scrollView.contentSize = image.size
        scrollView.addSubview(imageView)

        let doubleTap = UITapGestureRecognizer(target: self, action: #selector(handleDoubleTap(_:)))
        doubleTap.numberOfTapsRequired = 2
        scrollView.addGestureRecognizer(doubleTap)

        observer = NotificationCenter.default.addObserver(
            forName: .imageCropperRequestCrop,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            self?.performCrop()
        }
    }

    deinit {
        if let observer { NotificationCenter.default.removeObserver(observer) }
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        scrollView.frame = view.bounds
        configureZoomScales()
    }

    func update(cropRect: CGRect, containerSize: CGSize) {
        self.cropRect = cropRect
        self.containerSize = containerSize
        view.setNeedsLayout()
    }

    private func configureZoomScales() {
        let cropWidth = cropRect.width
        let cropHeight = cropRect.height
        guard cropWidth > 0, cropHeight > 0, image.size.width > 0, image.size.height > 0 else { return }

        // Минимальный зум: картинка должна как минимум полностью покрывать окно кропа
        // и по ширине, и по высоте — тогда невозможно «вытянуть» край окна за картинку.
        let minWidthScale = cropWidth / image.size.width
        let minHeightScale = cropHeight / image.size.height
        let minScale = max(minWidthScale, minHeightScale)

        scrollView.minimumZoomScale = minScale
        scrollView.maximumZoomScale = max(minScale * 4, 4)

        if scrollView.zoomScale < minScale {
            scrollView.zoomScale = minScale
        }

        // Инсеты так, чтобы можно было довести любой край картинки до центра окна кропа.
        let horizontalInset = cropRect.midX
        let verticalInset = cropRect.midY
        scrollView.contentInset = UIEdgeInsets(
            top: verticalInset - cropHeight / 2,
            left: horizontalInset - cropWidth / 2,
            bottom: verticalInset - cropHeight / 2,
            right: horizontalInset - cropWidth / 2
        )

        // Центрируем картинку под окном кропа.
        let contentWidth = image.size.width * scrollView.zoomScale
        let contentHeight = image.size.height * scrollView.zoomScale
        scrollView.contentOffset = CGPoint(
            x: (contentWidth - cropWidth) / 2 - scrollView.contentInset.left,
            y: (contentHeight - cropHeight) / 2 - scrollView.contentInset.top
        )
    }

    @objc private func handleDoubleTap(_ gesture: UITapGestureRecognizer) {
        if scrollView.zoomScale > scrollView.minimumZoomScale + 0.01 {
            scrollView.setZoomScale(scrollView.minimumZoomScale, animated: true)
        } else {
            let location = gesture.location(in: imageView)
            let zoomRect = CGRect(
                x: location.x - cropRect.width / 4,
                y: location.y - cropRect.height / 4,
                width: cropRect.width / 2,
                height: cropRect.height / 2
            )
            scrollView.zoom(to: zoomRect, animated: true)
        }
    }

    func viewForZooming(in scrollView: UIScrollView) -> UIView? { imageView }

    // MARK: - Crop

    private func performCrop() {
        let zoom = scrollView.zoomScale
        let visibleRect = CGRect(
            x: (scrollView.contentOffset.x + scrollView.contentInset.left) / zoom,
            y: (scrollView.contentOffset.y + scrollView.contentInset.top) / zoom,
            width: cropRect.width / zoom,
            height: cropRect.height / zoom
        )

        guard let cgImage = image.cgImage else {
            onCropReady?(image)
            return
        }

        // Учитываем ориентацию: visibleRect — в координатах UIImage,
        // переводим в координаты CGImage (там оси могут быть повёрнуты).
        let scale = image.scale
        let pixelRect = CGRect(
            x: visibleRect.origin.x * scale,
            y: visibleRect.origin.y * scale,
            width: visibleRect.width * scale,
            height: visibleRect.height * scale
        )

        let oriented = cgImage.cropping(to: pixelRect) ?? cgImage
        let cropped = UIImage(cgImage: oriented, scale: scale, orientation: image.imageOrientation)
        onCropReady?(cropped)
    }
}
