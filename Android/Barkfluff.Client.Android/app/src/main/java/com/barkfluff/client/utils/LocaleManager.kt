package com.barkfluff.client.utils

import androidx.appcompat.app.AppCompatDelegate
import androidx.core.os.LocaleListCompat
import com.barkfluff.client.data.GlobalParam

/**
 * Применяет языковую настройку приложения через AppCompat per-app locales.
 *
 * Значение "system" сбрасывает override и возвращает приложение к системной локали.
 * Любой другой код (например, "ru", "en") выставляет конкретную локаль —
 * AppCompat сам пересоздаёт Activity-стек.
 */
object LocaleManager {

    fun apply(language: String) {
        val locales = when (language) {
            GlobalParam.LANGUAGE_RU -> LocaleListCompat.forLanguageTags("ru")
            GlobalParam.LANGUAGE_EN -> LocaleListCompat.forLanguageTags("en")
            else -> LocaleListCompat.getEmptyLocaleList()
        }
        AppCompatDelegate.setApplicationLocales(locales)
    }
}
