package com.barkfluff.client

import android.content.Intent
import android.os.Bundle
import androidx.appcompat.app.AppCompatActivity
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityContactsBinding
import com.google.android.material.color.DynamicColors

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

    private fun setupBottomNavigation() {
        binding.bottomNavigation.selectedItemId = R.id.navigation_contacts
        binding.bottomNavigation.setOnItemSelectedListener { item ->
            when (item.itemId) {
                R.id.navigation_contacts -> {
                    // Уже на экране контактов
                    true
                }
                R.id.navigation_chats -> {
                    val intent = Intent(this, ChatsActivity::class.java)
                    startActivity(intent)
                    true
                }
                R.id.navigation_profile -> {
                    val intent = Intent(this, ProfileActivity::class.java)
                    startActivity(intent)
                    true
                }
                else -> false
            }
        }
    }
}
