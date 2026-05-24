package com.barkfluff.clientv2.ui.navigation

/** Маршруты навигации MVP: Splash → Welcome → SelectServer → Login → Chats → Chat. */
object Routes {
    const val SPLASH = "splash"
    const val WELCOME = "welcome"
    const val SELECT_SERVER = "select_server"
    const val LOGIN = "login"
    const val CHATS = "chats"
    const val PROFILE = "profile"
    const val PROFILE_EDIT = "profile_edit"
    const val SETTINGS = "settings"
    const val PRIVACY = "privacy"
    const val SEARCH = "search"

    const val CHAT_ARG_ID = "chatId"
    const val CHAT = "chat/{$CHAT_ARG_ID}"
    fun chat(chatId: String) = "chat/$chatId"
}
