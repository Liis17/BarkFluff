package com.barkfluff.client.utils

import android.content.Context
import android.graphics.Rect
import android.view.View
import android.view.ViewTreeObserver

class KeyboardHeightTracker(
    private val rootView: View,
    private val context: Context,
    private val onKeyboardHeightChanged: (height: Int, isVisible: Boolean) -> Unit
) {
    companion object {
        private const val PREFS_NAME = "barkfluff_keyboard"
        private const val KEY_HEIGHT = "keyboard_height"
        private const val MIN_KEYBOARD_HEIGHT = 150

        fun getLastKnownHeight(context: Context): Int {
            val prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
            val saved = prefs.getInt(KEY_HEIGHT, 0)
            if (saved > 0) return saved
            return (context.resources.displayMetrics.heightPixels * 0.4).toInt()
        }
    }

    private var wasKeyboardVisible = false

    private val layoutListener = ViewTreeObserver.OnGlobalLayoutListener {
        val rect = Rect()
        rootView.getWindowVisibleDisplayFrame(rect)
        val screenHeight = rootView.rootView.height
        val keypadHeight = screenHeight - rect.bottom

        val isVisible = keypadHeight > MIN_KEYBOARD_HEIGHT

        if (isVisible) {
            context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                .edit()
                .putInt(KEY_HEIGHT, keypadHeight)
                .apply()
        }

        if (isVisible != wasKeyboardVisible) {
            wasKeyboardVisible = isVisible
            onKeyboardHeightChanged(keypadHeight, isVisible)
        }
    }

    init {
        rootView.viewTreeObserver.addOnGlobalLayoutListener(layoutListener)
    }

    fun detach() {
        rootView.viewTreeObserver.removeOnGlobalLayoutListener(layoutListener)
    }
}
