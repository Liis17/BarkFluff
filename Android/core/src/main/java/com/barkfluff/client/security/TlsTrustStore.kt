package com.barkfluff.client.security

import android.content.Context
import android.content.SharedPreferences
import android.util.Base64

data class TlsPin(
    val host: String,
    val spkiSha256: String,
    val acceptedAtMillis: Long
)

/** Persists the user-approved public-key pin for one exact hostname. */
class TlsTrustStore(context: Context) {
    private val preferences: SharedPreferences = context.applicationContext.getSharedPreferences(
        PREFERENCES_NAME,
        Context.MODE_PRIVATE
    )

    fun pinFor(host: String): TlsPin? {
        val canonicalHost = TlsEndpoint.canonicalHost(host)
        val stored = preferences.getString(key(canonicalHost), null) ?: return null
        val delimiter = stored.lastIndexOf('|')
        if (delimiter <= 0 || delimiter == stored.lastIndex) return null
        val acceptedAt = stored.substring(delimiter + 1).toLongOrNull() ?: return null
        return TlsPin(canonicalHost, stored.substring(0, delimiter), acceptedAt)
    }

    fun replacePin(host: String, spkiSha256: String, acceptedAtMillis: Long = System.currentTimeMillis()) {
        require(spkiSha256.startsWith("sha256/")) { "Expected an SPKI SHA-256 pin" }
        val canonicalHost = TlsEndpoint.canonicalHost(host)
        preferences.edit()
            .putString(key(canonicalHost), "$spkiSha256|$acceptedAtMillis")
            .apply()
    }

    fun removePin(host: String) {
        preferences.edit().remove(key(TlsEndpoint.canonicalHost(host))).apply()
    }

    private fun key(host: String): String {
        val encodedHost = Base64.encodeToString(host.toByteArray(Charsets.UTF_8), Base64.URL_SAFE or Base64.NO_WRAP)
        return "$PIN_PREFIX$encodedHost"
    }

    private companion object {
        const val PREFERENCES_NAME = "barkfluff_tls_pins"
        const val PIN_PREFIX = "pin_"
    }
}
