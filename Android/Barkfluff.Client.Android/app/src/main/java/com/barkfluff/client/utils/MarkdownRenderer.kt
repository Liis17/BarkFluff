package com.barkfluff.client.utils

import android.content.res.Resources
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
import android.view.MotionEvent
import android.widget.TextView
import androidx.core.graphics.ColorUtils

/**
 * Базовый markdown-рендер для пузырей сообщений. Бэкенд хранит обычный текст с
 * символами разметки — интерпретация целиком на клиенте.
 *
 * Поддержка (line-based): заголовки, маркированные/нумерованные списки, цитаты,
 * горизонтальные линии, блоки кода + inline (**bold**, *italic*, ~~strike~~,
 * `code`, [текст](url)) и автолинковка «голых» URL.
 *
 * Вложенные списки, таблицы и HTML не поддерживаются (вне «базового» набора).
 */
object MarkdownRenderer {

    private const val FLAG = Spanned.SPAN_EXCLUSIVE_EXCLUSIVE

    private val HEADING = Regex("^(#{1,6})\\s+")
    private val ORDERED = Regex("^\\s*(\\d+)\\.\\s+(.*)")
    private val UNORDERED = Regex("^\\s*[-*+]\\s+(.*)")
    private val CODE_INLINE = Regex("`([^`\\n]+)`")
    private val BOLD_STARS = Regex("\\*\\*([^*]+?)\\*\\*")
    private val BOLD_UNDERS = Regex("__([^_]+?)__")
    private val STRIKE = Regex("~~([^~]+?)~~")
    private val ITALIC_STAR = Regex("\\*([^*]+?)\\*")
    private val ITALIC_UNDER = Regex("_([^_]+?)_")
    private val LINK = Regex("\\[([^\\]]+)\\]\\(([^)\\s]+)\\)")

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
