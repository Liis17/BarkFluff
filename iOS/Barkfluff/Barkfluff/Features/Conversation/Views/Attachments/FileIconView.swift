//
//  FileIconView.swift
//  Barkfluff
//
//  Иконка файла по расширению (iOS версия)
//

import SwiftUI

/// Иконка файла по расширению
struct FileIconView: View {
    let fileExtension: String
    var size: CGFloat = 40

    var body: some View {
        ZStack {
            // Background
            RoundedRectangle(cornerRadius: Theme.Radius.sm)
                .fill(iconColor.opacity(0.15))
                .frame(width: size, height: size)

            // Icon
            Image(systemName: iconName)
                .font(.system(size: size * 0.5))
                .foregroundStyle(iconColor)
        }
    }

    // MARK: - Icon Selection

    private var iconName: String {
        switch fileExtension.lowercased() {
        // Documents
        case "pdf":
            return "doc.richtext"
        case "doc", "docx":
            return "doc.wordprocessor"
        case "xls", "xlsx":
            return "doc.chart"
        case "ppt", "pptx":
            return "doc.slides"
        case "txt", "rtf":
            return "doc.plaintext"

        // Code
        case "swift", "kt", "java", "py", "js", "ts", "cpp", "c", "h", "cs", "go", "rs", "rb":
            return "chevron.left.forwardslash.chevron.right"

        // Archives
        case "zip", "rar", "7z", "tar", "gz":
            return "doc.zipper"

        // Audio
        case "mp3", "wav", "m4a", "aac", "flac", "ogg":
            return "music.note"

        // Video
        case "mp4", "mov", "avi", "mkv", "webm":
            return "video"

        // Images
        case "jpg", "jpeg", "png", "gif", "heic", "webp", "bmp", "tiff":
            return "photo"

        // Default
        default:
            return "doc"
        }
    }

    private var iconColor: Color {
        switch fileExtension.lowercased() {
        // Documents
        case "pdf":
            return .red
        case "doc", "docx":
            return .blue
        case "xls", "xlsx":
            return .green
        case "ppt", "pptx":
            return .orange

        // Code
        case "swift":
            return .orange
        case "kt", "java":
            return .purple
        case "py":
            return .blue
        case "js", "ts":
            return .yellow
        case "cpp", "c", "h":
            return .blue
        case "cs":
            return .purple
        case "go":
            return .cyan
        case "rs":
            return .orange
        case "rb":
            return .red

        // Archives
        case "zip", "rar", "7z", "tar", "gz":
            return .brown

        // Audio
        case "mp3", "wav", "m4a", "aac", "flac", "ogg":
            return .pink

        // Video
        case "mp4", "mov", "avi", "mkv", "webm":
            return .purple

        // Images
        case "jpg", "jpeg", "png", "gif", "heic", "webp", "bmp", "tiff":
            return .teal

        // Default
        default:
            return .gray
        }
    }
}

#Preview {
    HStack(spacing: 16) {
        FileIconView(fileExtension: "pdf")
        FileIconView(fileExtension: "docx")
        FileIconView(fileExtension: "xlsx")
        FileIconView(fileExtension: "zip")
        FileIconView(fileExtension: "swift")
        FileIconView(fileExtension: "mp3")
        FileIconView(fileExtension: "unknown")
    }
    .padding()
}
