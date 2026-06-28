package com.barkfluff.client.utils

data class AppVersion(
    val major: Int,
    val minor: Int,
    val patch: Int,
    val isBeta: Boolean
) : Comparable<AppVersion> {

    companion object {
        fun parse(version: String?): AppVersion? {
            if (version.isNullOrBlank()) return null
            val isBeta = version.contains("beta", ignoreCase = true)
            val clean = version.replace("beta", "", ignoreCase = true).trim()
            val parts = clean.split(".")
            if (parts.size != 3) return null
            return AppVersion(
                major = parts[0].toIntOrNull() ?: return null,
                minor = parts[1].toIntOrNull() ?: return null,
                patch = parts[2].toIntOrNull() ?: return null,
                isBeta = isBeta
            )
        }
    }

    override fun compareTo(other: AppVersion): Int {
        if (major != other.major) return major.compareTo(other.major)
        if (minor != other.minor) return minor.compareTo(other.minor)
        if (patch != other.patch) return patch.compareTo(other.patch)
        // Release > Beta при одинаковых числовых версиях
        if (isBeta && !other.isBeta) return -1
        if (!isBeta && other.isBeta) return 1
        return 0
    }

    override fun toString(): String {
        val base = "$major.$minor.$patch"
        return if (isBeta) "$base beta" else base
    }
}
