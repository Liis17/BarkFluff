package com.barkfluff.client.utils

import android.content.res.Resources
import android.content.Intent
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.graphics.Typeface
import android.text.Spannable
import android.text.SpannableStringBuilder
import android.text.Spanned
import android.text.method.LinkMovementMethod
import android.text.style.BackgroundColorSpan
import android.text.style.BulletSpan
import android.text.style.ClickableSpan
import android.text.style.ForegroundColorSpan
import android.text.style.LeadingMarginSpan
import android.text.style.QuoteSpan
import android.text.style.RelativeSizeSpan
import android.text.style.StrikethroughSpan
import android.text.style.StyleSpan
import android.text.style.TypefaceSpan
import android.text.style.URLSpan
import android.util.Patterns
import android.util.TypedValue
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TableLayout
import android.widget.TableRow
import android.widget.TextView
import androidx.core.graphics.ColorUtils
import coil.request.ImageRequest

/**
 * Базовый markdown-рендер для пузырей сообщений. Бэкенд хранит обычный текст с
 * символами разметки — интерпретация целиком на клиенте.
 *
 * Поддержка (line-based): заголовки, маркированные/нумерованные списки, цитаты,
 * горизонтальные линии, блоки кода + inline (**bold**, *italic*, ~~strike~~,
 * `code`, [текст](url)) и автолинковка «голых» URL.
 *
 * Вложенные списки не поддерживаются. HTML поддержан через allowlist: p/h1..h6/strong/sub,
 * a[href] и img[src, alt, width, height]. GFM-таблицы и HTML-изображения доступны только
 * в bubble сообщений через [renderMessageInto], где для них нужна отдельная View-иерархия.
 */
object MarkdownRenderer {

    private const val FLAG = Spanned.SPAN_EXCLUSIVE_EXCLUSIVE

    private val HEADING = Regex("^(#{1,6})\\s+")
    private val ORDERED = Regex("^\\s*(\\d+)\\.\\s+(.*)")
    private val UNORDERED = Regex("^\\s*[-*+]\\s+(.*)")
    private val CODE_INLINE = Regex("`([^`\\n]+)`")
    private val BOLD_STARS = Regex("\\*\\*([^*]+?)\\*\\*")
    private val BOLD_UNDERS = Regex("(?<!\\w)__([^_]+?)__(?!\\w)")
    private val STRIKE = Regex("~~([^~]+?)~~")
    private val ITALIC_STAR = Regex("\\*([^*]+?)\\*")
    private val ITALIC_UNDER = Regex("(?<!\\w)_([^_]+?)_(?!\\w)")
    private val LINK = Regex("\\[([^\\]]+)\\]\\(([^)\\s]+)\\)")
    private val TABLE_DELIMITER_CELL = Regex("^:?-+:?$")
    private val HTML_PARAGRAPH_OPEN = Regex(
        """^\s*<p(?:\s+align\s*=\s*(?:\"(left|center|right)\"|'(left|center|right)'|(left|center|right)))?\s*>\s*$""",
        RegexOption.IGNORE_CASE
    )
    private val HTML_PARAGRAPH_CLOSE = Regex("^\\s*</p>\\s*$", RegexOption.IGNORE_CASE)
    private val HTML_HEADING = Regex(
        """^\s*<h([1-6])(?:\s+align\s*=\s*(?:\"(left|center|right)\"|'(left|center|right)'|(left|center|right)))?\s*>(.*?)</h([1-6])>\s*$""",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL)
    )
    private val HTML_IMAGE = Regex("""^\s*<img\s+([^>]+?)/?>\s*$""", RegexOption.IGNORE_CASE)
    private val HTML_IMAGE_LINK = Regex(
        """^\s*<a\s+([^>]+)>\s*(<img\s+[^>]+/?>)\s*</a>\s*$""",
        RegexOption.IGNORE_CASE
    )
    private val HTML_ATTRIBUTE = Regex("""([a-z][a-z0-9-]*)\s*=\s*(?:\"([^\"]*)\"|'([^']*)'|([^\s\"'=<>`]+))""", RegexOption.IGNORE_CASE)
    private val HTML_STRONG = Regex("""<strong>(.*?)</strong>""", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL))
    private val HTML_SUB = Regex("""<sub>(.*?)</sub>""", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL))
    private val HTML_LINK = Regex("""<a\s+([^>]*)>(.*?)</a>""", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL))

    private val density = Resources.getSystem().displayMetrics.density
    private fun dp(v: Float) = (v * density).toInt()

    private fun codeBg(base: Int) = ColorUtils.setAlphaComponent(base, 0x22)
    private fun dim(base: Int) = ColorUtils.setAlphaComponent(base, 0x99)

    /**
     * Парсит markdown, ставит текст на bubble-TextView, автолинкует URL и
     * включает/сбрасывает movementMethod (сброс важен для переиспользования ViewHolder).
     */
    fun applyTo(textView: TextView, source: String) {
        val sb = buildSpanned(source, textView.currentTextColor)
        linkifyBareUrls(sb)
        val hasLink = sb.getSpans(0, sb.length, URLSpan::class.java).isNotEmpty()
        textView.movementMethod = if (hasLink) LinkOnlyMovementMethod else null
        textView.text = sb
    }

    /**
     * Рендерит текст сообщения в bubble. Таблицы получают собственную сетку с прокруткой,
     * обычные блоки остаются [TextView] со всеми прежними Spannable-стилями.
     */
    fun renderMessageInto(container: LinearLayout, template: TextView, source: String) {
        val blocks = splitMessageBlocks(source)
        container.removeAllViews()

        if (blocks.size == 1 && blocks[0] is MessageBlock.Text &&
            (blocks[0] as MessageBlock.Text).gravity == null && (blocks[0] as MessageBlock.Text).source == source
        ) {
            container.addView(template)
            applyTo(template, source)
            template.visibility = TextView.VISIBLE
            return
        }

        template.text = ""
        template.visibility = TextView.GONE
        blocks.forEach { block ->
            when (block) {
                is MessageBlock.Text -> {
                    if (block.source.isNotEmpty()) container.addView(createTextBlock(template, block.source, block.gravity))
                }
                is MessageBlock.Table -> container.addView(createTableBlock(template, block.table))
                is MessageBlock.HtmlImage -> container.addView(createHtmlImageBlock(template, block.image))
            }
        }
    }

    /** Сбрасывает динамические блоки при переиспользовании ViewHolder для нетекстового сообщения. */
    fun clearMessageContent(container: LinearLayout, template: TextView) {
        container.removeAllViews()
        template.text = ""
        template.visibility = TextView.GONE
        container.addView(template)
    }

    /** Убирает markdown-разметку в чистый однострочный текст для превью. */
    fun strip(source: String): String {
        val joined = source.lines().joinToString(" ") { raw ->
            val line = raw.trim()
            if (line.startsWith("```")) return@joinToString ""
            if (isHr(line)) return@joinToString ""
            line.replace(Regex("^#{1,6}\\s*"), "")
                .replace(Regex("^>\\s*"), "")
                .replace(Regex("^[-*+]\\s+"), "")
                .replace(Regex("^\\d+\\.\\s+"), "")
        }
        return joined
            .replace(LINK, "$1")
            .replace(HTML_IMAGE) { match -> htmlAttributes(match.groupValues[1])["alt"] ?: "" }
            .replace(Regex("</?[a-z][^>]*>", RegexOption.IGNORE_CASE), "")
            .replace(Regex("(\\*\\*|__|~~|`|\\*|_)"), "")
            .replace(Regex("\\s+"), " ")
            .trim()
    }

    private fun buildSpanned(source: String, baseColor: Int): SpannableStringBuilder {
        val out = SpannableStringBuilder()
        val lines = source.split("\n")
        var i = 0
        while (i < lines.size) {
            val line = lines[i]
            if (out.isNotEmpty()) out.append("\n")
            val trimmed = line.trimStart()

            // Ограждённый блок кода ``` ... ```
            if (trimmed.startsWith("```")) {
                val code = StringBuilder()
                i++
                while (i < lines.size && !lines[i].trimStart().startsWith("```")) {
                    if (code.isNotEmpty()) code.append("\n")
                    code.append(lines[i]); i++
                }
                if (i < lines.size) i++ // пропустить закрывающий забор
                val s = out.length
                out.append(code.toString())
                out.setSpan(TypefaceSpan("monospace"), s, out.length, FLAG)
                out.setSpan(BackgroundColorSpan(codeBg(baseColor)), s, out.length, FLAG)
                out.setSpan(LeadingMarginSpan.Standard(dp(12f), dp(12f)), s, out.length, FLAG)
                continue
            }

            // Горизонтальная линия
            if (isHr(line)) {
                val s = out.length
                out.append("────────")
                out.setSpan(ForegroundColorSpan(dim(baseColor)), s, out.length, FLAG)
                i++; continue
            }

            // Заголовок
            val hl = headingLevel(line)
            if (hl > 0) {
                val content = line.substring(hl).trim()
                val s = out.length
                out.append(parseInline(content, baseColor))
                out.setSpan(RelativeSizeSpan(headingScale(hl)), s, out.length, FLAG)
                out.setSpan(StyleSpan(Typeface.BOLD), s, out.length, FLAG)
                i++; continue
            }

            // Цитата
            if (trimmed.startsWith(">")) {
                val content = trimmed.removePrefix(">").trimStart()
                val s = out.length
                out.append(parseInline(content, baseColor))
                out.setSpan(QuoteSpan(dim(baseColor), dp(3f).coerceAtLeast(1), dp(8f)), s, out.length, FLAG)
                out.setSpan(ForegroundColorSpan(dim(baseColor)), s, out.length, FLAG)
                i++; continue
            }

            // Нумерованный список
            val om = ORDERED.find(line)
            if (om != null) {
                val s = out.length
                out.append("${om.groupValues[1]}. ")
                out.append(parseInline(om.groupValues[2], baseColor))
                out.setSpan(LeadingMarginSpan.Standard(0, dp(16f)), s, out.length, FLAG)
                i++; continue
            }

            // Маркированный список
            val ul = UNORDERED.find(line)
            if (ul != null) {
                val s = out.length
                out.append(parseInline(ul.groupValues[1], baseColor))
                out.setSpan(BulletSpan(dp(8f), baseColor, dp(2f)), s, out.length, FLAG)
                i++; continue
            }

            // Обычная строка
            out.append(parseInline(line, baseColor))
            i++
        }
        return out
    }

    private fun splitMessageBlocks(source: String): List<MessageBlock> {
        val lines = source.split("\n")
        val blocks = mutableListOf<MessageBlock>()
        var textStart = 0
        var index = 0
        var inCodeBlock = false

        while (index < lines.size) {
            if (lines[index].trimStart().startsWith("```")) {
                inCodeBlock = !inCodeBlock
                index++
                continue
            }

            val table = if (!inCodeBlock) parseTable(lines, index) else null
            if (table == null) {
                index++
                continue
            }

            if (index > textStart) blocks += MessageBlock.Text(lines.subList(textStart, index).joinToString("\n"))
            blocks += MessageBlock.Table(table.table)
            index = table.endIndex
            textStart = index
        }

        if (textStart < lines.size) blocks += MessageBlock.Text(lines.subList(textStart, lines.size).joinToString("\n"))
        return blocks.flatMap { block ->
            if (block is MessageBlock.Text) splitHtmlBlocks(block.source) else listOf(block)
        }.ifEmpty { listOf(MessageBlock.Text(source)) }
    }

    private fun splitHtmlBlocks(source: String): List<MessageBlock> {
        val blocks = mutableListOf<MessageBlock>()
        val text = StringBuilder()
        var gravity: Int? = null
        var inCodeBlock = false

        fun flushText() {
            if (text.isNotEmpty()) {
                blocks += MessageBlock.Text(text.toString().trimEnd('\n'), gravity)
                text.clear()
            }
        }

        source.lines().forEach { line ->
            if (line.trimStart().startsWith("```")) {
                if (text.isNotEmpty()) text.append('\n')
                text.append(line)
                inCodeBlock = !inCodeBlock
                return@forEach
            }
            if (inCodeBlock) {
                if (text.isNotEmpty()) text.append('\n')
                text.append(line)
                return@forEach
            }

            val paragraph = HTML_PARAGRAPH_OPEN.matchEntire(line)
            if (paragraph != null) {
                flushText()
                gravity = htmlGravity(paragraph.groupValues.drop(1).firstOrNull { it.isNotEmpty() })
                return@forEach
            }
            if (HTML_PARAGRAPH_CLOSE.matches(line)) {
                flushText()
                gravity = null
                return@forEach
            }

            val heading = HTML_HEADING.matchEntire(line)
            if (heading != null && heading.groupValues[1] == heading.groupValues[6]) {
                flushText()
                val headingGravity = htmlGravity(heading.groupValues.subList(2, 5).firstOrNull { it.isNotEmpty() }) ?: gravity
                blocks += MessageBlock.Text("${"#".repeat(heading.groupValues[1].toInt())} ${heading.groupValues[5]}", headingGravity)
                return@forEach
            }

            val image = parseHtmlImage(line, gravity)
            if (image != null) {
                flushText()
                blocks += MessageBlock.HtmlImage(image)
                return@forEach
            }

            if (text.isNotEmpty()) text.append('\n')
            text.append(line)
        }
        flushText()
        return blocks
    }

    private fun parseTable(lines: List<String>, start: Int): ParsedTable? {
        if (start + 1 >= lines.size) return null
        val header = splitTableRow(lines[start])
        val delimiter = splitTableRow(lines[start + 1])
        if (!lines[start].contains('|') || header.isEmpty() || header.size != delimiter.size ||
            delimiter.any { !TABLE_DELIMITER_CELL.matches(it.trim()) }) return null

        val alignments = delimiter.map { cell ->
            val value = cell.trim()
            when {
                value.startsWith(":") && value.endsWith(":") -> TableAlignment.CENTER
                value.endsWith(":") -> TableAlignment.END
                else -> TableAlignment.START
            }
        }
        val rows = mutableListOf<List<String>>()
        var end = start + 2
        while (end < lines.size && lines[end].contains('|')) {
            rows += normalizeTableRow(splitTableRow(lines[end]), header.size)
            end++
        }
        return ParsedTable(MarkdownTable(header, alignments, rows), end)
    }

    private fun splitTableRow(line: String): List<String> {
        val cells = mutableListOf<String>()
        val current = StringBuilder()
        var inCode = false
        var index = 0
        while (index < line.length) {
            val ch = line[index]
            when {
                ch == '\\' && line.getOrNull(index + 1) == '|' -> {
                    current.append('|')
                    index++
                }
                ch == '`' -> {
                    inCode = !inCode
                    current.append(ch)
                }
                ch == '|' && !inCode -> {
                    cells += current.toString().trim()
                    current.clear()
                }
                else -> current.append(ch)
            }
            index++
        }
        cells += current.toString().trim()

        if (line.trimStart().startsWith("|") && cells.firstOrNull().isNullOrEmpty()) cells.removeAt(0)
        if (line.trimEnd().endsWith("|") && cells.lastOrNull().isNullOrEmpty()) cells.removeAt(cells.lastIndex)
        return cells
    }

    private fun normalizeTableRow(cells: List<String>, width: Int): List<String> =
        List(width) { cells.getOrElse(it) { "" } }

    private fun createTextBlock(template: TextView, source: String, gravityOverride: Int? = null): TextView = TextView(template.context).apply {
        val original = template.layoutParams as ViewGroup.MarginLayoutParams
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).also {
            it.leftMargin = original.leftMargin
            it.rightMargin = original.rightMargin
            it.topMargin = original.topMargin
            it.bottomMargin = original.bottomMargin
        }
        setTextColor(template.currentTextColor)
        setTextSize(TypedValue.COMPLEX_UNIT_PX, template.textSize)
        typeface = template.typeface
        letterSpacing = template.letterSpacing
        includeFontPadding = template.includeFontPadding
        gravity = gravityOverride ?: template.gravity
        setLineSpacing(template.lineSpacingExtra, template.lineSpacingMultiplier)
        applyTo(this, source)
    }

    private fun createTableBlock(template: TextView, table: MarkdownTable): HorizontalScrollView {
        val context = template.context
        val tableLayout = TableLayout(context).apply {
            isShrinkAllColumns = false
            isStretchAllColumns = false
        }
        addTableRow(tableLayout, template, table.headers, table.alignments, header = true, rowIndex = 0)
        table.rows.forEachIndexed { rowIndex, row ->
            addTableRow(tableLayout, template, row, table.alignments, header = false, rowIndex = rowIndex)
        }

        tableLayout.measure(
            View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
            View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED)
        )
        val maxWidth = context.resources.displayMetrics.widthPixels - dp(88f)
        return HorizontalScrollView(context).apply {
            layoutParams = LinearLayout.LayoutParams(
                tableLayout.measuredWidth.coerceAtMost(maxWidth),
                ViewGroup.LayoutParams.WRAP_CONTENT
            ).also {
                it.leftMargin = dp(14f)
                it.rightMargin = dp(14f)
                it.topMargin = dp(4f)
                it.bottomMargin = dp(4f)
            }
            isHorizontalScrollBarEnabled = false
            isFillViewport = false
            isNestedScrollingEnabled = true
            addView(tableLayout)
        }
    }

    private fun createHtmlImageBlock(template: TextView, image: HtmlImageSpec): View {
        val context = template.context
        val maxWidth = context.resources.displayMetrics.widthPixels - dp(88f)
        val requestedWidth = image.width?.coerceAtMost(maxWidth)
        val requestedHeight = image.height?.let { height ->
            if (image.width != null && image.width > maxWidth) height * maxWidth / image.width else height
        }
        val layoutParams = LinearLayout.LayoutParams(
            requestedWidth ?: ViewGroup.LayoutParams.WRAP_CONTENT,
            requestedHeight ?: ViewGroup.LayoutParams.WRAP_CONTENT
        ).apply {
            gravity = image.gravity ?: Gravity.START
            topMargin = dp(4f)
            bottomMargin = dp(4f)
        }

        if (image.url == null) {
            return createTextBlock(template, image.alt.ifBlank { "Изображение" }, image.gravity).apply { this.layoutParams = layoutParams }
        }

        return ImageView(context).apply {
            this.layoutParams = layoutParams
            adjustViewBounds = true
            this.maxWidth = maxWidth
            maxHeight = dp(1024f)
            contentDescription = image.alt
            AvatarLoader.getImageLoader(context).enqueue(
                ImageRequest.Builder(context)
                    .data(image.url)
                    .crossfade(true)
                    .target(this)
                    .build()
            )
            image.linkUrl?.let { link ->
                setOnClickListener { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(link))) }
            }
        }
    }

    private fun addTableRow(
        tableLayout: TableLayout,
        template: TextView,
        cells: List<String>,
        alignments: List<TableAlignment>,
        header: Boolean,
        rowIndex: Int
    ) {
        val baseColor = template.currentTextColor
        val fillAlpha = if (header) 0x1c else if (rowIndex % 2 == 0) 0x0e else 0x08
        val row = TableRow(template.context)
        cells.forEachIndexed { index, value ->
            val cell = createTextBlock(template, value).apply {
                layoutParams = TableRow.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT)
                setPadding(dp(10f), dp(7f), dp(10f), dp(7f))
                gravity = when (alignments[index]) {
                    TableAlignment.START -> Gravity.START
                    TableAlignment.CENTER -> Gravity.CENTER_HORIZONTAL
                    TableAlignment.END -> Gravity.END
                }
                background = GradientDrawable().apply {
                    setColor(ColorUtils.setAlphaComponent(baseColor, fillAlpha))
                    setStroke(dp(1f), ColorUtils.setAlphaComponent(baseColor, 0x33))
                }
                if (header) setTypeface(typeface, Typeface.BOLD)
            }
            row.addView(cell)
        }
        tableLayout.addView(row)
    }

    /** Inline-разбор строки: сначала защищаем inline-код, остальное — через wrap-паттерны. */
    private fun parseInline(text: String, baseColor: Int): SpannableStringBuilder {
        val result = SpannableStringBuilder()
        var last = 0
        for (m in CODE_INLINE.findAll(text)) {
            if (m.range.first > last) {
                result.append(parseInlineNoCode(text.substring(last, m.range.first)))
            }
            val s = result.length
            result.append(m.groupValues[1])
            result.setSpan(TypefaceSpan("monospace"), s, result.length, FLAG)
            result.setSpan(BackgroundColorSpan(codeBg(baseColor)), s, result.length, FLAG)
            last = m.range.last + 1
        }
        if (last < text.length) {
            result.append(parseInlineNoCode(text.substring(last)))
        }
        return result
    }

    private fun parseInlineNoCode(text: String): SpannableStringBuilder {
        val sb = SpannableStringBuilder(text)
        applyHtmlLink(sb)
        applyWrap(sb, HTML_STRONG) { StyleSpan(Typeface.BOLD) }
        applyWrap(sb, HTML_SUB) { RelativeSizeSpan(0.8f) }
        applyWrap(sb, BOLD_STARS) { StyleSpan(Typeface.BOLD) }
        applyWrap(sb, BOLD_UNDERS) { StyleSpan(Typeface.BOLD) }
        applyWrap(sb, STRIKE) { StrikethroughSpan() }
        applyWrap(sb, ITALIC_STAR) { StyleSpan(Typeface.ITALIC) }
        applyWrap(sb, ITALIC_UNDER) { StyleSpan(Typeface.ITALIC) }
        applyLink(sb)
        return sb
    }

    /** Заменяет `маркер+текст+маркер` на текст и вешает span на group(1). */
    private inline fun applyWrap(sb: SpannableStringBuilder, regex: Regex, span: () -> Any) {
        var from = 0
        while (true) {
            val m = regex.find(sb, from) ?: break
            val s = m.range.first
            val content = m.groupValues[1]
            sb.replace(s, m.range.last + 1, content)
            sb.setSpan(span(), s, s + content.length, FLAG)
            from = s + content.length
        }
    }

    private fun applyLink(sb: SpannableStringBuilder) {
        var from = 0
        while (true) {
            val m = LINK.find(sb, from) ?: break
            val s = m.range.first
            val label = m.groupValues[1]
            val url = m.groupValues[2]
            sb.replace(s, m.range.last + 1, label)
            sb.setSpan(URLSpan(normalizeUrl(url)), s, s + label.length, FLAG)
            from = s + label.length
        }
    }

    private fun applyHtmlLink(sb: SpannableStringBuilder) {
        var from = 0
        while (true) {
            val match = HTML_LINK.find(sb, from) ?: break
            val start = match.range.first
            val label = match.groupValues[2]
            val url = htmlAttributes(match.groupValues[1])["href"]
            sb.replace(start, match.range.last + 1, label)
            if (url != null && isSafeHtmlUrl(url)) {
                sb.setSpan(URLSpan(url.trim()), start, start + label.length, FLAG)
            }
            from = start + label.length
        }
    }

    private fun parseHtmlImage(line: String, gravity: Int?): HtmlImageSpec? {
        var imageLine = line
        var linkUrl: String? = null
        HTML_IMAGE_LINK.matchEntire(line)?.let { match ->
            val href = htmlAttributes(match.groupValues[1])["href"]
            linkUrl = href?.takeIf(::isSafeHtmlUrl)?.trim()
            imageLine = match.groupValues[2]
        }
        val image = HTML_IMAGE.matchEntire(imageLine) ?: return null
        val attrs = htmlAttributes(image.groupValues[1])
        val src = attrs["src"] ?: return null
        return HtmlImageSpec(
            url = src.takeIf(::isSafeHtmlImageUrl)?.trim(),
            alt = attrs["alt"].orEmpty(),
            width = attrs["width"]?.toIntOrNull()?.takeIf { it in 1..2048 },
            height = attrs["height"]?.toIntOrNull()?.takeIf { it in 1..2048 },
            gravity = gravity,
            linkUrl = linkUrl
        )
    }

    private fun htmlAttributes(raw: String): Map<String, String> = buildMap {
        HTML_ATTRIBUTE.findAll(raw).forEach { match ->
            put(match.groupValues[1].lowercase(), match.groupValues.drop(2).firstOrNull { it.isNotEmpty() }.orEmpty())
        }
    }

    private fun htmlGravity(value: String?): Int? = when (value?.lowercase()) {
        "center" -> Gravity.CENTER_HORIZONTAL
        "right" -> Gravity.END
        else -> null
    }

    private fun isSafeHtmlUrl(url: String): Boolean =
        Regex("^(https?://|mailto:)", RegexOption.IGNORE_CASE).containsMatchIn(url.trim())

    private fun isSafeHtmlImageUrl(url: String): Boolean =
        Regex("^https?://", RegexOption.IGNORE_CASE).containsMatchIn(url.trim())

    /** Линкует «голые» URL, не затирая уже проставленные markdown-ссылки. */
    private fun linkifyBareUrls(sb: SpannableStringBuilder) {
        val taken = sb.getSpans(0, sb.length, URLSpan::class.java)
            .map { sb.getSpanStart(it) to sb.getSpanEnd(it) }
        val m = Patterns.WEB_URL.matcher(sb)
        while (m.find()) {
            val s = m.start()
            val e = m.end()
            if (taken.any { s < it.second && e > it.first }) continue
            sb.setSpan(URLSpan(normalizeUrl(sb.substring(s, e))), s, e, FLAG)
        }
    }

    private fun normalizeUrl(url: String) =
        if (url.contains("://") || url.startsWith("mailto:")) url else "http://$url"

    private fun isHr(line: String): Boolean {
        val t = line.trim()
        return t.length >= 3 && (t.all { it == '-' } || t.all { it == '*' } || t.all { it == '_' })
    }

    private fun headingLevel(line: String): Int {
        val m = HEADING.find(line) ?: return 0
        return m.groupValues[1].length
    }

    private fun headingScale(level: Int) = when (level) {
        1 -> 1.5f
        2 -> 1.3f
        3 -> 1.15f
        else -> 1.05f
    }

    private sealed interface MessageBlock {
        data class Text(val source: String, val gravity: Int? = null) : MessageBlock
        data class Table(val table: MarkdownTable) : MessageBlock
        data class HtmlImage(val image: HtmlImageSpec) : MessageBlock
    }

    private data class HtmlImageSpec(
        val url: String?,
        val alt: String,
        val width: Int?,
        val height: Int?,
        val gravity: Int?,
        val linkUrl: String?
    )

    private data class ParsedTable(val table: MarkdownTable, val endIndex: Int)
    private data class MarkdownTable(
        val headers: List<String>,
        val alignments: List<TableAlignment>,
        val rows: List<List<String>>
    )

    private enum class TableAlignment { START, CENTER, END }

    /**
     * Перехватывает тап только над ссылкой; остальные тапы отдаёт родителю,
     * чтобы `binding.root.setOnClickListener` (меню сообщения) продолжал срабатывать.
     */
    private object LinkOnlyMovementMethod : LinkMovementMethod() {
        override fun onTouchEvent(widget: TextView, buffer: Spannable, event: MotionEvent): Boolean {
            if (event.action == MotionEvent.ACTION_UP || event.action == MotionEvent.ACTION_DOWN) {
                val layout = widget.layout ?: return false
                val x = event.x.toInt() - widget.totalPaddingLeft + widget.scrollX
                val y = event.y.toInt() - widget.totalPaddingTop + widget.scrollY
                val line = layout.getLineForVertical(y)
                val off = layout.getOffsetForHorizontal(line, x.toFloat())
                val links = buffer.getSpans(off, off, ClickableSpan::class.java)
                if (links.isNotEmpty()) {
                    if (event.action == MotionEvent.ACTION_UP) links[0].onClick(widget)
                    return true
                }
            }
            return false
        }
    }
}
