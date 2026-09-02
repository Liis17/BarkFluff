import SwiftUI
import BFCore

/// SwiftUI renderer for the shared BarkFluff message Markdown dialect.
public struct MarkdownMessageView: View {
    private let document: MarkdownDocument
    private let foreground: Color

    public init(source: String, foreground: Color = .primary) {
        document = MarkdownParser.parse(source)
        self.foreground = foreground
    }

    public var body: some View {
        Group {
            if let plainText = document.plainText {
                Text(verbatim: plainText)
                    .foregroundStyle(foreground)
                    .multilineTextAlignment(.leading)
            } else {
                VStack(alignment: .leading, spacing: 6) {
                    ForEach(document.blocks.indices, id: \.self) { index in
                        blockView(document.blocks[index])
                    }
                }
                .foregroundStyle(foreground)
            }
        }
        .textSelection(.enabled)
    }

    @ViewBuilder
    private func blockView(_ block: MarkdownBlock) -> some View {
        switch block {
        case let .text(group):
            MarkdownTextGroupView(group: group, foreground: foreground)
        case let .code(code):
            MarkdownCodeBlockView(code: code, foreground: foreground)
        case let .quote(lines):
            MarkdownQuoteView(lines: lines, foreground: foreground)
        case let .table(table):
            MarkdownTableView(table: table, foreground: foreground)
        case let .image(image):
            MarkdownImageView(image: image, foreground: foreground)
        }
    }
}

private struct MarkdownTextGroupView: View {
    let group: MarkdownTextGroup
    let foreground: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 3) {
            ForEach(group.lines.indices, id: \.self) { index in
                MarkdownLineView(
                    line: group.lines[index],
                    alignment: group.alignment,
                    foreground: foreground
                )
            }
        }
    }
}

private struct MarkdownLineView: View {
    let line: MarkdownLine
    let alignment: MarkdownAlignment
    let foreground: Color

    var body: some View {
        content
            .frame(maxWidth: .infinity, alignment: alignment.frameAlignment)
    }

    @ViewBuilder
    private var content: some View {
        switch line.kind {
        case .paragraph, .heading:
            MarkdownInlineText(
                inlines: line.inlines,
                foreground: foreground,
                textAlignment: alignment.textAlignment,
                headingLevel: line.kind == .heading ? line.headingLevel : nil
            )
        case .bullet:
            HStack(alignment: .firstTextBaseline, spacing: 5) {
                Text("•")
                    .foregroundStyle(foreground.opacity(0.8))
                MarkdownInlineText(
                    inlines: line.inlines,
                    foreground: foreground,
                    textAlignment: .leading
                )
            }
        case .ordered:
            HStack(alignment: .firstTextBaseline, spacing: 5) {
                Text("\(line.orderedMarker).")
                    .foregroundStyle(foreground.opacity(0.8))
                MarkdownInlineText(
                    inlines: line.inlines,
                    foreground: foreground,
                    textAlignment: .leading
                )
            }
        case .rule:
            Divider()
                .overlay(foreground.opacity(0.35))
        }
    }
}

private struct MarkdownInlineText: View {
    let inlines: [MarkdownInline]
    let foreground: Color
    let textAlignment: TextAlignment
    var headingLevel: Int?

    var body: some View {
        Text(attributedString)
            .foregroundStyle(foreground)
            .multilineTextAlignment(textAlignment)
            .font(headingFont)
    }

    private var headingFont: Font? {
        guard let headingLevel else { return nil }
        switch headingLevel {
        case 1: return .title2.weight(.bold)
        case 2: return .title3.weight(.bold)
        case 3: return .headline
        case 4: return .subheadline.weight(.semibold)
        default: return .body.weight(.semibold)
        }
    }

    private var attributedString: AttributedString {
        var result = AttributedString()

        for inline in inlines {
            var run = AttributedString(inline.text)
            run.foregroundColor = foreground

            if inline.style.contains(.code) {
                run.font = .system(.body, design: .monospaced)
                run.backgroundColor = foreground.opacity(0.12)
            } else {
                var font: Font?
                if inline.style.contains(.bold) || inline.style.contains(.italic) {
                    var styledFont = Font.body
                    if inline.style.contains(.bold) {
                        styledFont = styledFont.weight(.bold)
                    }
                    if inline.style.contains(.italic) {
                        styledFont = styledFont.italic()
                    }
                    font = styledFont
                } else if inline.style.contains(.small) {
                    font = .footnote
                    run.baselineOffset = -2
                }
                if let font {
                    run.font = font
                }
            }

            if inline.style.contains(.strikethrough) {
                run.strikethroughStyle = .single
            }

            if let linkURL = inline.linkURL, let url = URL(string: linkURL) {
                run.link = url
                run.underlineStyle = .single
            }

            result.append(run)
        }

        return result
    }
}

private struct MarkdownQuoteView: View {
    let lines: [MarkdownLine]
    let foreground: Color

    var body: some View {
        HStack(alignment: .top, spacing: 8) {
            RoundedRectangle(cornerRadius: 1.5)
                .fill(foreground.opacity(0.55))
                .frame(width: 3)

            VStack(alignment: .leading, spacing: 3) {
                ForEach(lines.indices, id: \.self) { index in
                    MarkdownLineView(
                        line: lines[index],
                        alignment: .start,
                        foreground: foreground.opacity(0.82)
                    )
                }
            }
        }
        .padding(.leading, 2)
    }
}

private struct MarkdownCodeBlockView: View {
    let code: String
    let foreground: Color

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            Text(verbatim: code.isEmpty ? " " : code)
                .font(.system(.callout, design: .monospaced))
                .foregroundStyle(foreground)
                .fixedSize(horizontal: true, vertical: false)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(8)
        }
        .background(
            foreground.opacity(0.10),
            in: RoundedRectangle(cornerRadius: 8)
        )
    }
}

private struct MarkdownTableView: View {
    let table: MarkdownTable
    let foreground: Color

    var body: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            Grid(horizontalSpacing: 0, verticalSpacing: 0) {
                GridRow {
                    ForEach(table.headers.indices, id: \.self) { index in
                        cellView(
                            table.headers[index],
                            alignment: alignment(at: index),
                            isHeader: true
                        )
                    }
                }

                ForEach(table.rows.indices, id: \.self) { rowIndex in
                    GridRow {
                        ForEach(table.rows[rowIndex].cells.indices, id: \.self) { columnIndex in
                            cellView(
                                table.rows[rowIndex].cells[columnIndex],
                                alignment: alignment(at: columnIndex),
                                isHeader: false
                            )
                        }
                    }
                }
            }
        }
    }

    private func alignment(at index: Int) -> MarkdownAlignment {
        guard index < table.alignments.count else { return .start }
        return table.alignments[index]
    }

    @ViewBuilder
    private func cellView(
        _ cell: MarkdownTableCell,
        alignment: MarkdownAlignment,
        isHeader: Bool
    ) -> some View {
        MarkdownInlineText(
            inlines: cell.inlines,
            foreground: foreground,
            textAlignment: alignment.textAlignment
        )
        .font(isHeader ? .callout.weight(.semibold) : .callout)
        .frame(minWidth: 90, alignment: alignment.frameAlignment)
        .padding(.horizontal, 8)
        .padding(.vertical, 6)
        .background(isHeader ? foreground.opacity(0.10) : .clear)
        .overlay(
            Rectangle()
                .stroke(foreground.opacity(0.16), lineWidth: 0.5)
        )
    }
}

private struct MarkdownImageView: View {
    let image: MarkdownImage
    let foreground: Color

    var body: some View {
        let width = image.width.map(CGFloat.init)
        let height = image.height.map(CGFloat.init)

        Group {
            if let source = image.url, let url = URL(string: source) {
                if let linkURL = image.linkURL, let destination = URL(string: linkURL) {
                    Link(destination: destination) {
                        remoteImage(url: url, fallback: image.alt)
                    }
                    .buttonStyle(.plain)
                } else {
                    remoteImage(url: url, fallback: image.alt)
                }
            } else {
                fallbackText
            }
        }
        .frame(width: width, height: height)
        .frame(maxWidth: .infinity, alignment: image.alignment.frameAlignment)
    }

    @ViewBuilder
    private func remoteImage(url: URL, fallback: String) -> some View {
        AsyncImage(url: url) { phase in
            switch phase {
            case let .success(image):
                image
                    .resizable()
                    .scaledToFit()
            case .empty:
                ProgressView()
                    .tint(foreground)
                    .frame(minWidth: 44, minHeight: 44)
            case .failure:
                fallbackTextValue(fallback)
            @unknown default:
                fallbackTextValue(fallback)
            }
        }
    }

    private var fallbackText: some View {
        fallbackTextValue(image.alt)
    }

    private func fallbackTextValue(_ value: String) -> some View {
        Text(value.isEmpty ? "🖼️" : value)
            .font(.footnote)
            .foregroundStyle(foreground.opacity(0.8))
            .padding(8)
            .background(foreground.opacity(0.08), in: RoundedRectangle(cornerRadius: 6))
    }
}

private extension MarkdownAlignment {
    var frameAlignment: Alignment {
        switch self {
        case .start: return .leading
        case .center: return .center
        case .end: return .trailing
        }
    }

    var textAlignment: TextAlignment {
        switch self {
        case .start: return .leading
        case .center: return .center
        case .end: return .trailing
        }
    }
}

#if DEBUG
struct MarkdownMessageView_Previews: PreviewProvider {
    static let sample = """
    ## Выполнен вход в твой аккаунт

    - **Устройство:** MacBook Air — Li_is
    - **ОС:** macOS 26.6.2
    - **Приложение:** BarkFluff macOS v1.0

    > **Если это не ты,** смени пароль и заверши другие сессии.

    ---

    **Жирный**, *курсив*, ~~зачёркнутый~~ и `код`

    [Сайт BarkFluff](https://example.com) и bare URL example.com

    ```swift
    let message = "Markdown V1"
    print(message)
    ```

    | Поле | Значение |
    | :--- | ---: |
    | IP | 91.197.3.120 |

    <p align="center">
    <strong>Безопасный HTML</strong>
    </p>
    <img src="https://placehold.co/320x80/png?text=BarkFluff" alt="Пример изображения">
    """

    static var previews: some View {
        Group {
            MarkdownMessageView(source: sample, foreground: .primary)
                .padding()
                .frame(width: 360)
                .previewDisplayName("Incoming")

            MarkdownMessageView(source: sample, foreground: .white)
                .padding()
                .background(Color.blue)
                .frame(width: 360)
                .previewDisplayName("Outgoing")
        }
    }
}
#endif
