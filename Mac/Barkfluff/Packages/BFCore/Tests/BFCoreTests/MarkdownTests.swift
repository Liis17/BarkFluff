import XCTest
@testable import BFCore

final class MarkdownTests: XCTestCase {
    func testPlainTextAndHeadingAreExposedThroughThePublicDocument() {
        let plain = MarkdownParser.parse("Привет\nмир")

        XCTAssertEqual(plain.plainText, "Привет\nмир")

        let heading = MarkdownParser.parse("## Заголовок")
        guard case let .text(group) = heading.blocks.first,
              let line = group.lines.first else {
            return XCTFail("Expected a text group with a heading")
        }

        XCTAssertEqual(group.alignment, .start)
        XCTAssertEqual(line.kind, .heading)
        XCTAssertEqual(line.headingLevel, 2)
        XCTAssertEqual(line.inlines.map(\.text).joined(), "Заголовок")
    }

    func testStripRemovesFormattingForCompactPreviews() {
        let source = "## Выполнен вход\n\n- **Устройство:** MacBook\n> _Проверьте_"

        XCTAssertEqual(
            MarkdownText.strip(source),
            "Выполнен вход Устройство: MacBook Проверьте"
        )
    }

    func testInlineStylesAndLinksAreExposedAsRuns() {
        let inlines = firstTextLine(from: "**bold** *italic* ~~deleted~~ `code` [site](https://example.com)")

        XCTAssertEqual(inlines, [
            MarkdownInline(text: "bold", style: .bold),
            MarkdownInline(text: " "),
            MarkdownInline(text: "italic", style: .italic),
            MarkdownInline(text: " "),
            MarkdownInline(text: "deleted", style: .strikethrough),
            MarkdownInline(text: " "),
            MarkdownInline(text: "code", style: .code),
            MarkdownInline(text: " "),
            MarkdownInline(text: "site", linkURL: "https://example.com")
        ])
    }

    func testBareURLIsLinkifiedButCodeAndExistingLinkAreProtected() {
        let inlines = firstTextLine(from: "example.com `https://code.example.com` [link](https://already.example.com)")

        XCTAssertEqual(inlines[0], MarkdownInline(text: "example.com", linkURL: "http://example.com"))
        XCTAssertEqual(inlines[2], MarkdownInline(text: "https://code.example.com", style: .code))
        XCTAssertEqual(inlines[4], MarkdownInline(text: "link", linkURL: "https://already.example.com"))
    }

    func testBlockSyntaxIncludesListsQuotesRulesAndFencedCode() {
        let document = MarkdownParser.parse("""
        - first
        1. second
        ---
        > quoted **text**
        ```swift
        **literal**
        ```
        """)

        guard case let .text(group) = document.blocks[0] else {
            return XCTFail("Expected list and rule text group")
        }
        XCTAssertEqual(group.lines.map(\.kind), [.bullet, .ordered, .rule])
        XCTAssertEqual(group.lines[1].orderedMarker, "1")

        guard case let .quote(quoteLines) = document.blocks[1],
              case let .code(code) = document.blocks[2] else {
            return XCTFail("Expected quote and code blocks")
        }
        XCTAssertEqual(quoteLines[0].inlines, [MarkdownInline(text: "quoted "), MarkdownInline(text: "text", style: .bold)])
        XCTAssertEqual(code, "**literal**")
    }

    func testGFMTableParsesAlignmentEscapedPipesAndShortRows() {
        let document = MarkdownParser.parse("""
        | Name | Description | Count |
        | :--- | :----------: | ---: |
        | A\\|B | `x|y` | 2 |
        | only | value |
        """)

        guard case let .table(table) = document.blocks.first else {
            return XCTFail("Expected a table block")
        }
        XCTAssertEqual(table.alignments, [.start, .center, .end])
        XCTAssertEqual(table.headers[0].inlines.map(\.text).joined(), "Name")
        XCTAssertEqual(table.rows[0].cells[0].inlines.map(\.text).joined(), "A|B")
        XCTAssertEqual(table.rows[0].cells[1].inlines, [MarkdownInline(text: "x|y", style: .code)])
        XCTAssertEqual(table.rows[1].cells.count, 3)
        XCTAssertEqual(table.rows[1].cells[2].inlines, [])
    }

    func testSafeHTMLSupportsAlignmentAndImagesWithoutActivatingUnsafeURLs() {
        let document = MarkdownParser.parse("""
        <p align="center">
        <strong>README</strong>
        </p>
        <img src="https://example.com/image.png" alt="preview" width="240" height="9999">
        <img src="javascript:alert(1)" alt="unsafe">
        """)

        guard case let .text(group) = document.blocks[0],
              case let .image(safeImage) = document.blocks[1],
              case let .image(unsafeImage) = document.blocks[2] else {
            return XCTFail("Expected HTML paragraph and images")
        }
        XCTAssertEqual(group.alignment, .center)
        XCTAssertEqual(group.lines[0].inlines, [MarkdownInline(text: "README", style: .bold)])
        XCTAssertEqual(safeImage.url, "https://example.com/image.png")
        XCTAssertEqual(safeImage.width, 240)
        XCTAssertNil(safeImage.height)
        XCTAssertNil(unsafeImage.url)
        XCTAssertEqual(unsafeImage.alt, "unsafe")
    }

    func testStripPreservesCodeAndTableContentAndUsesImageAltText() {
        let source = """
        # Header
        ```
        **literal**
        ```
        | A | B |
        |---|---|
        | 1 | 2 |
        <img src="https://example.com/x.png" alt="image alt">
        """

        XCTAssertEqual(
            MarkdownText.strip(source),
            "Header **literal** A B 1 2 image alt"
        )
    }

    private func firstTextLine(from source: String, file: StaticString = #filePath, line: UInt = #line) -> [MarkdownInline] {
        let document = MarkdownParser.parse(source)
        guard case let .text(group) = document.blocks.first,
              let firstLine = group.lines.first else {
            XCTFail("Expected a text line", file: file, line: line)
            return []
        }
        return firstLine.inlines
    }
}
