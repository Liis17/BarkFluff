package com.barkfluff.client.utils

import android.text.Editable
import android.text.TextWatcher
import android.view.KeyEvent
import android.widget.EditText
import com.barkfluff.client.R

/**
 * Управляет рядом из N однозначных ячеек OTP-кода: авто-переход фокуса вперёд/назад
 * и покраска ячейки при заполнении. Логика авто-перехода извлечена из
 * LoginActivity.setupOtpBoxes()/getOtpCode().
 */
class OtpCellsHelper(
    private val cells: List<EditText>,
    private val onComplete: (String) -> Unit = {}
) {

    fun setup() {
        for (i in cells.indices) {
            val cell = cells[i]

            cell.addTextChangedListener(object : TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
                override fun afterTextChanged(s: Editable?) {
                    updateCellState(i)
                    if (s != null && s.length == 1 && i < cells.size - 1) {
                        cells[i + 1].requestFocus()
                    }
                    if (i == cells.size - 1 && s != null && s.length == 1) {
                        val code = getCode()
                        if (code.length == cells.size) {
                            onComplete(code)
                        }
                    }
                }
            })

            cell.setOnKeyListener { _, keyCode, event ->
                if (keyCode == KeyEvent.KEYCODE_DEL && event.action == KeyEvent.ACTION_DOWN) {
                    if (cell.text.isNullOrEmpty() && i > 0) {
                        cells[i - 1].apply {
                            requestFocus()
                            text?.clear()
                        }
                        return@setOnKeyListener true
                    }
                }
                false
            }
        }
    }

    fun getCode(): String = cells.joinToString("") { it.text.toString() }

    fun clear() {
        cells.forEach {
            it.text?.clear()
            updateCellStateFor(it, filled = false)
        }
        focusFirst()
    }

    fun focusFirst() {
        cells.first().requestFocus()
    }

    private fun updateCellState(index: Int) {
        val cell = cells[index]
        updateCellStateFor(cell, filled = cell.text?.length == 1)
    }

    private fun updateCellStateFor(cell: EditText, filled: Boolean) {
        if (filled) {
            cell.setBackgroundResource(R.drawable.bg_register_otp_cell_filled)
            cell.setTextColor(resolveThemeColor(cell.context, com.google.android.material.R.attr.colorOnPrimary))
        } else {
            cell.setBackgroundResource(R.drawable.bg_otp_cell)
            cell.setTextColor(resolveThemeColor(cell.context, com.google.android.material.R.attr.colorOnSurface))
        }
    }

    private fun resolveThemeColor(context: android.content.Context, attr: Int): Int {
        val typedValue = android.util.TypedValue()
        context.theme.resolveAttribute(attr, typedValue, true)
        return typedValue.data
    }
}
