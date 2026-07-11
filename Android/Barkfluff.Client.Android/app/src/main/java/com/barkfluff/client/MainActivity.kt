package com.barkfluff.client

import android.Manifest
import android.animation.ValueAnimator
import android.content.Intent
import android.content.res.ColorStateList
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.transition.ChangeBounds
import android.transition.Fade
import android.transition.TransitionManager
import android.transition.TransitionSet
import android.util.Log
import android.view.MotionEvent
import android.view.View
import android.view.animation.PathInterpolator
import android.widget.LinearLayout
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
import com.google.android.material.color.MaterialColors
import com.google.android.material.button.MaterialButton
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.shape.ShapeAppearanceModel
import kotlinx.coroutines.launch

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding

    private val notificationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { /* granted or denied — no special handling needed */ }

    // Порядок табов: Чаты (0) | Звонки (1) | Профиль (2)
    private val fragments = mutableMapOf<Int, Fragment>()
    private var currentTabIndex = TAB_CHATS
    private var chatUnreadCount = 0
    private var profileUpdateAvailable = false
    private var fabCornerRadiusDp = FLOATING_FAB_CORNER_RADIUS_DP

    companion object {
        private const val TAB_CHATS = 0
        private const val TAB_CALLS = 1
        private const val TAB_PROFILE = 2
        private const val KEY_CURRENT_TAB = "current_tab"
        private const val BOTTOM_NAV_WIDE_ITEM_WIDTH_DP = 72
        private const val FLOATING_FAB_CORNER_RADIUS_DP = 22
        private const val FLOATING_FAB_PRESSED_CORNER_RADIUS_DP = 32

        /**
         * Хранит chatId для открытия после cold start (когда приложение убито
         * и уведомление открывает MainActivity, минуя SplashActivity).
         * SplashActivity выполнит инициализацию → вернётся в MainActivity →
         * chatId будет прочитан из этого поля.
         */
        @Volatile
        var pendingChatId: String? = null

        /** Признак того, что pendingChatId — приватный чат (уведомление private_chat_invite). */
        @Volatile
        var pendingChatIsPrivate: Boolean = false
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
            supportFragmentManager.findFragmentByTag("tab_$TAB_CALLS")?.let { fragments[TAB_CALLS] = it }
            supportFragmentManager.findFragmentByTag("tab_$TAB_PROFILE")?.let { fragments[TAB_PROFILE] = it }
        } else {
            // Первый запуск — показываем чаты
            showFragment(TAB_CHATS)
        }

        setupBottomNavigation()
        setupCreateChatFab()
        setupChatUnreadBadge()
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

    override fun onResume() {
        super.onResume()
        if (::binding.isInitialized) {
            applyBottomNavigationVisibility()
            if (!isPhoneFloatingNavigation()) {
                compactifyBottomNav()
                binding.bottomNavigation.selectedItemId = tabIndexToMenuId(currentTabIndex)
            }
            updateCreateChatFabVisibility()
        }
    }

    private fun handleChatIntent(intent: Intent?) {
        // extra_chat_id — из нашего PendingIntent, chat_id — fallback из FCM data payload,
        // pendingChatId — сохранённый chatId после cold start через SplashActivity
        val chatId = intent?.getStringExtra(NotificationHelper.EXTRA_CHAT_ID)
            ?: intent?.getStringExtra("chat_id")
            ?: pendingChatId
            ?: return

        val isPrivateChat = intent?.getBooleanExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT, false)
            ?.takeIf { intent.hasExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT) }
            ?: pendingChatIsPrivate

        // Очищаем все источники чтобы не обрабатывать повторно
        pendingChatId = null
        pendingChatIsPrivate = false
        intent?.removeExtra(NotificationHelper.EXTRA_CHAT_ID)
        intent?.removeExtra("chat_id")
        intent?.removeExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT)

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
                pendingChatIsPrivate = isPrivateChat
                startActivity(Intent(this, SplashActivity::class.java))
                finish()
                return
            }
        }

        // Приватный чат: PrivateChatActivity сам зарезолвит состояние инвайта через getChat
        if (isPrivateChat) {
            startActivity(Intent(this, PrivateChatActivity::class.java).apply {
                putExtra(PrivateChatActivity.EXTRA_CHAT_ID, chatId)
                flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            })
            return
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
        if (isPhoneFloatingNavigation()) {
            setupFloatingNavigation()
            return
        }

        applyBottomNavigationVisibility()

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

    private fun setupFloatingNavigation() {
        findViewById<MaterialButton>(R.id.chatNavButton).setOnClickListener { onFloatingTabClicked(TAB_CHATS) }
        findViewById<MaterialButton>(R.id.callsNavButton).setOnClickListener { onFloatingTabClicked(TAB_CALLS) }
        findViewById<MaterialButton>(R.id.profileNavButton).setOnClickListener { onFloatingTabClicked(TAB_PROFILE) }
        applyBottomNavigationVisibility()
    }

    private fun onFloatingTabClicked(tabIndex: Int) {
        if (tabIndex == currentTabIndex) {
            if (tabIndex == TAB_CHATS) {
                (fragments[TAB_CHATS] as? ChatsFragment)?.scrollToTop()
            }
            return
        }
        switchTab(tabIndex)
    }

    private fun setupCreateChatFab() {
        supportFragmentManager.setFragmentResultListener(CreateChatBottomSheet.RESULT_KEY, this) { _, result ->
            when (result.getString(CreateChatBottomSheet.RESULT_TYPE)) {
                CreateChatBottomSheet.TYPE_REGULAR -> startActivity(Intent(this, SearchActivity::class.java))
                CreateChatBottomSheet.TYPE_GROUP -> startActivity(Intent(this, CreateGroupChatActivity::class.java))
                CreateChatBottomSheet.TYPE_PRIVATE -> startActivity(
                    Intent(this, SearchActivity::class.java)
                        .putExtra(SearchActivity.EXTRA_MODE, SearchActivity.MODE_PRIVATE)
                )
                CreateChatBottomSheet.TYPE_SECRET -> startActivity(
                    Intent(this, CreateEncryptedChatActivity::class.java)
                        .putExtra(
                            CreateEncryptedChatActivity.EXTRA_INITIAL_TYPE,
                            CreateEncryptedChatActivity.INITIAL_TYPE_SECRET
                        )
                )
            }
        }

        binding.createChatFab.setOnClickListener {
            if (supportFragmentManager.findFragmentByTag(CreateChatBottomSheet.TAG) == null) {
                val globalParam = GlobalParam(this)
                CreateChatBottomSheet.newInstance(
                    privateEnabled = globalParam.privateChatsEnabled,
                    secretEnabled = globalParam.secretChatsEnabled
                )
                    .show(supportFragmentManager, CreateChatBottomSheet.TAG)
            }
        }
        binding.createChatFab.setOnTouchListener { _, event ->
            if (isPhoneFloatingNavigation()) {
                when (event.actionMasked) {
                    MotionEvent.ACTION_DOWN -> animateFloatingFabCornerRadius(FLOATING_FAB_PRESSED_CORNER_RADIUS_DP)
                    MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL ->
                        animateFloatingFabCornerRadius(FLOATING_FAB_CORNER_RADIUS_DP)
                }
            }
            false
        }
        updateCreateChatFabVisibility()
    }

    private fun setupChatUnreadBadge() {
        supportFragmentManager.setFragmentResultListener(ChatsFragment.MAIN_UNREAD_RESULT_KEY, this) { _, result ->
            chatUnreadCount = result.getInt(ChatsFragment.MAIN_UNREAD_COUNT, 0).coerceAtLeast(0)
            renderFloatingNavigation(animate = false)
        }
    }

    private fun updateCreateChatFabVisibility() {
        binding.createChatFab.visibility = if (currentTabIndex == TAB_CHATS) android.view.View.VISIBLE else android.view.View.GONE
    }

    private fun applyBottomNavigationVisibility() {
        val visibleTabs = visibleTabIndices()
        if (isPhoneFloatingNavigation()) {
            findViewById<View>(R.id.callsNavTab).visibility =
                if (visibleTabs.contains(TAB_CALLS)) View.VISIBLE else View.GONE
            if (!visibleTabs.contains(currentTabIndex)) {
                showFragment(visibleTabs.first())
            }
            renderFloatingNavigation(animate = false)
            return
        }

        binding.bottomNavigation.menu.findItem(R.id.navigation_chats).isVisible = visibleTabs.contains(TAB_CHATS)
        binding.bottomNavigation.menu.findItem(R.id.navigation_calls).isVisible = visibleTabs.contains(TAB_CALLS)
        binding.bottomNavigation.menu.findItem(R.id.navigation_profile).isVisible = visibleTabs.contains(TAB_PROFILE)

        if (!visibleTabs.contains(currentTabIndex)) {
            showFragment(visibleTabs.first())
        }
    }

    private fun visibleTabIndices(): List<Int> {
        val globalParam = GlobalParam(this)
        val tabs = mutableListOf<Int>()
        tabs.add(TAB_CHATS)
        if (globalParam.mainTabCallsVisible) tabs.add(TAB_CALLS)
        tabs.add(TAB_PROFILE)
        return tabs
    }

    private fun compactifyBottomNav() {
        val menuView = binding.bottomNavigation.getChildAt(0) as? android.view.ViewGroup ?: return
        val density = resources.displayMetrics.density
        val itemWidthDp = BOTTOM_NAV_WIDE_ITEM_WIDTH_DP
        val itemWidthPx = (itemWidthDp * density).toInt()
        for (i in 0 until menuView.childCount) {
            val item = menuView.getChildAt(i)
            val params = item.layoutParams
            params.width = itemWidthPx
            item.layoutParams = params
        }
    }

    private fun switchTab(newTabIndex: Int) {
        if (!visibleTabIndices().contains(newTabIndex)) return

        val transaction = supportFragmentManager.beginTransaction()
        transaction.setCustomAnimations(R.anim.fade_in, R.anim.fade_out)

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
        updateCreateChatFabVisibility()
        renderFloatingNavigation(animate = true)
    }

    private fun showFragment(tabIndex: Int) {
        val fragment = fragments[tabIndex] ?: createFragment(tabIndex).also {
            fragments[tabIndex] = it
        }

        supportFragmentManager.beginTransaction()
            .replace(R.id.fragmentContainer, fragment, "tab_$tabIndex")
            .commit()

        currentTabIndex = tabIndex
        if (::binding.isInitialized) {
            updateCreateChatFabVisibility()
            renderFloatingNavigation(animate = false)
        }
    }

    private fun createFragment(tabIndex: Int): Fragment {
        return when (tabIndex) {
            TAB_CHATS -> ChatsFragment()
            TAB_CALLS -> CallsFragment()
            TAB_PROFILE -> ProfileFragment()
            else -> ChatsFragment()
        }
    }

    private fun tabIndexToMenuId(tabIndex: Int): Int {
        return when (tabIndex) {
            TAB_CHATS -> R.id.navigation_chats
            TAB_CALLS -> R.id.navigation_calls
            TAB_PROFILE -> R.id.navigation_profile
            else -> R.id.navigation_chats
        }
    }

    private fun menuIdToTabIndex(menuId: Int): Int {
        return when (menuId) {
            R.id.navigation_chats -> TAB_CHATS
            R.id.navigation_calls -> TAB_CALLS
            R.id.navigation_profile -> TAB_PROFILE
            else -> TAB_CHATS
        }
    }

    private fun checkForUpdates() {
        lifecycleScope.launch {
            try {
                val currentVersion = GlobalParam.getAppVersion(this@MainActivity)
                val hasUpdate = UpdateChecker.hasUpdate(currentVersion)
                if (hasUpdate) {
                    profileUpdateAvailable = true
                    if (isPhoneFloatingNavigation()) {
                        findViewById<View>(R.id.profileUpdateBadge).visibility = View.VISIBLE
                    } else {
                        val badge = binding.bottomNavigation.getOrCreateBadge(R.id.navigation_profile)
                        badge.isVisible = true
                        badge.backgroundColor = MaterialColors.getColor(
                            binding.bottomNavigation,
                            com.google.android.material.R.attr.colorErrorContainer
                        )
                    }
                } else {
                    profileUpdateAvailable = false
                    if (isPhoneFloatingNavigation()) {
                        findViewById<View>(R.id.profileUpdateBadge).visibility = View.GONE
                    } else {
                        binding.bottomNavigation.removeBadge(R.id.navigation_profile)
                    }
                }
            } catch (e: Exception) {
                Log.e("MainActivity", "Error checking for updates", e)
            }
        }
    }

    private fun isPhoneFloatingNavigation(): Boolean = findViewById<LinearLayout>(R.id.floatingNavContainer) != null

    private fun renderFloatingNavigation(animate: Boolean) {
        val container = findViewById<LinearLayout>(R.id.floatingNavContainer) ?: return
        if (animate && ValueAnimator.areAnimatorsEnabled()) {
            TransitionManager.beginDelayedTransition(container, TransitionSet().apply {
                ordering = TransitionSet.ORDERING_TOGETHER
                addTransition(ChangeBounds())
                addTransition(Fade())
                duration = 380L
                interpolator = PathInterpolator(0.2f, 0f, 0f, 1f)
            })
        }

        renderFloatingTab(
            button = findViewById(R.id.chatNavButton),
            tabIndex = TAB_CHATS,
            label = getString(R.string.nav_chats),
            iconRes = if (currentTabIndex == TAB_CHATS) R.drawable.ic_chat_bubble_filled else R.drawable.ic_chat_bubble,
            selected = currentTabIndex == TAB_CHATS
        )
        renderFloatingTab(
            button = findViewById(R.id.callsNavButton),
            tabIndex = TAB_CALLS,
            label = getString(R.string.nav_calls),
            iconRes = R.drawable.ic_phone,
            selected = currentTabIndex == TAB_CALLS
        )
        renderFloatingTab(
            button = findViewById(R.id.profileNavButton),
            tabIndex = TAB_PROFILE,
            label = getString(R.string.nav_profile),
            iconRes = R.drawable.ic_account,
            selected = currentTabIndex == TAB_PROFILE
        )

        val unreadBadge = findViewById<android.widget.TextView>(R.id.chatUnreadBadge)
        unreadBadge.visibility = if (chatUnreadCount > 0) View.VISIBLE else View.GONE
        unreadBadge.text = if (chatUnreadCount > 99) "99+" else chatUnreadCount.toString()
        findViewById<View>(R.id.profileUpdateBadge).visibility =
            if (profileUpdateAvailable) View.VISIBLE else View.GONE
    }

    private fun renderFloatingTab(
        button: MaterialButton,
        tabIndex: Int,
        label: String,
        iconRes: Int,
        selected: Boolean
    ) {
        val contentColor = MaterialColors.getColor(
            button,
            if (selected) com.google.android.material.R.attr.colorOnPrimaryContainer
            else com.google.android.material.R.attr.colorOnSurfaceVariant
        )
        button.text = if (selected) label else ""
        button.setIconResource(iconRes)
        button.iconTint = ColorStateList.valueOf(contentColor)
        button.backgroundTintList = ColorStateList.valueOf(
            MaterialColors.getColor(
                button,
                if (selected) com.google.android.material.R.attr.colorPrimaryContainer
                else com.google.android.material.R.attr.colorSurfaceContainerHigh
            )
        )
        button.setTextColor(contentColor)
        button.iconPadding = if (selected) dpToPx(8) else 0
        button.setPaddingRelative(dpToPx(if (selected) 20 else 15), 0, dpToPx(if (selected) 20 else 15), 0)
        button.isSelected = selected
        button.stateDescription = if (selected) getString(R.string.nav_selected) else null
        button.contentDescription = when (tabIndex) {
            TAB_CHATS -> getString(R.string.cd_chats_with_unread, chatUnreadCount)
            TAB_PROFILE -> getString(R.string.cd_profile)
            else -> label
        }
    }

    private fun animateFloatingFabCornerRadius(targetRadiusDp: Int) {
        if (!ValueAnimator.areAnimatorsEnabled() || fabCornerRadiusDp == targetRadiusDp) return
        ValueAnimator.ofInt(fabCornerRadiusDp, targetRadiusDp).apply {
            duration = 120L
            addUpdateListener { animator ->
                val radiusDp = animator.animatedValue as Int
                fabCornerRadiusDp = radiusDp
                binding.createChatFab.shapeAppearanceModel = ShapeAppearanceModel.builder()
                    .setAllCornerSizes(dpToPx(radiusDp).toFloat())
                    .build()
            }
            start()
        }
    }

    private fun dpToPx(valueDp: Int): Int = (valueDp * resources.displayMetrics.density).toInt()

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
