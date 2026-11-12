package com.barkfluff.client.utils

import android.app.Dialog
import android.view.View
import android.view.WindowManager
import androidx.appcompat.app.AlertDialog
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import com.google.android.material.dialog.MaterialAlertDialogBuilder

/** Keeps a view's bottom content area above either the navigation bar or the IME. */
fun View.applyImeInsetsToBottomPadding() {
    val basePaddingLeft = paddingLeft
    val basePaddingTop = paddingTop
    val basePaddingRight = paddingRight
    val basePaddingBottom = paddingBottom
    // Свой listener отключает штатную обработку fitsSystemWindows, поэтому остальные стороны
    // для таких view выставляем сами — иначе контент уезжает под статус-бар.
    val fitsOtherSides = fitsSystemWindows
    ViewCompat.setOnApplyWindowInsetsListener(this) { view, insets ->
        val bars = insets.getInsets(
            WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout()
        )
        val imeBottom = insets.getInsets(WindowInsetsCompat.Type.ime()).bottom
        if (fitsOtherSides) {
            view.updatePadding(
                left = basePaddingLeft + bars.left,
                top = basePaddingTop + bars.top,
                right = basePaddingRight + bars.right,
            )
        }
        view.updatePadding(bottom = basePaddingBottom + maxOf(bars.bottom, imeBottom))
        insets
    }
    ViewCompat.requestApplyInsets(this)
}

/** Lets an input dialog's own window resize and dispatch current IME insets. */
fun Dialog.configureImeInsets() {
    val dialogWindow = window ?: return
    dialogWindow.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
    ViewCompat.requestApplyInsets(dialogWindow.decorView)
}

/** Creates an input dialog with IME handling configured before it is shown. */
fun MaterialAlertDialogBuilder.showImeAware(): AlertDialog {
    val dialog = create()
    dialog.configureImeInsets()
    dialog.show()
    dialog.configureImeInsets()
    return dialog
}
