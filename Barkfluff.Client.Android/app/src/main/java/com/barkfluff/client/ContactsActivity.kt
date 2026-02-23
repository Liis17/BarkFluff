package com.barkfluff.client

import android.app.ActivityOptions
import android.content.Intent
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityContactsBinding
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder

/**
 * Экран контактов (заглушка)
 */
class ContactsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityContactsBinding
    private lateinit var globalParam: GlobalParam

    companion object {
        private const val TAG = "ContactsActivity"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityContactsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        globalParam = GlobalParam(this)

        setupBottomNavigation()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        binding.bottomNavigation.selectedItemId = R.id.navigation_contacts
    }

    private fun setupBottomNavigation() {
        binding.bottomNavigation.selectedItemId = R.id.navigation_contacts
        binding.bottomNavigation.setOnItemSelectedListener { item ->
            if (item.itemId == binding.bottomNavigation.selectedItemId) {
                return@setOnItemSelectedListener true
            }

            when (item.itemId) {
                R.id.navigation_contacts -> {
                    true
                }
                R.id.navigation_chats -> {
                    val intent = Intent(this, ChatsActivity::class.java)
                    intent.addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT)
                    val options = ActivityOptions.makeCustomAnimation(this, R.anim.slide_in_from_right, R.anim.slide_out_to_left)
                    startActivity(intent, options.toBundle())
                    true
                }
                R.id.navigation_profile -> {
                    val intent = Intent(this, ProfileActivity::class.java)
                    intent.addFlags(Intent.FLAG_ACTIVITY_REORDER_TO_FRONT)
                    val options = ActivityOptions.makeCustomAnimation(this, R.anim.slide_in_from_right, R.anim.slide_out_to_left)
                    startActivity(intent, options.toBundle())
                    true
                }
                else -> false
            }
        }
    }

    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        MaterialAlertDialogBuilder(this)
            .setTitle("Выход из приложения")
            .setMessage("Вы действительно хотите выйти?")
            .setPositiveButton("Выйти") { _, _ ->
                finishAffinity()
            }
            .setNegativeButton("Отмена", null)
            .show()
    }
}
