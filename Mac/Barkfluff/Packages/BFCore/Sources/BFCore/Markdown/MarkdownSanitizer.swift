import Foundation

enum MarkdownSanitizer {
    private static let attributePattern = #"([a-z][a-z0-9-]*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'=<>`]+))"#
    private static let safeLinkPattern = #"^(?:https?://|mailto:)"#
    private static let safeImagePattern = #"^https?://"#

    static func htmlAttributes(_ raw: String) -> [String: String] {
        let regex = try! NSRegularExpression(
            pattern: attributePattern,
            options: [.caseInsensitive]
        )
        let range = NSRange(location: 0, length: raw.utf16.count)
        var attributes: [String: String] = [:]

        for match in regex.matches(in: raw, options: [], range: range) {
            guard let name = string(from: match.range(at: 1), in: raw) else { continue }
            let value = [2, 3, 4]
                .compactMap { string(from: match.range(at: $0), in: raw) }
                .first ?? ""
            attributes[name.lowercased()] = value
        }

        return attributes
    }

    static func isSafeLinkURL(_ url: String?) -> Bool {
        guard let url else { return false }
        return matches(safeLinkPattern, url.trimmingCharacters(in: .whitespacesAndNewlines))
    }

    static func isSafeImageURL(_ url: String?) -> Bool {
        guard let url else { return false }
        return matches(safeImagePattern, url.trimmingCharacters(in: .whitespacesAndNewlines))
    }

    static func imageSide(_ raw: String?) -> Int? {
        guard let raw, let value = Int(raw.trimmingCharacters(in: .whitespacesAndNewlines)), (1...2048).contains(value) else {
            return nil
        }
        return value
    }

    static func alignment(_ value: String?) -> MarkdownAlignment? {
        switch value?.lowercased() {
        case "left": return .start
        case "center": return .center
        case "right": return .end
        default: return nil
        }
    }

    static func normalizedMarkdownLink(_ raw: String) -> String? {
        let value = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if isSafeLinkURL(value) {
            return value
        }

        guard isRecognizedBareURL(value) else { return nil }
        return "http://" + value
    }

    static func normalizedBareURL(_ raw: String) -> String? {
        let value = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if isSafeLinkURL(value) {
            return value
        }

        guard isRecognizedBareURL(value) else { return nil }
        return "http://" + value
    }

    private static func isRecognizedBareURL(_ value: String) -> Bool {
        let regex = try! NSRegularExpression(
            pattern: #"(?i)^(?:(?:www\.)|(?:(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+(?:com|org|net|int|edu|gov|mil|info|biz|pro|name|xyz|online|site|tech|store|shop|cloud|app|dev|ai|io|co|me|tv|cc|su|ru|by|kz|ua|uk|de|fr|it|es|pl|nl|se|no|fi|cz|tr|cn|jp|kr|in|br|ca|au|eu|top|live|news|blog|art|fun|link|click|space|website|digital|agency|team|games|studio|design|software|group|media|world|life|zone|host|press|wiki|guru|expert)))"#,
            options: []
        )
        return regex.firstMatch(
            in: value,
            options: [],
            range: NSRange(location: 0, length: value.utf16.count)
        ) != nil
    }

    private static func matches(_ pattern: String, _ value: String) -> Bool {
        let regex = try! NSRegularExpression(pattern: pattern, options: [.caseInsensitive])
        return regex.firstMatch(
            in: value,
            options: [],
            range: NSRange(location: 0, length: value.utf16.count)
        ) != nil
    }

    private static func string(from range: NSRange, in source: String) -> String? {
        guard range.location != NSNotFound, let swiftRange = Range(range, in: source) else {
            return nil
        }
        return String(source[swiftRange])
    }
}
