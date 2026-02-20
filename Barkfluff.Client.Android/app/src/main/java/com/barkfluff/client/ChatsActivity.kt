package com.barkfluff.client

import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.databinding.ActivityChatsBinding
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder

/**
 * Экран чатов (заглушка)
 */
class ChatsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityChatsBinding

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityChatsBinding.inflate(layoutInflater)
        setContentView(binding.root)
    }

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        MaterialAlertDialogBuilder(this)
            .setTitle("Выход из приложения")
            .setMessage("Вы действительно хотите выйти?")
            .setPositiveButton("Выйти") { _, _ ->
                super.onBackPressed()
            }
            .setNegativeButton("Отмена", null)
            .show()
    }
}
