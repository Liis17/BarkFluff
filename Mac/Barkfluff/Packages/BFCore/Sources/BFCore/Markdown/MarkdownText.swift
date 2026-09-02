import Foundation

/// Plain-text projection used by compact message surfaces.
public enum MarkdownText {
    public static func strip(_ source: String?) -> String {
        guard let source, !source.isEmpty else { return "" }

        let document = MarkdownParser.parse(source)
        var parts: [String] = []

        func append(_ value: String) {
            let withoutTags = value.replacingOccurrences(
                of: #"</?[a-z][^>]*>"#,
                with: " ",
                options: [.regularExpression, .caseInsensitive]
            )
            let withoutLinks = withoutTags.replacingOccurrences(
                of: #"\[([^\]]+)\]\(([^)\s]+)\)"#,
                with: "$1",
                options: .regularExpression
            )
            parts.append(withoutLinks)
        }

        for block in document.blocks {
            switch block {
            case let .text(group):
                for line in group.lines where line.kind != .rule {
                    append(line.inlines.map(\.text).joined())
                }
            case let .code(code):
                append(code)
            case let .quote(lines):
                for line in lines {
                    append(line.inlines.map(\.text).joined())
                }
            case let .table(table):
                for cell in table.headers {
                    append(cell.inlines.map(\.text).joined())
                }
                for row in table.rows {
                    for cell in row.cells {
                        append(cell.inlines.map(\.text).joined())
                    }
                }
            case let .image(image):
                append(image.alt)
            }
        }

        return parts
            .joined(separator: " ")
            .replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
