package com.barkfluff.client.search

import android.view.View
import android.widget.CheckBox
import android.widget.RadioButton
import android.widget.RadioGroup
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class AttachmentFilterLinkTest {
    private lateinit var group: RadioGroup
    private lateinit var any: RadioButton
    private lateinit var with: RadioButton
    private lateinit var without: RadioButton
    private lateinit var checks: List<CheckBox>

    @Before fun setUp() = onMain {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        group = RadioGroup(context)
        val radios = List(3) { RadioButton(context).apply { id = View.generateViewId(); group.addView(this) } }
        any = radios[0]; with = radios[1]; without = radios[2]
        checks = List(2) { CheckBox(context) }
        group.check(any.id)
        linkAttachmentTypeChecks(group, with.id, without.id, checks)
    }

    @Test fun firstTickWhileAnyKeepsTypeAndSelectsWith() {
        onMain { checks[1].isChecked = true }

        assertTrue(checks[1].isChecked)
        assertEquals(with.id, group.checkedRadioButtonId)
    }

    @Test fun choosingAnyClearsTickedTypes() {
        onMain { checks[0].isChecked = true; checks[1].isChecked = true; group.check(any.id) }

        assertFalse(checks.any { it.isChecked })
        assertTrue(checks.all { it.isEnabled })
    }

    @Test fun choosingWithoutClearsAndDisablesTypes() {
        onMain { checks[0].isChecked = true; group.check(without.id) }

        assertFalse(checks.any { it.isChecked })
        assertFalse(checks.any { it.isEnabled })
    }

    private fun onMain(block: () -> Unit) = InstrumentationRegistry.getInstrumentation().runOnMainSync(block)
}
