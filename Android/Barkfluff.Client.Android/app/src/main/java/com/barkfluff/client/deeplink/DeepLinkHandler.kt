package com.barkfluff.client.deeplink

import android.net.Uri

sealed class DeepLinkCommand {
    data class OpenUserChat(val username: String) : DeepLinkCommand()
    object Unknown : DeepLinkCommand()
}

object DeepLinkHandler {

    /**
     * Парсит URI вида bf://user-username=li_is или bfdev://user-username=li_is
     * Формат: scheme://command=argument
     */
    fun parse(uri: Uri): DeepLinkCommand {
        // uri.schemeSpecificPart даёт "//user-username=li_is" для bf://user-username=li_is
        val raw = uri.schemeSpecificPart
            ?.removePrefix("//")
            ?.trimEnd('/')
            ?: return DeepLinkCommand.Unknown

        val separatorIndex = raw.indexOf('=')
        if (separatorIndex < 0) return DeepLinkCommand.Unknown

        val command = raw.substring(0, separatorIndex)
        val argument = raw.substring(separatorIndex + 1)

        if (argument.isBlank()) return DeepLinkCommand.Unknown

        return when (command) {
            "user-username" -> DeepLinkCommand.OpenUserChat(argument)
            else -> DeepLinkCommand.Unknown
        }
    }
}
