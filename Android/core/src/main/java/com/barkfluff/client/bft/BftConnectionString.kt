package com.barkfluff.client.bft

import java.io.ByteArrayOutputStream
import java.net.Inet6Address
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

data class BftConnectionSettings(
    val staticPublicKey: ByteArray,
    val obfsKey: ByteArray,
    val host: String,
    val port: Int,
    val token: String? = null,
    val name: String? = null
) {
    fun format(): String = BftConnectionString.format(this)

    fun formatEncoded(): String = BftConnectionString.formatEncoded(this)

    override fun toString(): String =
        "BftConnectionSettings(host=$host, port=$port, token=${if (token == null) "null" else "[redacted]"}, name=$name)"
}

object BftConnectionString {
    private val unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~"

    @JvmStatic
    fun parse(link: String): BftConnectionSettings {
        val value = if (link.startsWith("bft://") && isEncodedBody(link.substring(6))) {
            "bft://" + decodeBody(link.substring(6))
        } else {
            link
        }
        require(value.startsWith("bft://") && '#' !in value && '/' !in value.substring(6)) {
            "Invalid BFT connection string"
        }
        val queryIndex = value.indexOf('?', 6)
        val authority = if (queryIndex < 0) value.substring(6) else value.substring(6, queryIndex)
        var token: String? = null
        var name: String? = null
        if (queryIndex >= 0) {
            var tokenSeen = false
            var nameSeen = false
            for (parameter in value.substring(queryIndex + 1).split('&')) {
                val separator = parameter.indexOf('=')
                require(separator > 0) { "Invalid BFT query parameter" }
                val key = parameter.substring(0, separator)
                val encoded = parameter.substring(separator + 1)
                when (key) {
                    "token" -> {
                        require(!tokenSeen) { "Repeated BFT token parameter" }
                        tokenSeen = true
                        token = decodeParameter(encoded, 1, 256)
                    }
                    "name" -> {
                        require(!nameSeen) { "Repeated BFT name parameter" }
                        nameSeen = true
                        name = decodeParameter(encoded, 1, 256).also(::validateName)
                    }
                    else -> throw IllegalArgumentException("Unknown BFT query parameter")
                }
            }
        }

        val at = authority.indexOf('@')
        require(at > 0 && at == authority.lastIndexOf('@') && at < authority.lastIndex) {
            "Invalid BFT authority"
        }
        val keyPart = authority.substring(0, at)
        val dot = keyPart.indexOf('.')
        require(dot > 0 && dot == keyPart.lastIndexOf('.') && dot < keyPart.lastIndex) {
            "Invalid BFT key pair"
        }
        val staticKey = decodeKey(keyPart.substring(0, dot))
        val obfsKey = decodeKey(keyPart.substring(dot + 1))
        val (host, port) = parseHostPort(authority.substring(at + 1))
        return BftConnectionSettings(staticKey, obfsKey, host, port, token, name)
    }

    @JvmStatic
    fun format(settings: BftConnectionSettings): String {
        require(settings.staticPublicKey.size == 32 && settings.obfsKey.size == 32) { "BFT keys must be 32 bytes" }
        require(settings.port in 1..65535) { "BFT port is out of range" }
        val host = if (':' in settings.host) "[${settings.host}]" else settings.host
        if (':' in settings.host) {
            require(parseIpv6(settings.host) != null) { "Invalid IPv6 host" }
        } else {
            validateDnsHost(settings.host)
        }
        val query = buildList {
            settings.token?.let { add("token=${encodeParameter(it, 1, 256)}") }
            settings.name?.let {
                validateName(it)
                add("name=${encodeParameter(it, 1, 256)}")
            }
        }
        return "bft://${encodeKey(settings.staticPublicKey)}.${encodeKey(settings.obfsKey)}@$host:${settings.port}" +
            if (query.isEmpty()) "" else "?${query.joinToString("&")}"
    }

    /** Formats the link as `bft://` followed by the base64url (unpadded) encoding of the classic link body. */
    @JvmStatic
    fun formatEncoded(settings: BftConnectionSettings): String =
        "bft://" + java.util.Base64.getUrlEncoder().withoutPadding()
            .encodeToString(format(settings).substring(6).toByteArray(StandardCharsets.UTF_8))

    private fun isEncodedBody(body: String): Boolean {
        val trimmed = body.trimEnd('=')
        return trimmed.isNotEmpty() && body.length - trimmed.length <= 2 &&
            trimmed.all { it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' || it == '-' || it == '_' }
    }

    private fun decodeBody(body: String): String {
        require(body.length <= MAX_ENCODED_BODY_LENGTH) { "Encoded BFT connection string is too long" }
        val bytes = try {
            java.util.Base64.getUrlDecoder().decode(body.trimEnd('='))
        } catch (error: IllegalArgumentException) {
            throw IllegalArgumentException("Invalid encoded BFT connection string", error)
        }
        return try {
            StandardCharsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes)).toString()
        } catch (error: CharacterCodingException) {
            throw IllegalArgumentException("Encoded BFT connection string is not valid UTF-8", error)
        }
    }

    private fun parseHostPort(value: String): Pair<String, Int> {
        val host: String
        val portText: String
        if (value.startsWith('[')) {
            val end = value.indexOf(']')
            require(end > 1 && end + 1 < value.length && value[end + 1] == ':') { "Invalid IPv6 host" }
            host = value.substring(1, end)
            require(parseIpv6(host) != null) { "Invalid IPv6 host" }
            portText = value.substring(end + 2)
        } else {
            val colon = value.lastIndexOf(':')
            require(colon > 0 && colon == value.indexOf(':')) { "Host and port are required; IPv6 must be bracketed" }
            host = value.substring(0, colon)
            validateDnsHost(host)
            portText = value.substring(colon + 1)
        }
        require(portText.isNotEmpty() && portText.all { it in '0'..'9' }) { "Invalid BFT port" }
        val port = portText.toIntOrNull()
        require(port != null && port in 1..65535) { "Invalid BFT port" }
        return host to port
    }

    private fun validateDnsHost(host: String) {
        require(host.length in 1..253 && host.all { it.code < 128 }) { "Invalid BFT host" }
        require(host.split('.').all { label ->
            label.length in 1..63 && label.first().isLetterOrDigit() && label.last().isLetterOrDigit() &&
                label.all { it.isLetterOrDigit() || it == '-' }
        }) { "Invalid BFT host" }
    }

    private fun parseIpv6(host: String): Inet6Address? = try {
        if (':' !in host || '%' in host) null else InetAddress.getByName(host) as? Inet6Address
    } catch (_: Exception) {
        null
    }

    private fun decodeKey(value: String): ByteArray {
        require(value.length == 43 && value.all { it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' || it == '-' || it == '_' }) {
            "BFT keys must be unpadded base64url"
        }
        val decoded = try {
            java.util.Base64.getUrlDecoder().decode(value + "=")
        } catch (error: IllegalArgumentException) {
            throw IllegalArgumentException("Invalid BFT key encoding", error)
        }
        require(decoded.size == 32 && encodeKey(decoded) == value) { "BFT keys must be 32 bytes" }
        return decoded
    }

    private fun encodeKey(value: ByteArray): String = java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(value)

    private fun decodeParameter(value: String, minBytes: Int, maxBytes: Int): String {
        require(value.length <= maxBytes * 3) { "BFT query parameter is too long" }
        val bytes = ByteArrayOutputStream(value.length)
        var index = 0
        while (index < value.length) {
            val c = value[index]
            if (c == '%') {
                require(index + 2 < value.length) { "Invalid percent escape" }
                val hi = value[index + 1].digitToIntOrNull(16)
                val lo = value[index + 2].digitToIntOrNull(16)
                require(hi != null && lo != null) { "Invalid percent escape" }
                bytes.write((hi shl 4) or lo)
                index += 3
            } else {
                require(c.code < 128 && c in unreserved) { "BFT query characters must be percent-encoded" }
                bytes.write(c.code)
                index++
            }
        }
        val decoded = try {
            StandardCharsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(bytes.toByteArray())).toString()
        } catch (error: CharacterCodingException) {
            throw IllegalArgumentException("BFT query value is not valid UTF-8", error)
        }
        val encoded = strictUtf8(decoded)
        require(encoded.size in minBytes..maxBytes) { "BFT query value length is out of range" }
        return decoded
    }

    private fun encodeParameter(value: String, minBytes: Int, maxBytes: Int): String {
        val bytes = strictUtf8(value)
        require(bytes.size in minBytes..maxBytes) { "BFT query value length is out of range" }
        return buildString(bytes.size) {
            for (byte in bytes) {
                val unsigned = byte.toInt() and 0xff
                if (unsigned < 128 && unsigned.toChar() in unreserved) append(unsigned.toChar())
                else append('%').append(HEX[unsigned ushr 4]).append(HEX[unsigned and 0x0f])
            }
        }
    }

    private fun strictUtf8(value: String): ByteArray = try {
        StandardCharsets.UTF_8.newEncoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
            .encode(java.nio.CharBuffer.wrap(value)).let { buffer -> ByteArray(buffer.remaining()).also(buffer::get) }
    } catch (error: CharacterCodingException) {
        throw IllegalArgumentException("BFT query value is not valid Unicode", error)
    }

    private fun validateName(name: String) {
        val count = name.codePointCount(0, name.length)
        require(count in 1..64 && name.codePoints().noneMatch(Character::isISOControl)) {
            "BFT name must contain 1–64 Unicode code points without controls"
        }
    }

    private const val HEX = "0123456789ABCDEF"
    private const val MAX_ENCODED_BODY_LENGTH = 4096
}
