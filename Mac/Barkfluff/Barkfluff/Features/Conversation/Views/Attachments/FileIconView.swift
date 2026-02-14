//
//  FileIconView.swift
//  Barkfluff
//
//  Иконка файла по расширению
//

import SwiftUI

/// Иконка файла с цветом по расширению
public struct FileIconView: View {

    let fileName: String
    let size: CGFloat

    public init(fileName: String, size: CGFloat = 36) {
        self.fileName = fileName
        self.size = size
    }

    public var body: some View {
        ZStack {
            // Фон
            RoundedRectangle(cornerRadius: 8)
                .fill(iconColor.opacity(0.15))

            // Иконка
            Image(systemName: systemIcon)
                .font(.system(size: size * 0.5))
                .foregroundStyle(iconColor)
        }
        .frame(width: size, height: size)
    }

    // MARK: - Private

    private var fileExtension: String {
        (fileName as NSString).pathExtension.lowercased()
    }

    private var systemIcon: String {
        switch fileExtension {
        // Документы
        case "pdf":
            return "doc.text.fill"
        case "doc", "docx":
            return "doc.word.fill"
        case "xls", "xlsx":
            return "doc.chart.fill"
        case "ppt", "pptx":
            return "doc.richtext.fill"
        case "txt", "rtf":
            return "doc.text"

        // Архивы
        case "zip", "rar", "7z", "tar", "gz":
            return "doc.zipper"

        // Код
        case "swift", "kt", "java", "py", "js", "ts", "cpp", "c", "h", "cs", "go", "rb", "rs":
            return "chevron.left.forwardslash.chevron.right"

        // Аудио
        case "mp3", "wav", "aac", "flac", "m4a", "ogg":
            return "music.note"

        // Видео
        case "mp4", "mov", "avi", "mkv", "webm":
            return "video.fill"

        // Изображения
        case "jpg", "jpeg", "png", "gif", "webp", "heic", "bmp", "svg":
            return "photo.fill"

        // Данные
        case "json", "xml", "yaml", "yml":
            return "curlybraces"

        // Остальное
        default:
            return "doc.fill"
        }
    }

    private var iconColor: Color {
        switch fileExtension {
        // PDF - красный
        case "pdf":
            return .red

        // Word - синий
        case "doc", "docx":
            return .blue

        // Excel - зелёный
        case "xls", "xlsx":
            return .green

        // PowerPoint - оранжевый
        case "ppt", "pptx":
            return .orange

        // Архивы - фиолетовый
        case "zip", "rar", "7z", "tar", "gz":
            return .purple

        // Код - cyan
        case "swift", "kt", "java", "py", "js", "ts", "cpp", "c", "h", "cs", "go", "rb", "rs":
            return .cyan

        // Аудио - розовый
        case "mp3", "wav", "aac", "flac", "m4a", "ogg":
            return .pink

        // Видео - индиго
        case "mp4", "mov", "avi", "mkv", "webm":
            return .indigo

        // Изображения - teal
        case "jpg", "jpeg", "png", "gif", "webp", "heic", "bmp", "svg":
            return .teal

        // Данные - yellow
        case "json", "xml", "yaml", "yml":
            return .yellow

        // Остальное - серый
        default:
            return .gray
        }
    }
}

#Preview {
    HStack(spacing: 16) {
        FileIconView(fileName: "document.pdf")
        FileIconView(fileName: "report.docx")
        FileIconView(fileName: "data.xlsx")
        FileIconView(fileName: "archive.zip")
        FileIconView(fileName: "code.swift")
        FileIconView(fileName: "unknown.xyz")
    }
    .padding()
}
