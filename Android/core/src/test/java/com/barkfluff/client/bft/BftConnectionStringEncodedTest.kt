package com.barkfluff.client.bft

import org.junit.Assert.assertEquals
import org.junit.Assert.fail
import org.junit.Test

class BftConnectionStringEncodedTest {
    @Test
    fun encodedLinkRoundTripsWithClassicForm() {
        val parsed = BftConnectionString.parse(ENCODED)
        assertEquals("proxy.example.org", parsed.host)
        assertEquals(8443, parsed.port)
        assertEquals("t0ken/+ 1", parsed.token)
        assertEquals("Пёс 🌐", parsed.name)
        assertEquals(CLASSIC, parsed.format())
        assertEquals(ENCODED, BftConnectionString.parse(CLASSIC).formatEncoded())
    }

    @Test
    fun invalidEncodedLinksAreRejected() {
        listOf(
            "bft://",
            "bft://QUJD", // "ABC": no key pair or authority
            "bft://" + java.util.Base64.getUrlEncoder().withoutPadding().encodeToString(CLASSIC.toByteArray()), // nested bft://
            "bft://A", // invalid base64 length
            "bft://_w", // 0xFF is not UTF-8
            "bft://" + "A".repeat(4100)
        ).forEach { link ->
            try {
                BftConnectionString.parse(link)
                fail("Expected rejection: $link")
            } catch (_: IllegalArgumentException) {
            }
        }
    }

    private companion object {
        const val CLASSIC = "bft://B6N8vBQgk8i3VdwbEOhstCY3StFqqFPtC9_AsrhtHHw.ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8@proxy.example.org:8443?token=t0ken%2F%2B%201&name=%D0%9F%D1%91%D1%81%20%F0%9F%8C%90"
        const val ENCODED = "bft://QjZOOHZCUWdrOGkzVmR3YkVPaHN0Q1kzU3RGcXFGUHRDOV9Bc3JodEhIdy5JQ0VpSXlRbEppY29LU29yTEMwdUx6QXhNak0wTlRZM09EazZPenc5UGo4QHByb3h5LmV4YW1wbGUub3JnOjg0NDM_dG9rZW49dDBrZW4lMkYlMkIlMjAxJm5hbWU9JUQwJTlGJUQxJTkxJUQxJTgxJTIwJUYwJTlGJThDJTkw"
    }
}
