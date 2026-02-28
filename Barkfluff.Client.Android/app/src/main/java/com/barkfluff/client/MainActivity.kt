package com.barkfluff.client

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.util.Log
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.fragment.app.Fragment
import com.barkfluff.client.databinding.ActivityMainBinding
import com.barkfluff.client.notifications.NotificationHelper
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding

    private val notificationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { /* granted or denied — no special handling needed */ }

    // Порядок табов: Контакты (0) | Чаты (1) | Профиль (2)
    private val fragments = mutableMapOf<Int, Fragment>()
    private var currentTabIndex = TAB_CHATS

    companion object {
        private const val TAB_CONTACTS = 0
        private const val TAB_CHATS = 1
        private const val TAB_PROFILE = 2
        private const val KEY_CURRENT_TAB = "current_tab"
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        requestNotificationPermission()

        if (savedInstanceState != null) {
            currentTabIndex = savedInstanceState.getInt(KEY_CURRENT_TAB, TAB_CHATS)
            // Восстанавливаем ссылки на существующие фрагменты
            supportFragmentManager.findFragmentByTag("tab_$TAB_CONTACTS")?.let { fragments[TAB_CONTACTS] = it }
            supportFragmentManager.findFragmentByTag("tab_$TAB_CHATS")?.let { fragments[TAB_CHATS] = it }
            supportFragmentManager.findFragmentByTag("tab_$TAB_PROFILE")?.let { fragments[TAB_PROFILE] = it }
        } else {
            // Первый запуск — показываем чаты
            showFragment(TAB_CHATS)
        }

        setupBottomNavigation()
        handleChatIntent(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleChatIntent(intent)
    }

    private fun handleChatIntent(intent: Intent?) {
        val chatId = intent?.getStringExtra(NotificationHelper.EXTRA_CHAT_ID) ?: return
        Log.d("MainActivity", "Opening chat from notification: chatId=$chatId")

        // Устанавливаем чат как открытый
        com.barkfluff.client.data.OpenChatManager.setOpenChat(chatId)
        
        // Открываем ChatActivity
        val chatIntent = Intent(this, ChatActivity::class.java).apply {
            putExtra("chat_id", chatId)
        }
        startActivity(chatIntent)
        
        // Очищаем extra чтобы не обрабатывать повторно
        intent?.removeExtra(NotificationHelper.EXTRA_CHAT_ID)
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        outState.putInt(KEY_CURRENT_TAB, currentTabIndex)
    }

    private fun setupBottomNavigation() {
        binding.bottomNavigation.selectedItemId = tabIndexToMenuId(currentTabIndex)
        binding.bottomNavigation.setOnItemSelectedListener { item ->
            val newTabIndex = menuIdToTabIndex(item.itemId)
            if (newTabIndex == currentTabIndex) return@setOnItemSelectedListener true
            switchTab(newTabIndex)
            true
        }
    }

    private fun switchTab(newTabIndex: Int) {
        val enterAnim = if (newTabIndex < currentTabIndex) {
            R.anim.slide_in_from_left
        } else {
            R.anim.slide_in_from_right
        }

        val transaction = supportFragmentManager.beginTransaction()
        transaction.setCustomAnimations(enterAnim, R.anim.fade_out)

        // Скрываем текущий фрагмент
        fragments[currentTabIndex]?.let { transaction.hide(it) }

        // Показываем или создаём новый фрагмент
        val fragment = fragments[newTabIndex]
        if (fragment != null) {
            transaction.show(fragment)
        } else {
            val newFragment = createFragment(newTabIndex)
            fragments[newTabIndex] = newFragment
            transaction.add(R.id.fragmentContainer, newFragment, "tab_$newTabIndex")
        }

        transaction.runOnCommit {
            // Входящий фрагмент поверх уходящего
            fragments[newTabIndex]?.view?.bringToFront()
        }

        transaction.commit()
        currentTabIndex = newTabIndex
    }

    private fun showFragment(tabIndex: Int) {
        val fragment = fragments[tabIndex] ?: createFragment(tabIndex).also {
            fragments[tabIndex] = it
        }

        supportFragmentManager.beginTransaction()
            .replace(R.id.fragmentContainer, fragment, "tab_$tabIndex")
            .commit()

        currentTabIndex = tabIndex
    }

    private fun createFragment(tabIndex: Int): Fragment {
        return when (tabIndex) {
            TAB_CONTACTS -> ContactsFragment()
            TAB_CHATS -> ChatsFragment()
            TAB_PROFILE -> ProfileFragment()
            else -> ChatsFragment()
        }
    }

    private fun tabIndexToMenuId(tabIndex: Int): Int {
        return when (tabIndex) {
            TAB_CONTACTS -> R.id.navigation_contacts
            TAB_CHATS -> R.id.navigation_chats
            TAB_PROFILE -> R.id.navigation_profile
            else -> R.id.navigation_chats
        }
    }

    private fun menuIdToTabIndex(menuId: Int): Int {
        return when (menuId) {
            R.id.navigation_contacts -> TAB_CONTACTS
            R.id.navigation_chats -> TAB_CHATS
            R.id.navigation_profile -> TAB_PROFILE
            else -> TAB_CHATS
        }
    }

    private fun requestNotificationPermission() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS)
                != PackageManager.PERMISSION_GRANTED
            ) {
                notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
            }
        }
    }

    override fun onStart() {
        super.onStart()
        (application as BarkFluffApplication).realtimeService.start()
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
