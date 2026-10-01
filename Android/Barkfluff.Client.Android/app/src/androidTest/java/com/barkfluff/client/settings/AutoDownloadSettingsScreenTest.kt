package com.barkfluff.client.settings

import androidx.test.core.app.ActivityScenario
import androidx.test.espresso.Espresso.onView
import androidx.test.espresso.action.ViewActions.click
import androidx.test.espresso.action.ViewActions.closeSoftKeyboard
import androidx.test.espresso.action.ViewActions.replaceText
import androidx.test.espresso.action.ViewActions.scrollTo
import androidx.test.espresso.assertion.ViewAssertions.matches
import androidx.test.espresso.matcher.ViewMatchers.hasDescendant
import androidx.test.espresso.matcher.ViewMatchers.isDisplayed
import androidx.test.espresso.matcher.ViewMatchers.withId
import androidx.test.espresso.matcher.ViewMatchers.withText
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import barkfluff.shared.Shared
import com.barkfluff.client.R
import com.barkfluff.client.StorageSettingsActivity
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.domain.media.AutoDownloadMode
import com.barkfluff.client.domain.media.AutoDownloadSettings
import org.hamcrest.Matchers.allOf
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AutoDownloadSettingsScreenTest {
    private val instrumentation get() = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext
    private lateinit var previous: AutoDownloadSettings

    @Before fun resetSettings() {
        instrumentation.runOnMainSync {
            val params = GlobalParam(context)
            previous = params.autoDownloadSettings
            params.autoDownloadSettings = AutoDownloadSettings()
        }
    }

    @After fun restoreSettings() {
        instrumentation.runOnMainSync { GlobalParam(context).autoDownloadSettings = previous }
    }

    @Test fun eachCategoryHasItsOwnModeAndTheChoiceSurvivesRecreation() {
        ActivityScenario.launch(StorageSettingsActivity::class.java).use { scenario ->
            val labels = listOf(R.string.auto_download_photos, R.string.auto_download_gifs,
                R.string.auto_download_videos, R.string.auto_download_audio, R.string.auto_download_voice,
                R.string.auto_download_documents)
            labels.forEachIndexed { index, label ->
                row(label).check(matches(hasDescendant(withText(
                    if (index == 0) R.string.auto_download_any_network else R.string.auto_download_wifi))))
            }
            row(R.string.auto_download_voice).perform(scrollTo(), click())
            onView(withText(R.string.auto_download_manual)).perform(click())
            scenario.recreate()
            row(R.string.auto_download_voice).check(matches(hasDescendant(withText(R.string.auto_download_manual))))
            val settings = GlobalParam(context).autoDownloadSettings
            assertEquals(AutoDownloadMode.MANUAL, settings.mode(Shared.MessageAttachmentType.VOICE))
            assertEquals(AutoDownloadMode.ANY_NETWORK, settings.mode(Shared.MessageAttachmentType.IMAGE))
            assertEquals(AutoDownloadMode.WIFI_ONLY, settings.mode(Shared.MessageAttachmentType.AUDIO))
        }
    }

    @Test fun invalidLimitsKeepTheDialogOpenAndBothBoundaryValuesPersist() {
        ActivityScenario.launch(StorageSettingsActivity::class.java).use { scenario ->
            row(R.string.auto_download_limit).perform(scrollTo(), click())
            for (invalid in listOf("", "0", "513", "999999999999")) {
                onView(withId(R.id.autoDownloadLimitInput)).perform(replaceText(invalid), closeSoftKeyboard())
                onView(withText(R.string.btn_save)).perform(click())
                onView(withText(R.string.auto_download_limit_error)).check(matches(isDisplayed()))
                assertEquals(2, GlobalParam(context).autoDownloadSettings.maxSizeMb)
            }
            onView(withId(R.id.autoDownloadLimitInput)).perform(replaceText("1"), closeSoftKeyboard())
            onView(withText(R.string.btn_save)).perform(click())
            assertEquals(1, GlobalParam(context).autoDownloadSettings.maxSizeMb)
            row(R.string.auto_download_limit).perform(scrollTo(), click())
            onView(withId(R.id.autoDownloadLimitInput)).perform(replaceText("512"), closeSoftKeyboard())
            onView(withText(R.string.btn_save)).perform(click())
            scenario.recreate()
            row(R.string.auto_download_limit).check(matches(hasDescendant(withText(context.getString(R.string.auto_download_limit_value, 512)))))
            assertEquals(512, GlobalParam(context).autoDownloadSettings.maxSizeMb)
        }
    }

    private fun row(label: Int) = onView(allOf(withId(R.id.autoDownloadSettingRow), hasDescendant(withText(label))))
}
