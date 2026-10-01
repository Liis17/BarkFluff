package com.barkfluff.client.search

import android.widget.CheckBox
import android.widget.RadioGroup

internal fun linkAttachmentTypeChecks(group: RadioGroup, withId: Int, withoutId: Int, checks: List<CheckBox>) {
    var tickedByCheckbox = false
    group.setOnCheckedChangeListener { _, checkedId ->
        // check() сначала снимает прежнюю радиокнопку, и listener получает её id — это не выбор пользователя
        if (tickedByCheckbox) return@setOnCheckedChangeListener
        checks.forEach {
            it.isEnabled = checkedId != withoutId
            if (checkedId != withId) it.isChecked = false
        }
    }
    checks.forEach { check -> check.setOnCheckedChangeListener { _, checked ->
        if (checked) {
            tickedByCheckbox = true
            group.check(withId)
            tickedByCheckbox = false
        }
    } }
}
