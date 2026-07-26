package com.barkfluff.client.utils

import android.content.Context
import androidx.appcompat.app.AppCompatDelegate
import com.barkfluff.client.data.GlobalParam
import java.io.IOException
import java.util.Locale

/**
 * Читает юридические документы из assets/legal. Файлы туда кладёт gradle-таск
 * copyLegalDocs из Backend/Barkfluff.WebServer/html/legal — тот же источник, что у сайта.
 *
 * Имена файлов: <DOC>.<lang>.md, где lang — ru / en / de / es / zh-CN.
 */
object LegalDocsRepository {

    const val DOC_TERMS = "TERMS_OF_SERVICE"
    const val DOC_PRIVACY = "PRIVACY_POLICY"

    /** Русская версия — оригинал, остальные локали переведены с неё. */
    private const val SOURCE_LANGUAGE = GlobalParam.LANGUAGE_RU

    /** Даты лежат в шапке документа, дальше идут разделы. */
    private const val HEADER_LINES = 8

    /**
     * Строка шапки вида `**Метка:** значение`. Двоеточие в шаблон не входит намеренно:
     * в китайской локали используется полноширинное `：`.
     */
    private val REVISION_LINE = Regex("""^\*\*.+?\*\*\s*(.+?)\s*$""")

    /**
     * Загружает документ на языке приложения. Если для языка файла нет — русская версия
     * (она же оригинал, остальные — переводы).
     */
    fun load(context: Context, doc: String): String {
        val language = currentLanguage()
        return readAsset(context, doc, language)
            ?: readAsset(context, doc, SOURCE_LANGUAGE)
            ?: throw IOException("Не найден legal-документ $doc ни для $language, ни для $SOURCE_LANGUAGE")
    }

    /**
     * Редакция документов — дата последнего обновления из шапки соглашения. Используется как
     * версия принятого согласия: сменилась редакция — согласие спрашиваем заново.
     *
     * Читается всегда из русского оригинала: строка попадает в SharedPreferences, и если брать
     * её из локализованной версии, смена языка приложения выглядела бы как новая редакция.
     */
    fun revision(context: Context): String =
        readAsset(context, DOC_TERMS, SOURCE_LANGUAGE)
            .orEmpty()
            .lineSequence()
            .take(HEADER_LINES)
            .mapNotNull { REVISION_LINE.find(it)?.groupValues?.get(1) }
            .lastOrNull()
            .orEmpty()

    /**
     * Язык документа: сначала явный выбор пользователя в настройках, иначе — активная
     * локаль приложения. Маппинг тегов совпадает с [LocaleManager].
     */
    private fun currentLanguage(): String {
        val explicit = AppCompatDelegate.getApplicationLocales()
            .takeIf { !it.isEmpty }
            ?.get(0)
            ?: Locale.getDefault()

        return when {
            explicit.language == "ru" -> GlobalParam.LANGUAGE_RU
            explicit.language == "de" -> GlobalParam.LANGUAGE_DE
            explicit.language == "es" -> GlobalParam.LANGUAGE_ES
            explicit.language == "zh" -> GlobalParam.LANGUAGE_ZH
            else -> GlobalParam.LANGUAGE_EN
        }
    }

    private fun readAsset(context: Context, doc: String, language: String): String? = try {
        context.assets.open("legal/$doc.$language.md").use {
            flattenTables(it.reader().readText())
        }
    } catch (e: IOException) {
        null
    }

    /**
     * MarkdownRenderer таблицы не поддерживает (см. его kdoc), а в политике их три.
     * Разворачиваем каждую строку таблицы в «**первая ячейка** — остальные», чтобы
     * вместо сырых пайпов пользователь видел читаемый список.
     */
    private fun flattenTables(source: String): String = buildString {
        var header: List<String> = emptyList()

        source.lineSequence().forEach { line ->
            val trimmed = line.trim()
            if (!trimmed.startsWith("|")) {
                header = emptyList()
                appendLine(line)
                return@forEach
            }

            val cells = trimmed.trim('|').split('|').map { it.trim() }
            when {
                // Строка-разделитель `| --- | --- |` — только помечает предыдущую как шапку.
                cells.all { it.isNotEmpty() && it.all { ch -> ch == '-' || ch == ':' } } -> Unit

                header.isEmpty() -> header = cells

                else -> {
                    appendLine("- **${cells.first()}**")
                    cells.drop(1).forEachIndexed { index, value ->
                        if (value.isNotEmpty()) {
                            val label = header.getOrNull(index + 1)
                            appendLine(if (label != null) "  - $label: $value" else "  - $value")
                        }
                    }
                }
            }
        }
    }
}
