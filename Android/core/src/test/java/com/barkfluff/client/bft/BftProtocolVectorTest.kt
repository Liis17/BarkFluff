package com.barkfluff.client.bft

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.IOException

class BftProtocolVectorTest {
    @Test
    fun connectionStringVectorsParseFormatAndRejectEveryInvalidEntry() {
        val vector = BftVectors.objectAt(BftVectors.root, "connectionString")
        val settings = BftConnectionString.parse(BftVectors.stringAt(vector, "string"))
        assertArrayEquals(BftVectors.hexAt(vector, "staticPublicKey"), settings.staticPublicKey)
        assertArrayEquals(BftVectors.hexAt(vector, "obfsKey"), settings.obfsKey)
        assertEquals(BftVectors.stringAt(vector, "host"), settings.host)
        assertEquals(BftVectors.intAt(vector, "port"), settings.port)
        assertEquals(BftVectors.stringAt(vector, "token"), settings.token)
        assertEquals(BftVectors.stringAt(vector, "name"), settings.name)
        assertEquals(BftVectors.stringAt(vector, "formattedString"), settings.format())
        for (invalid in BftVectors.arrayAt(vector, "invalid").map { it as String }) {
            assertThrows<IllegalArgumentException>(invalid) { BftConnectionString.parse(invalid) }
        }
    }

    @Test
    fun obfuscationKeyAndRecordVectorsMatchExactly() {
        val vector = BftVectors.objectAt(BftVectors.root, "obfsKeys")
        val keys = BftObfuscationKeys.derive(BftVectors.hexAt(vector, "obfsKey"), BftVectors.hexAt(vector, "salt"))
        try {
            assertArrayEquals(BftVectors.hexAt(vector, "c2sLen"), keys.c2sLength)
            assertArrayEquals(BftVectors.hexAt(vector, "c2sData"), keys.c2sData)
            assertArrayEquals(BftVectors.hexAt(vector, "s2cLen"), keys.s2cLength)
            assertArrayEquals(BftVectors.hexAt(vector, "s2cData"), keys.s2cData)
            for (record in BftVectors.arrayAt(BftVectors.root, "records").map { it as Map<String, Any?> }) {
                val c2s = BftVectors.stringAt(record, "keys") == "c2s"
                val sequence = BftVectors.longAt(record, "n")
                val payload = BftVectors.hexAt(record, "payload")
                val padding = BftVectors.intAt(record, "padLen")
                val encoded = BftObfuscatedRecords.encrypt(keys, c2s, sequence, payload, padding)
                assertArrayEquals(BftVectors.hexAt(record, "record"), encoded)
                assertArrayEquals(payload, BftObfuscatedRecords.decrypt(keys, c2s, sequence, encoded))
                encoded.fill(0)
            }
        } finally {
            keys.close()
        }
    }

    @Test
    fun recordsReadAcrossSingleByteReadsAndRejectAllNegativeVectors() {
        val keyVector = BftVectors.objectAt(BftVectors.root, "obfsKeys")
        val keys = BftObfuscationKeys.derive(BftVectors.hexAt(keyVector, "obfsKey"), BftVectors.hexAt(keyVector, "salt"))
        try {
            val valid = BftVectors.arrayAt(BftVectors.root, "records").take(2).map { it as Map<String, Any?> }
            val joined = valid.flatMap { BftVectors.hexAt(it, "record").asIterable() }.toByteArray()
            val reader = BftObfuscatedRecordReader(OneByteInputStream(joined), keys, clientToServer = true)
            assertArrayEquals("first".toByteArray(), reader.read())
            assertArrayEquals("second".toByteArray(), reader.read())
            assertEquals(null, reader.read())

            val negative = BftVectors.objectAt(BftVectors.root, "negative")
            for (entry in BftVectors.arrayAt(negative, "records").map { it as Map<String, Any?> }) {
                val candidate = BftObfuscatedRecordReader(
                    ByteArrayInputStream(BftVectors.hexAt(entry, "stream")), keys, clientToServer = true,
                    initialSequence = BftVectors.longAt(entry, "startN")
                )
                if (BftVectors.stringAt(entry, "name") == "replay") {
                    assertArrayEquals("first", "first".toByteArray(), candidate.read())
                    assertThrows<IOException>("replay") { candidate.read() }
                } else {
                    assertThrows<IOException>(BftVectors.stringAt(entry, "name")) { candidate.read() }
                }
            }
        } finally {
            keys.close()
        }
    }

    @Test
    fun clientNoiseHandshakeAcceptsRecordedServerFlightAndMatchesSplitTransportKeys() {
        val connection = BftVectors.objectAt(BftVectors.root, "connectionString")
        val handshake = BftVectors.objectAt(BftVectors.root, "handshake")
        val settings = BftConnectionSettings(
            BftVectors.hexAt(connection, "staticPublicKey"), BftVectors.hexAt(connection, "obfsKey"),
            "proxy.example.org", 8443
        )
        val options = BftClientHandshakeOptions(
            random = ZeroRandom,
            timestampSeconds = BftVectors.longAt(handshake, "timestamp"),
            salt = BftVectors.hexAt(BftVectors.objectAt(BftVectors.root, "obfsKeys"), "salt"),
            ephemeralPrivateKey = BftVectors.hexAt(handshake, "clientEphemeralPrivate"),
            firstRecordPaddingLength = BftVectors.intAt(handshake, "clientPadLen")
        )
        val flight = BftClientHandshake.createFlight(settings, options)
        assertArrayEquals(BftVectors.hexAt(handshake, "clientFlight"), flight.salt + flight.record)

        val serverTransport = BftVectors.arrayAt(BftVectors.root, "transport").map { it as Map<String, Any?> }
            .single { BftVectors.stringAt(it, "direction") == "s2c" && BftVectors.longAt(it, "noiseN") == 0L }
        val response = BftVectors.hexAt(handshake, "serverFirstRecord") + BftVectors.hexAt(serverTransport, "record")
        val output = ByteArrayOutputStream()
        val session = BftClientHandshake.connect(ByteArrayInputStream(response), output, settings, options)
        try {
            assertArrayEquals(BftVectors.hexAt(handshake, "handshakeHash"), session.handshakeHash)
            assertArrayEquals(BftVectors.hexAt(serverTransport, "plaintext"), session.readMessage())

            val clientTransport = BftVectors.arrayAt(BftVectors.root, "transport").map { it as Map<String, Any?> }
                .single { BftVectors.stringAt(it, "direction") == "c2s" && BftVectors.longAt(it, "noiseN") == 0L }
            val flightLength = BftVectors.hexAt(handshake, "clientFlight").size
            session.writeMessage(BftVectors.hexAt(clientTransport, "plaintext"))
            assertArrayEquals(
                BftVectors.hexAt(clientTransport, "record"),
                output.toByteArray().copyOfRange(flightLength, output.size())
            )
        } finally {
            session.close()
        }
    }

    @Test
    fun transportVectorsMatchBothDirectionsAndTheRekeyBoundary() {
        val keyVector = BftVectors.objectAt(BftVectors.root, "obfsKeys")
        val keys = BftObfuscationKeys.derive(BftVectors.hexAt(keyVector, "obfsKey"), BftVectors.hexAt(keyVector, "salt"))
        val vectors = BftVectors.arrayAt(BftVectors.root, "transport").map { it as Map<String, Any?> }
        try {
            val c2s = BftNoiseCipherState(BftVectors.hexAt(BftVectors.root.let { BftVectors.objectAt(it, "handshake") }, "c2sKey"))
            try {
                vectors.filter { BftVectors.stringAt(it, "direction") == "c2s" && BftVectors.longAt(it, "noiseN") < 65535 }
                    .forEach { assertTransportVector(keys, c2s, it) }
            } finally {
                c2s.close()
            }

            val s2c = BftNoiseCipherState(BftVectors.hexAt(BftVectors.objectAt(BftVectors.root, "handshake"), "s2cKey"))
            try {
                vectors.filter { BftVectors.stringAt(it, "direction") == "s2c" }
                    .forEach { assertTransportVector(keys, s2c, it) }
            } finally {
                s2c.close()
            }

            val boundary = BftNoiseCipherState(
                BftVectors.hexAt(BftVectors.objectAt(BftVectors.root, "handshake"), "c2sKey"), initialCounter = 65535
            )
            try {
                vectors.filter { BftVectors.stringAt(it, "direction") == "c2s" && BftVectors.longAt(it, "noiseN") >= 65535 }
                    .forEach { assertTransportVector(keys, boundary, it) }
            } finally {
                boundary.close()
            }
        } finally {
            keys.close()
        }
    }

    @Test
    fun frameVectorsAndAllNegativeFramesMatchTheCodec() {
        val frameVectors = BftVectors.objectAt(BftVectors.root, "frames")
        for ((name, raw) in frameVectors) {
            val encoded = BftVectors.hex(raw)
            val decoded = BftFrameCodec.decode(encoded)
            assertEquals(name, 1, decoded.size)
            assertArrayEquals(name, encoded, BftFrameCodec.encode(decoded.single()))
        }
        val negative = BftVectors.objectAt(BftVectors.root, "negative")
        for (entry in BftVectors.arrayAt(negative, "frames").map { it as Map<String, Any?> }) {
            assertThrows<IOException>(BftVectors.stringAt(entry, "name")) {
                BftFrameCodec.decode(BftVectors.hexAt(entry, "bytes"))
            }
        }
        val hello = BftFrameCodec.parseHello(BftFrameCodec.decode(BftVectors.hex(frameVectors.getValue("HELLO"))).single().body)
        assertArrayEquals(BftVectors.hexAt(BftVectors.root, "tlsSpkiSha256"), hello.tlsSpkiSha256)
        assertEquals(listOf(1, 4, 5), hello.serviceIds)
    }

    @Test
    fun zeroResultX25519IsRejected() {
        assertThrows<IOException>("small-order X25519 public key") {
            BftCrypto.x25519(ByteArray(32) { 7 }, ByteArray(32))
        }
        assertTrue(BftVectors.root.isNotEmpty())
    }

    private fun assertTransportVector(
        keys: BftObfuscationKeys,
        cipher: BftNoiseCipherState,
        vector: Map<String, Any?>
    ) {
        val direction = BftVectors.stringAt(vector, "direction")
        val clientToServer = direction == "c2s"
        val noise = cipher.encrypt(BftVectors.hexAt(vector, "plaintext"))
        val record = BftObfuscatedRecords.encrypt(
            keys, clientToServer, BftVectors.longAt(vector, "recordN"), noise, BftVectors.intAt(vector, "padLen")
        )
        try {
            assertArrayEquals("$direction/${BftVectors.longAt(vector, "noiseN")}", BftVectors.hexAt(vector, "record"), record)
        } finally {
            noise.fill(0)
            record.fill(0)
        }
    }

    companion object {
        private object ZeroRandom : BftRandomSource {
            override fun fill(destination: ByteArray) = destination.fill(0)
        }
    }
}

private inline fun <reified T : Throwable> assertThrows(message: String, action: () -> Unit) {
    try {
        action()
    } catch (error: Throwable) {
        if (error is T) return
        throw AssertionError("$message: expected ${T::class.java.name}, got $error", error)
    }
    throw AssertionError("$message: expected ${T::class.java.name}")
}
