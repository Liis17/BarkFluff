package com.barkfluff.client

import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityLanguageSettingsBinding
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.send.MediaSendNotification
import com.barkfluff.client.utils.LocaleManager

class LanguageSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityLanguageSettingsBinding
    private lateinit var globalParam: GlobalParam

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityLanguageSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)

        binding.toolbar.setNavigationOnClickListener {
            onBackPressedDispatcher.onBackPressed()
        }

        val checkedId = when (globalParam.appLanguage) {
            GlobalParam.LANGUAGE_RU -> R.id.radioRussian
            GlobalParam.LANGUAGE_EN -> R.id.radioEnglish
            GlobalParam.LANGUAGE_DE -> R.id.radioGerman
            GlobalParam.LANGUAGE_ES -> R.id.radioSpanish
            GlobalParam.LANGUAGE_ZH -> R.id.radioChinese
            else -> R.id.radioSystem
        }
        binding.languageRadioGroup.check(checkedId)

        binding.languageRadioGroup.setOnCheckedChangeListener { _, id ->
            val newLanguage = when (id) {
                R.id.radioRussian -> GlobalParam.LANGUAGE_RU
                R.id.radioEnglish -> GlobalParam.LANGUAGE_EN
                R.id.radioGerman -> GlobalParam.LANGUAGE_DE
                R.id.radioSpanish -> GlobalParam.LANGUAGE_ES
                R.id.radioChinese -> GlobalParam.LANGUAGE_ZH
                else -> GlobalParam.LANGUAGE_SYSTEM
            }
            if (newLanguage == globalParam.appLanguage) return@setOnCheckedChangeListener
            globalParam.appLanguage = newLanguage
            LocaleManager.apply(newLanguage)
        }
    }

    override fun onResume() {
        super.onResume()
        NotificationHelper.createChannels(this)
        MediaSendNotification.ensureChannel(this)
    }
}
