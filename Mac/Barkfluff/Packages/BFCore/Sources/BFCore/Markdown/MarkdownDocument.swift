import Foundation

/// Parsed message content shared by the Apple clients.
public struct MarkdownDocument: Hashable, Sendable {
    public let blocks: [MarkdownBlock]

    public init(blocks: [MarkdownBlock]) {
        self.blocks = blocks
    }

    /// Returns the original content when the document contains no formatting.
    /// This lets message lists render the common plain-text case without walking
    /// the complete block tree.
    public var plainText: String? {
        guard !blocks.isEmpty else { return "" }
        guard blocks.count == 1,
              case let .text(group) = blocks[0],
              group.alignment == .start,
              group.lines.allSatisfy({ line in
                  line.kind == .paragraph
                      && line.inlines.allSatisfy { inline in
                          inline.style == .none && inline.linkURL == nil
                      }
              }) else {
            return nil
        }

        return group.lines
            .map { $0.inlines.map(\.text).joined() }
            .joined(separator: "\n")
    }
}

public enum MarkdownBlock: Hashable, Sendable {
    case text(MarkdownTextGroup)
    case code(String)
    case quote([MarkdownLine])
    case table(MarkdownTable)
    case image(MarkdownImage)
}

public struct MarkdownTextGroup: Hashable, Sendable {
    public let lines: [MarkdownLine]
    public let alignment: MarkdownAlignment

    public init(lines: [MarkdownLine], alignment: MarkdownAlignment = .start) {
        self.lines = lines
        self.alignment = alignment
    }
}

public struct MarkdownLine: Hashable, Sendable {
    public let kind: MarkdownLineKind
    public let inlines: [MarkdownInline]
    public let headingLevel: Int
    public let orderedMarker: String

    public init(
        kind: MarkdownLineKind,
        inlines: [MarkdownInline],
        headingLevel: Int = 0,
        orderedMarker: String = ""
    ) {
        self.kind = kind
        self.inlines = inlines
        self.headingLevel = headingLevel
        self.orderedMarker = orderedMarker
    }
}

public enum MarkdownLineKind: Hashable, Sendable {
    case paragraph
    case heading
    case bullet
    case ordered
    case rule
}

public struct MarkdownInline: Hashable, Sendable {
    public let text: String
    public let style: MarkdownInlineStyle
    public let linkURL: String?

    public init(
        text: String,
        style: MarkdownInlineStyle = .none,
        linkURL: String? = nil
    ) {
        self.text = text
        self.style = style
        self.linkURL = linkURL
    }
}

public struct MarkdownInlineStyle: OptionSet, Hashable, Sendable {
    public let rawValue: UInt8

    public init(rawValue: UInt8) {
        self.rawValue = rawValue
    }

    public static let none = MarkdownInlineStyle([])
    public static let bold = MarkdownInlineStyle(rawValue: 1 << 0)
    public static let italic = MarkdownInlineStyle(rawValue: 1 << 1)
    public static let strikethrough = MarkdownInlineStyle(rawValue: 1 << 2)
    public static let code = MarkdownInlineStyle(rawValue: 1 << 3)
    public static let small = MarkdownInlineStyle(rawValue: 1 << 4)
}

public enum MarkdownAlignment: Hashable, Sendable {
    case start
    case center
    case end
}

public struct MarkdownTable: Hashable, Sendable {
    public let headers: [MarkdownTableCell]
    public let alignments: [MarkdownAlignment]
    public let rows: [MarkdownTableRow]

    public init(
        headers: [MarkdownTableCell],
        alignments: [MarkdownAlignment],
        rows: [MarkdownTableRow]
    ) {
        self.headers = headers
        self.alignments = alignments
        self.rows = rows
    }
}

public struct MarkdownTableRow: Hashable, Sendable {
    public let cells: [MarkdownTableCell]

    public init(cells: [MarkdownTableCell]) {
        self.cells = cells
    }
}

public struct MarkdownTableCell: Hashable, Sendable {
    public let inlines: [MarkdownInline]

    public init(inlines: [MarkdownInline]) {
        self.inlines = inlines
    }
}

/// An image parsed from the safe HTML subset.
public struct MarkdownImage: Hashable, Sendable {
    public let url: String?
    public let alt: String
    public let width: Int?
    public let height: Int?
    public let alignment: MarkdownAlignment
    public let linkURL: String?

    public init(
        url: String?,
        alt: String,
        width: Int? = nil,
        height: Int? = nil,
        alignment: MarkdownAlignment = .start,
        linkURL: String? = nil
    ) {
        self.url = url
        self.alt = alt
        self.width = width
        self.height = height
        self.alignment = alignment
        self.linkURL = linkURL
    }
}
