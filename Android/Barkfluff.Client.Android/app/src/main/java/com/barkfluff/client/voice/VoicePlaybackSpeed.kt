package com.barkfluff.client.voice

import android.content.Context
import java.text.NumberFormat

object VoicePlaybackSpeed {
    private fun preferences(context: Context) = context.getSharedPreferences("voice_playback", Context.MODE_PRIVATE)
    fun read(context: Context): Float = preferences(context).getFloat("speed", 1f)
        .takeIf { it == 1f || it == 1.5f || it == 2f } ?: 1f
    fun cycle(context: Context): Float {
        val next = when (read(context)) { 1f -> 1.5f; 1.5f -> 2f; else -> 1f }
        preferences(context).edit().putFloat("speed", next).apply()
        return next
    }
    fun label(context: Context, speed: Float = read(context)): String =
        NumberFormat.getNumberInstance(context.resources.configuration.locales[0]).format(speed) + "×"
}
