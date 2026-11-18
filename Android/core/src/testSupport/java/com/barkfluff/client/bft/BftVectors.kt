package com.barkfluff.client.bft

import java.io.InputStream

internal object BftVectors {
    @Volatile private var loadedRoot: Map<String, Any?>? = null
    private val classpathRoot: Map<String, Any?> by lazy {
        parse(checkNotNull(javaClass.getResourceAsStream("/bftv1-vectors.json")))
    }

    val root: Map<String, Any?> get() = loadedRoot ?: classpathRoot

    fun load(input: InputStream) {
        loadedRoot = parse(input)
    }

    private fun parse(input: InputStream): Map<String, Any?> {
        @Suppress("UNCHECKED_CAST")
        return JsonParser(input.bufferedReader(Charsets.UTF_8).use { it.readText() }).parse() as Map<String, Any?>
    }

    fun objectAt(parent: Map<String, Any?>, key: String): Map<String, Any?> = parent.getValue(key) as Map<String, Any?>
    fun arrayAt(parent: Map<String, Any?>, key: String): List<Any?> = parent.getValue(key) as List<Any?>
    fun stringAt(parent: Map<String, Any?>, key: String): String = parent.getValue(key) as String
    fun intAt(parent: Map<String, Any?>, key: String): Int = (parent.getValue(key) as Number).toInt()
    fun longAt(parent: Map<String, Any?>, key: String): Long = (parent.getValue(key) as Number).toLong()
    fun hex(value: Any?): ByteArray = (value as String).chunked(2).map { it.toInt(16).toByte() }.toByteArray()
    fun hexAt(parent: Map<String, Any?>, key: String): ByteArray = hex(parent.getValue(key))

    private class JsonParser(private val source: String) {
        private var index = 0

        fun parse(): Any? {
            val value = value()
            whitespace()
            check(index == source.length) { "Trailing JSON input" }
            return value
        }

        private fun value(): Any? {
            whitespace()
            return when (source[index]) {
                '{' -> objectValue()
                '[' -> arrayValue()
                '"' -> stringValue()
                't' -> literal("true", true)
                'f' -> literal("false", false)
                'n' -> literal("null", null)
                else -> numberValue()
            }
        }

        private fun objectValue(): Map<String, Any?> {
            index++
            whitespace()
            val result = linkedMapOf<String, Any?>()
            if (take('}')) return result
            while (true) {
                whitespace()
                val key = stringValue()
                whitespace()
                check(take(':'))
                result[key] = value()
                whitespace()
                if (take('}')) return result
                check(take(','))
            }
        }

        private fun arrayValue(): List<Any?> {
            index++
            whitespace()
            val result = mutableListOf<Any?>()
            if (take(']')) return result
            while (true) {
                result += value()
                whitespace()
                if (take(']')) return result
                check(take(','))
            }
        }

        private fun stringValue(): String {
            check(take('"'))
            return buildString {
                while (index < source.length) {
                    val c = source[index++]
                    when (c) {
                        '"' -> return@buildString
                        '\\' -> {
                            when (val escaped = source[index++]) {
                                '"', '\\', '/' -> append(escaped)
                                'b' -> append('\b')
                                'f' -> append('\u000c')
                                'n' -> append('\n')
                                'r' -> append('\r')
                                't' -> append('\t')
                                'u' -> append(source.substring(index, index + 4).toInt(16).toChar().also { index += 4 })
                                else -> error("Invalid JSON escape")
                            }
                        }
                        else -> append(c)
                    }
                }
                error("Unterminated JSON string")
            }
        }

        private fun numberValue(): Number {
            val start = index
            while (index < source.length && source[index] in "-+0123456789.eE") index++
            val text = source.substring(start, index)
            return if (text.any { it == '.' || it == 'e' || it == 'E' }) text.toDouble() else text.toLong()
        }

        private fun <T> literal(text: String, result: T): T {
            check(source.startsWith(text, index))
            index += text.length
            return result
        }

        private fun whitespace() {
            while (index < source.length && source[index].isWhitespace()) index++
        }

        private fun take(c: Char): Boolean = if (index < source.length && source[index] == c) {
            index++
            true
        } else false
    }
}

internal class OneByteInputStream(private val source: ByteArray) : InputStream() {
    private var offset = 0
    override fun read(): Int = if (offset == source.size) -1 else source[offset++].toInt() and 0xff
    override fun read(bytes: ByteArray, start: Int, length: Int): Int {
        if (length == 0) return 0
        if (offset == source.size) return -1
        bytes[start] = source[offset++]
        return 1
    }
}
