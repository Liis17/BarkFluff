package com.barkfluff.client

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.fragment.app.Fragment
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.databinding.ActivityMainBinding
import com.barkfluff.client.deeplink.DeepLinkCommand
import com.barkfluff.client.deeplink.DeepLinkHandler
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.utils.UpdateChecker
import com.google.android.material.color.DynamicColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.launch

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding

    private val notificationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { /* granted or denied — no special handling needed */ }

    // Порядок табов: Чаты (0) | Профиль (1)
    private val fragments = mutableMapOf<Int, Fragment>()
    private var currentTabIndex = TAB_CHATS

    companion object {
        private const val TAB_CHATS = 0
        private const val TAB_PROFILE = 1
        private const val KEY_CURRENT_TAB = "current_tab"

        /**
         * Хранит chatId для открытия после cold start (когда приложение убито
         * и уведомление открывает MainActivity, минуя SplashActivity).
         * SplashActivity выполнит инициализацию → вернётся в MainActivity →
         * chatId будет прочитан из этого поля.
         */
        @Volatile
        var pendingChatId: String? = null
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
            supportFragmentManager.findFragmentByTag("tab_$TAB_CHATS")?.let { fragments[TAB_CHATS] = it }
            supportFragmentManager.findFragmentByTag("tab_$TAB_PROFILE")?.let { fragments[TAB_PROFILE] = it }
        } else {
            // Первый запуск — показываем чаты
            showFragment(TAB_CHATS)
        }

        setupBottomNavigation()
        handleChatIntent(intent)
        handlePendingDeepLink()
        checkForUpdates()
        registerPrekeyBundleIfNeeded()
    }

    private fun registerPrekeyBundleIfNeeded() {
        val app = applicationContext as BarkFluffApplication
        if (!app.prekeyManager.isRegistered) {
            lifecycleScope.launch {
                com.barkfluff.client.crypto.E2EBootstrap.ensurePrekeyBundleRegistered(this@MainActivity)
            }
        }
        // Подписка на входящие E2E-инвайты (приватные и секретные)
        com.barkfluff.client.crypto.EncryptedInviteHandler.attach(this, this)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleChatIntent(intent)
    }

    private fun handleChatIntent(intent: Intent?) {
        // extra_chat_id — из нашего PendingIntent, chat_id — fallback из FCM data payload,
        // pendingChatId — сохранённый chatId после cold start через SplashActivity
        val chatId = intent?.getStringExtra(NotificationHelper.EXTRA_CHAT_ID)
            ?: intent?.getStringExtra("chat_id")
            ?: pendingChatId
            ?: return

        // Очищаем все источники чтобы не обрабатывать повторно
        pendingChatId = null
        intent?.removeExtra(NotificationHelper.EXTRA_CHAT_ID)
        intent?.removeExtra("chat_id")

        Log.d("MainActivity", "Opening chat from notification: chatId=$chatId")

        // Проверяем инициализацию gRPC клиентов
        val app = applicationContext as BarkFluffApplication
        if (!app.grpcManager.isInitialized()) {
            val globalParam = GlobalParam(this)
            val hasToken = globalParam.accessToken != null
            val tokenExpiration = globalParam.accessTokenExpiration
            val bufferMs = 5 * 60 * 1000L
            val isExpired = System.currentTimeMillis() + bufferMs >= tokenExpiration

            if (hasToken && !isExpired) {
                // Токен валиден — инициализируем gRPC на месте
                Log.d("MainActivity", "Token valid, initializing gRPC in-place")
                app.grpcManager.initAllClients(this, globalParam)
            } else {
                // Нужна авторизация через SplashActivity
                Log.d("MainActivity", "gRPC not initialized, redirecting through SplashActivity")
                pendingChatId = chatId
                startActivity(Intent(this, SplashActivity::class.java))
                finish()
                return
            }
        }

        // gRPC готов — открываем ChatActivity
        OpenChatManager.setOpenChat(chatId)

        val chatIntent = Intent(this, ChatActivity::class.java).apply {
            putExtra("chat_id", chatId)
            flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
        }
        startActivity(chatIntent)
    }

    private fun handlePendingDeepLink() {
        val app = applicationContext as BarkFluffApplication
        val pendingLink = app.pendingDeepLink ?: return
        app.pendingDeepLink = null

        val command = DeepLinkHandler.parse(pendingLink)
        if (command is DeepLinkCommand.OpenUserChat) {
            resolveDeepLinkUser(command.username)
        }
    }

    private fun resolveDeepLinkUser(username: String) {
        val app = applicationContext as BarkFluffApplication
        val grpcManager = app.grpcManager

        lifecycleScope.launch {
            val searchResult = grpcManager.searchUsers(username, size = 20)
            if (searchResult.isFailure) {
                Toast.makeText(this@MainActivity, "Ошибка поиска пользователя", Toast.LENGTH_SHORT).show()
                return@launch
            }

            val users = searchResult.getOrNull() ?: emptyList()
            val user = users.find { it.username.equals(username, ignoreCase = true) }

            if (user == null) {
                Toast.makeText(this@MainActivity, "Пользователь @$username не найден", Toast.LENGTH_SHORT).show()
                return@launch
            }

            val chatResult = grpcManager.getPersonChatId(user.userId)
            if (chatResult.isFailure) {
                Toast.makeText(this@MainActivity, "Не удалось открыть чат", Toast.LENGTH_SHORT).show()
                return@launch
            }

            val chatId = chatResult.getOrNull()!!
            val displayName = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
            val avatarFileId = user.profilePicturePreviewFileId.ifBlank { user.profilePictureFileId }.ifBlank { null }

            OpenChatManager.setOpenChat(chatId)

            val chatIntent = Intent(this@MainActivity, ChatActivity::class.java).apply {
                putExtra("chat_id", chatId)
                putExtra("chat_title", displayName)
                putExtra("chat_avatar_file_id", avatarFileId)
                putExtra("is_group_chat", false)
                putExtra("other_user_id", user.userId)
                flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            }
            startActivity(chatIntent)
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        outState.putInt(KEY_CURRENT_TAB, currentTabIndex)
    }

    private fun setupBottomNavigation() {
        // Убираем внутренние минимальные ширины item'ов, чтобы панель была компактной
        compactifyBottomNav()

        binding.bottomNavigation.selectedItemId = tabIndexToMenuId(currentTabIndex)
        binding.bottomNavigation.setOnItemSelectedListener { item ->
            val newTabIndex = menuIdToTabIndex(item.itemId)
            if (newTabIndex == currentTabIndex) return@setOnItemSelectedListener true
            switchTab(newTabIndex)
            true
        }
    }

    private fun compactifyBottomNav() {
        val menuView = binding.bottomNavigation.getChildAt(0) as? android.view.ViewGroup ?: return
        val itemWidthDp = 72
        val density = resources.displayMetrics.density
        val itemWidthPx = (itemWidthDp * density).toInt()
        for (i in 0 until menuView.childCount) {
            val item = menuView.getChildAt(i)
            val params = item.layoutParams
            params.width = itemWidthPx
            item.layoutParams = params
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
            TAB_CHATS -> ChatsFragment()
            TAB_PROFILE -> ProfileFragment()
            else -> ChatsFragment()
        }
    }

    private fun tabIndexToMenuId(tabIndex: Int): Int {
        return when (tabIndex) {
            TAB_CHATS -> R.id.navigation_chats
            TAB_PROFILE -> R.id.navigation_profile
            else -> R.id.navigation_chats
        }
    }

    private fun menuIdToTabIndex(menuId: Int): Int {
        return when (menuId) {
            R.id.navigation_chats -> TAB_CHATS
            R.id.navigation_profile -> TAB_PROFILE
            else -> TAB_CHATS
        }
    }

    private fun checkForUpdates() {
        lifecycleScope.launch {
            try {
                val currentVersion = GlobalParam.getAppVersion(this@MainActivity)
                val hasUpdate = UpdateChecker.hasUpdate(currentVersion)
                val badge = binding.bottomNavigation.getOrCreateBadge(R.id.navigation_profile)
                if (hasUpdate) {
                    badge.isVisible = true
                    badge.backgroundColor = android.graphics.Color.parseColor("#FF3D00")
                } else {
                    binding.bottomNavigation.removeBadge(R.id.navigation_profile)
                }
            } catch (e: Exception) {
                Log.e("MainActivity", "Error checking for updates", e)
            }
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
