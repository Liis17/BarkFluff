import Foundation

/// Parser for the Markdown dialect used by the Android and web clients.
public enum MarkdownParser {
    private static let topLevelDomains = "com|org|net|int|edu|gov|mil|info|biz|pro|name|xyz|online|site|tech|store|shop|cloud|app|dev|ai|io|co|me|tv|cc|su|ru|by|kz|ua|uk|de|fr|it|es|pl|nl|se|no|fi|cz|tr|cn|jp|kr|in|br|ca|au|eu|top|live|news|blog|art|fun|link|click|space|website|digital|agency|team|games|studio|design|software|group|media|world|life|zone|host|press|wiki|guru|expert"

    public static func parse(_ source: String?) -> MarkdownDocument {
        let normalized = normalizeLineEndings(source ?? "")
        guard !normalized.isEmpty else { return MarkdownDocument(blocks: []) }

        var lines = normalized.components(separatedBy: "\n")
        if lines.last == "" {
            lines.removeLast()
        }

        var blocks: [MarkdownBlock] = []
        var pendingLines: [MarkdownLine] = []
        var alignment: MarkdownAlignment = .start

        func flushText() {
            guard !pendingLines.isEmpty else { return }
            blocks.append(.text(MarkdownTextGroup(lines: pendingLines, alignment: alignment)))
            pendingLines.removeAll(keepingCapacity: true)
        }

        var index = 0
        while index < lines.count {
            let line = lines[index]
            let trimmed = line.trimmingCharacters(in: .whitespaces)

            if trimmed.hasPrefix("```") {
                flushText()
                index += 1
                var code: [String] = []
                while index < lines.count && !lines[index].trimmingCharacters(in: .whitespaces).hasPrefix("```") {
                    code.append(lines[index])
                    index += 1
                }
                if index < lines.count {
                    index += 1
                }
                blocks.append(.code(code.joined(separator: "\n")))
                continue
            }

            if let table = tableStarting(at: index, in: lines) {
                flushText()
                blocks.append(.table(table.table))
                index = table.nextIndex
                continue
            }

            if let paragraphAlignment = htmlParagraphOpeningAlignment(in: line) {
                flushText()
                alignment = paragraphAlignment
                index += 1
                continue
            }

            if htmlParagraphClosingRegex().firstMatch(
                in: line,
                options: [],
                range: NSRange(location: 0, length: line.utf16.count)
            ) != nil {
                flushText()
                alignment = .start
                index += 1
                continue
            }

            if let heading = htmlHeading(in: line) {
                flushText()
                let line = MarkdownLine(
                    kind: .heading,
                    inlines: parseInline(heading.content),
                    headingLevel: heading.level
                )
                blocks.append(.text(MarkdownTextGroup(lines: [line], alignment: heading.alignment)))
                index += 1
                continue
            }

            if let image = htmlImage(in: line, alignment: alignment) {
                flushText()
                blocks.append(.image(image))
                index += 1
                continue
            }

            if trimmed.hasPrefix(">") {
                flushText()
                var quoteLines: [MarkdownLine] = []
                while index < lines.count {
                    let quoteLine = lines[index].trimmingCharacters(in: .whitespaces)
                    guard quoteLine.hasPrefix(">") else { break }
                    let content = String(quoteLine.dropFirst()).trimmingCharacters(in: .whitespaces)
                    quoteLines.append(MarkdownLine(kind: .paragraph, inlines: parseInline(content)))
                    index += 1
                }
                blocks.append(.quote(quoteLines))
                continue
            }

            if isHorizontalRule(line) {
                pendingLines.append(MarkdownLine(kind: .rule, inlines: []))
                index += 1
                continue
            }

            if let heading = markdownHeading(in: line) {
                pendingLines.append(MarkdownLine(
                    kind: .heading,
                    inlines: parseInline(heading.content),
                    headingLevel: heading.level
                ))
                index += 1
                continue
            }

            if let ordered = orderedListItem(in: line) {
                pendingLines.append(MarkdownLine(
                    kind: .ordered,
                    inlines: parseInline(ordered.content),
                    orderedMarker: ordered.marker
                ))
                index += 1
                continue
            }

            if let unordered = unorderedListItem(in: line) {
                pendingLines.append(MarkdownLine(kind: .bullet, inlines: parseInline(unordered)))
                index += 1
                continue
            }

            pendingLines.append(MarkdownLine(kind: .paragraph, inlines: parseInline(line)))
            index += 1
        }

        flushText()
        return MarkdownDocument(blocks: blocks)
    }

    static func isHorizontalRule(_ line: String) -> Bool {
        let trimmed = line.trimmingCharacters(in: .whitespaces)
        guard trimmed.count >= 3 else { return false }
        guard let first = trimmed.first, first == "-" || first == "*" || first == "_" else {
            return false
        }
        return trimmed.allSatisfy { $0 == first }
    }

    private static func normalizeLineEndings(_ source: String) -> String {
        source.replacingOccurrences(of: "\r\n", with: "\n")
            .replacingOccurrences(of: "\r", with: "\n")
    }

    private static func markdownHeading(in line: String) -> (level: Int, content: String)? {
        let regex = try! NSRegularExpression(pattern: #"^(#{1,6})\s+(.*)$"#, options: [])
        guard let match = regex.firstMatch(
            in: line,
            options: [],
            range: NSRange(location: 0, length: line.utf16.count)
        ), let marker = string(from: match.range(at: 1), in: line),
        let content = string(from: match.range(at: 2), in: line) else {
            return nil
        }
        return (marker.count, content.trimmingCharacters(in: .whitespaces))
    }

    private static func orderedListItem(in line: String) -> (marker: String, content: String)? {
        let regex = try! NSRegularExpression(pattern: #"^\s*(\d+)\.\s+(.*)$"#, options: [])
        guard let match = regex.firstMatch(
            in: line,
            options: [],
            range: NSRange(location: 0, length: line.utf16.count)
        ), let marker = string(from: match.range(at: 1), in: line),
        let content = string(from: match.range(at: 2), in: line) else {
            return nil
        }
        return (marker, content)
    }

    private static func unorderedListItem(in line: String) -> String? {
        let regex = try! NSRegularExpression(pattern: #"^\s*[-*+]\s+(.*)$"#, options: [])
        guard let match = regex.firstMatch(
            in: line,
            options: [],
            range: NSRange(location: 0, length: line.utf16.count)
        ) else {
            return nil
        }
        return string(from: match.range(at: 1), in: line)
    }

    private static func htmlParagraphOpeningAlignment(in line: String) -> MarkdownAlignment? {
        let regex = htmlParagraphOpeningRegex()
        guard let match = regex.firstMatch(
            in: line,
            options: [],
            range: NSRange(location: 0, length: line.utf16.count)
        ) else {
            return nil
        }
        return MarkdownSanitizer.alignment(firstMatchedValue(match, in: line, groups: [1, 2, 3])) ?? .start
    }

    private static func htmlHeading(in line: String) -> (level: Int, alignment: MarkdownAlignment, content: String)? {
        let regex = htmlHeadingRegex()
        guard let match = regex.firstMatch(
            in: line,
            options: [],
            range: NSRange(location: 0, length: line.utf16.count)
        ), let openingLevel = string(from: match.range(at: 1), in: line),
        let closingLevel = string(from: match.range(at: 6), in: line),
        openingLevel == closingLevel,
        let content = string(from: match.range(at: 5), in: line) else {
            return nil
        }
        let htmlAlignment = MarkdownSanitizer.alignment(firstMatchedValue(match, in: line, groups: [2, 3, 4])) ?? .start
        return (Int(openingLevel) ?? 1, htmlAlignment, content)
    }

    private static func htmlImage(in line: String, alignment: MarkdownAlignment) -> MarkdownImage? {
        var imageLine = line
        var linkURL: String?

        let imageLinkRegex = try! NSRegularExpression(
            pattern: #"^\s*<a\s+([^>]+)>\s*(<img\s+[^>]+/?>)\s*</a>\s*$"#,
            options: [.caseInsensitive]
        )
        if let wrapped = imageLinkRegex.firstMatch(
            in: line,
            options: [],
            range: NSRange(location: 0, length: line.utf16.count)
        ), let attributes = string(from: wrapped.range(at: 1), in: line),
        let nestedImage = string(from: wrapped.range(at: 2), in: line) {
            let href = MarkdownSanitizer.htmlAttributes(attributes)["href"]
            if let href, MarkdownSanitizer.isSafeLinkURL(href) {
                linkURL = href.trimmingCharacters(in: .whitespacesAndNewlines)
            }
            imageLine = nestedImage
        }

        let imageRegex = try! NSRegularExpression(
            pattern: #"^\s*<img\s+([^>]+?)/?>\s*$"#,
            options: [.caseInsensitive]
        )
        guard let match = imageRegex.firstMatch(
            in: imageLine,
            options: [],
            range: NSRange(location: 0, length: imageLine.utf16.count)
        ), let rawAttributes = string(from: match.range(at: 1), in: imageLine) else {
            return nil
        }

        let attributes = MarkdownSanitizer.htmlAttributes(rawAttributes)
        guard let source = attributes["src"] else { return nil }
        let trimmedSource = source.trimmingCharacters(in: .whitespacesAndNewlines)
        return MarkdownImage(
            url: MarkdownSanitizer.isSafeImageURL(trimmedSource) ? trimmedSource : nil,
            alt: attributes["alt"] ?? "",
            width: MarkdownSanitizer.imageSide(attributes["width"]),
            height: MarkdownSanitizer.imageSide(attributes["height"]),
            alignment: alignment,
            linkURL: linkURL
        )
    }

    private static func tableStarting(at start: Int, in lines: [String]) -> (table: MarkdownTable, nextIndex: Int)? {
        guard start + 1 < lines.count, lines[start].contains("|") else { return nil }
        let headers = splitTableRow(lines[start])
        let separators = splitTableRow(lines[start + 1])
        guard !headers.isEmpty,
              headers.count == separators.count,
              separators.allSatisfy(isTableDelimiter) else {
            return nil
        }

        let alignments = separators.map(tableAlignment)
        var rows: [MarkdownTableRow] = []
        var nextIndex = start + 2
        while nextIndex < lines.count,
              lines[nextIndex].contains("|"),
              !lines[nextIndex].trimmingCharacters(in: .whitespaces).isEmpty,
              !lines[nextIndex].trimmingCharacters(in: .whitespaces).hasPrefix("```") {
            let cells = splitTableRow(lines[nextIndex])
            let normalized = (0..<headers.count).map { column in
                MarkdownTableCell(inlines: parseInline(column < cells.count ? cells[column] : ""))
            }
            rows.append(MarkdownTableRow(cells: normalized))
            nextIndex += 1
        }

        return (
            MarkdownTable(
                headers: headers.map { MarkdownTableCell(inlines: parseInline($0)) },
                alignments: alignments,
                rows: rows
            ),
            nextIndex
        )
    }

    private static func splitTableRow(_ line: String) -> [String] {
        var cells: [String] = []
        var current: [Character] = []
        var inCode = false
        let characters = Array(line)
        var index = 0

        while index < characters.count {
            let character = characters[index]
            if character == "\\", index + 1 < characters.count, characters[index + 1] == "|" {
                current.append("|")
                index += 2
                continue
            }
            if character == "`" {
                inCode.toggle()
                current.append(character)
            } else if character == "|", !inCode {
                cells.append(String(current).trimmingCharacters(in: .whitespaces))
                current.removeAll(keepingCapacity: true)
            } else {
                current.append(character)
            }
            index += 1
        }
        cells.append(String(current).trimmingCharacters(in: .whitespaces))

        let trimmedLine = line.trimmingCharacters(in: .whitespaces)
        if trimmedLine.hasPrefix("|"), cells.first == "" {
            cells.removeFirst()
        }
        if trimmedLine.hasSuffix("|"), cells.last == "" {
            cells.removeLast()
        }
        return cells
    }

    private static func isTableDelimiter(_ cell: String) -> Bool {
        var value = cell.trimmingCharacters(in: .whitespaces)
        if value.first == ":" { value.removeFirst() }
        if value.last == ":" { value.removeLast() }
        return !value.isEmpty && value.allSatisfy { $0 == "-" }
    }

    private static func tableAlignment(_ cell: String) -> MarkdownAlignment {
        let value = cell.trimmingCharacters(in: .whitespaces)
        let startsWithColon = value.first == ":"
        let endsWithColon = value.last == ":"
        if startsWithColon, endsWithColon { return .center }
        if endsWithColon { return .end }
        return .start
    }

    private static func parseInline(_ text: String) -> [MarkdownInline] {
        let codeRegex = try! NSRegularExpression(pattern: #"`([^`\n]+)`"#, options: [])
        let fullRange = NSRange(location: 0, length: text.utf16.count)
        let codeMatches = codeRegex.matches(in: text, options: [], range: fullRange)
        guard !codeMatches.isEmpty else {
            return parseMarkup(text)
        }

        var result: [MarkdownInline] = []
        var lastUTF16 = 0
        for match in codeMatches {
            let matchRange = match.range
            if matchRange.location > lastUTF16,
               let plain = string(from: NSRange(location: lastUTF16, length: matchRange.location - lastUTF16), in: text) {
                result.append(contentsOf: parseMarkup(plain))
            }
            if let code = string(from: match.range(at: 1), in: text), !code.isEmpty {
                result.append(MarkdownInline(text: code, style: .code))
            }
            lastUTF16 = matchRange.location + matchRange.length
        }
        if lastUTF16 < text.utf16.count,
           let plain = string(from: NSRange(location: lastUTF16, length: text.utf16.count - lastUTF16), in: text) {
            result.append(contentsOf: parseMarkup(plain))
        }
        return result
    }

    private static func parseMarkup(_ text: String) -> [MarkdownInline] {
        var buffer = InlineBuffer(text)
        applyHTMLLinks(to: &buffer)
        applyWrapper(to: &buffer, pattern: #"<strong>(.+?)</strong>"#, style: .bold, options: [.caseInsensitive, .dotMatchesLineSeparators])
        applyWrapper(to: &buffer, pattern: #"<sub>(.+?)</sub>"#, style: .small, options: [.caseInsensitive, .dotMatchesLineSeparators])
        applyWrapper(to: &buffer, pattern: #"\*\*([\s\S]+?)\*\*"#, style: .bold)
        applyWrapper(to: &buffer, pattern: #"(?<!\w)__([^_]+?)__(?!\w)"#, style: .bold)
        applyWrapper(to: &buffer, pattern: #"~~([^~]+?)~~"#, style: .strikethrough)
        applyWrapper(to: &buffer, pattern: #"\*([^*]+?)\*"#, style: .italic)
        applyWrapper(to: &buffer, pattern: #"(?<!\w)_([^_]+?)_(?!\w)"#, style: .italic)
        applyMarkdownLinks(to: &buffer)
        linkifyBareURLs(in: &buffer)
        return buffer.inlines()
    }

    private static func applyHTMLLinks(to buffer: inout InlineBuffer) {
        let pattern = #"<a\s+([^>]*)>([\s\S]*?)</a>"#
        let regex = try! NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
        for match in regex.matches(in: buffer.string, options: [], range: buffer.fullNSRange).reversed() {
            guard let wrapperRange = buffer.characterRange(for: match.range),
                  let labelRange = buffer.characterRange(for: match.range(at: 2)),
                  let attributes = buffer.string(from: match.range(at: 1)) else {
                continue
            }
            let href = MarkdownSanitizer.htmlAttributes(attributes)["href"]
            let safeURL = MarkdownSanitizer.isSafeLinkURL(href)
                ? href?.trimmingCharacters(in: .whitespacesAndNewlines)
                : nil
            let labelLength = labelRange.count
            buffer.remove(labelRange.upperBound..<wrapperRange.upperBound)
            buffer.remove(wrapperRange.lowerBound..<labelRange.lowerBound)
            if labelLength > 0, let safeURL {
                buffer.setLink(safeURL, in: wrapperRange.lowerBound..<(wrapperRange.lowerBound + labelLength))
            }
        }
    }

    private static func applyWrapper(
        to buffer: inout InlineBuffer,
        pattern: String,
        style: MarkdownInlineStyle,
        options: NSRegularExpression.Options = []
    ) {
        let regex = try! NSRegularExpression(pattern: pattern, options: options)
        for match in regex.matches(in: buffer.string, options: [], range: buffer.fullNSRange).reversed() {
            guard let wrapperRange = buffer.characterRange(for: match.range),
                  let contentRange = buffer.characterRange(for: match.range(at: 1)),
                  contentRange.count > 0 else {
                continue
            }
            let contentLength = contentRange.count
            buffer.remove(contentRange.upperBound..<wrapperRange.upperBound)
            buffer.remove(wrapperRange.lowerBound..<contentRange.lowerBound)
            buffer.addStyle(style, in: wrapperRange.lowerBound..<(wrapperRange.lowerBound + contentLength))
        }
    }

    private static func applyMarkdownLinks(to buffer: inout InlineBuffer) {
        let regex = try! NSRegularExpression(pattern: #"\[([^\]]+)\]\(([^)\s]+)\)"#, options: [])
        for match in regex.matches(in: buffer.string, options: [], range: buffer.fullNSRange).reversed() {
            guard let wrapperRange = buffer.characterRange(for: match.range),
                  let labelRange = buffer.characterRange(for: match.range(at: 1)),
                  let rawURL = buffer.string(from: match.range(at: 2)),
                  let url = MarkdownSanitizer.normalizedMarkdownLink(rawURL) else {
                continue
            }
            let labelLength = labelRange.count
            buffer.remove(labelRange.upperBound..<wrapperRange.upperBound)
            buffer.remove(wrapperRange.lowerBound..<labelRange.lowerBound)
            if labelLength > 0 {
                buffer.setLink(url, in: wrapperRange.lowerBound..<(wrapperRange.lowerBound + labelLength))
            }
        }
    }

    private static func linkifyBareURLs(in buffer: inout InlineBuffer) {
        let pattern = "(?i)(?<![\\p{L}\\p{N}@.-])(?:(?:https?://|www\\.)[^\\s<>\"']*[^\\s<>\"'.,!?;:)\\]]|(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\\.)+(?:" + topLevelDomains + ")(?![a-z0-9])(?:[/?#][^\\s<>\"']*[^\\s<>\"'.,!?;:)\\]])?)"
        let regex = try! NSRegularExpression(pattern: pattern, options: [])
        for match in regex.matches(in: buffer.string, options: [], range: buffer.fullNSRange).reversed() {
            guard let range = buffer.characterRange(for: match.range),
                  !buffer.hasLink(in: range),
                  let rawURL = buffer.string(from: match.range),
                  let url = MarkdownSanitizer.normalizedBareURL(rawURL) else {
                continue
            }
            buffer.setLink(url, in: range)
        }
    }

    private static func firstMatchedValue(
        _ match: NSTextCheckingResult,
        in source: String,
        groups: [Int]
    ) -> String? {
        for group in groups {
            if let value = string(from: match.range(at: group), in: source), !value.isEmpty {
                return value
            }
        }
        return nil
    }

    private static func htmlParagraphOpeningRegex() -> NSRegularExpression {
        try! NSRegularExpression(
            pattern: #"^\s*<p(?:\s+align\s*=\s*(?:"(left|center|right)"|'(left|center|right)'|(left|center|right)))?\s*>\s*$"#,
            options: [.caseInsensitive]
        )
    }

    private static func htmlParagraphClosingRegex() -> NSRegularExpression {
        try! NSRegularExpression(pattern: #"^\s*</p>\s*$"#, options: [.caseInsensitive])
    }

    private static func htmlHeadingRegex() -> NSRegularExpression {
        try! NSRegularExpression(
            pattern: #"^\s*<h([1-6])(?:\s+align\s*=\s*(?:"(left|center|right)"|'(left|center|right)'|(left|center|right)))?\s*>([\s\S]*?)</h([1-6])>\s*$"#,
            options: [.caseInsensitive]
        )
    }

    private static func string(from range: NSRange, in source: String) -> String? {
        guard range.location != NSNotFound, let swiftRange = Range(range, in: source) else {
            return nil
        }
        return String(source[swiftRange])
    }

    private struct InlineBuffer {
        private(set) var characters: [Character]
        private(set) var styles: [MarkdownInlineStyle]
        private(set) var links: [String?]

        init(_ source: String) {
            characters = Array(source)
            styles = Array(repeating: .none, count: characters.count)
            links = Array(repeating: nil, count: characters.count)
        }

        var string: String { String(characters) }

        var fullNSRange: NSRange {
            NSRange(location: 0, length: string.utf16.count)
        }

        func characterRange(for range: NSRange) -> Range<Int>? {
            guard range.location != NSNotFound,
                  let swiftRange = Range(range, in: string) else {
                return nil
            }
            let start = string.distance(from: string.startIndex, to: swiftRange.lowerBound)
            let end = string.distance(from: string.startIndex, to: swiftRange.upperBound)
            return start..<end
        }

        func string(from range: NSRange) -> String? {
            guard range.location != NSNotFound, let swiftRange = Range(range, in: string) else {
                return nil
            }
            return String(string[swiftRange])
        }

        mutating func remove(_ range: Range<Int>) {
            guard !range.isEmpty else { return }
            characters.removeSubrange(range)
            styles.removeSubrange(range)
            links.removeSubrange(range)
        }

        mutating func addStyle(_ style: MarkdownInlineStyle, in range: Range<Int>) {
            guard !range.isEmpty else { return }
            for index in range {
                styles[index].insert(style)
            }
        }

        mutating func setLink(_ link: String, in range: Range<Int>) {
            guard !range.isEmpty else { return }
            for index in range {
                links[index] = link
            }
        }

        func hasLink(in range: Range<Int>) -> Bool {
            range.contains { links[$0] != nil }
        }

        func inlines() -> [MarkdownInline] {
            guard !characters.isEmpty else { return [] }
            var result: [MarkdownInline] = []
            var start = 0

            func append(_ start: Int, _ end: Int, to result: inout [MarkdownInline], characters: [Character], styles: [MarkdownInlineStyle], links: [String?]) {
                guard start < end else { return }
                let text = String(characters[start..<end])
                let inline = MarkdownInline(text: text, style: styles[start], linkURL: links[start])
                if let previous = result.last,
                   previous.style == inline.style,
                   previous.linkURL == inline.linkURL {
                    result[result.count - 1] = MarkdownInline(
                        text: previous.text + inline.text,
                        style: previous.style,
                        linkURL: previous.linkURL
                    )
                } else {
                    result.append(inline)
                }
            }

            for index in 1..<characters.count {
                if styles[index] != styles[start] || links[index] != links[start] {
                    append(start, index, to: &result, characters: characters, styles: styles, links: links)
                    start = index
                }
            }
            append(start, characters.count, to: &result, characters: characters, styles: styles, links: links)
            return result
        }
    }
}
