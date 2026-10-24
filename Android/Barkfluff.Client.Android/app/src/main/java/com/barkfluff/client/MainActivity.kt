package com.barkfluff.client

import android.annotation.SuppressLint
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
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.fragment.app.Fragment
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.data.OpenChatManager
import com.barkfluff.client.databinding.ActivityMainBinding
import com.barkfluff.client.deeplink.DeepLinkCommand
import com.barkfluff.client.deeplink.DeepLinkHandler
import com.barkfluff.client.domain.gateway.UserDirectoryGateway
import com.barkfluff.client.domain.gateway.ChatDirectoryGateway
import com.barkfluff.client.cache.CacheScope
import com.barkfluff.client.cache.ChatCacheRepository
import com.barkfluff.client.grpc.GrpcClientRegistry
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.utils.UpdateChecker
import com.google.android.material.color.DynamicColors
import com.google.android.material.color.MaterialColors
import com.google.android.material.button.MaterialButton
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.shape.ShapeAppearanceModel
import dagger.hilt.android.AndroidEntryPoint
import io.grpc.Metadata
import io.grpc.Status
import io.grpc.StatusException
import io.grpc.StatusRuntimeException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

@AndroidEntryPoint
class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    @javax.inject.Inject lateinit var clientRegistry: GrpcClientRegistry
    @javax.inject.Inject lateinit var userDirectoryGateway: UserDirectoryGateway
    @javax.inject.Inject lateinit var chatDirectoryGateway: ChatDirectoryGateway
    @javax.inject.Inject lateinit var chatCacheRepository: ChatCacheRepository

    private var pendingNotificationRoute: NotificationRoute? = null
    private var notificationRouteJob: kotlinx.coroutines.Job? = null

    private val notificationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { granted ->
        if (granted) requestFullScreenIntentPermission()
    }

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
        private const val KEY_ROUTE_SCOPE = "notification_route_scope"
        private const val KEY_ROUTE_KIND = "notification_route_kind"
        private const val KEY_ROUTE_CHAT = "notification_route_chat"
        private const val KEY_ROUTE_EVENT = "notification_route_event"
        private const val KEY_ROUTE_THREAD = "notification_route_thread"
        private const val KEY_ROUTE_DATA = "notification_route_data"
        private const val KEY_ROUTE_NOTIFICATION = "notification_route_is_notification"
        private const val KEY_ROUTE_PRIVATE_INVITE = "notification_route_private_invite"
        private const val BOTTOM_NAV_WIDE_ITEM_WIDTH_DP = 72
        private const val FLOATING_FAB_CORNER_RADIUS_DP = 22
        private const val FLOATING_FAB_PRESSED_CORNER_RADIUS_DP = 32

    }

    private data class NotificationRoute(
        val scopeId: String?,
        val kind: String,
        val chatId: String,
        val eventId: String,
        val threadId: String,
        val dataUri: String?,
        val isNotification: Boolean,
        val isPrivateInvite: Boolean,
    )

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
        val restoredRoute = savedInstanceState?.getString(KEY_ROUTE_CHAT)?.let { chatId ->
            NotificationRoute(
                scopeId = savedInstanceState.getString(KEY_ROUTE_SCOPE),
                kind = savedInstanceState.getString(KEY_ROUTE_KIND) ?: NotificationHelper.KIND_REGULAR,
                chatId = chatId,
                eventId = savedInstanceState.getString(KEY_ROUTE_EVENT).orEmpty(),
                threadId = savedInstanceState.getString(KEY_ROUTE_THREAD) ?: chatId,
                dataUri = savedInstanceState.getString(KEY_ROUTE_DATA),
                isNotification = savedInstanceState.getBoolean(KEY_ROUTE_NOTIFICATION),
                isPrivateInvite = savedInstanceState.getBoolean(KEY_ROUTE_PRIVATE_INVITE),
            )
        }
        if (restoredRoute != null) {
            clearConsumedRouteIntent()
            pendingNotificationRoute = restoredRoute
            notificationRouteJob = lifecycleScope.launch { navigateNotificationRoute(restoredRoute) }
        } else if (savedInstanceState == null) {
            handleChatIntent(intent)
        } else {
            clearConsumedRouteIntent()
        }
        handlePendingDeepLink()
        checkForUpdates()
        registerPrekeyBundleIfNeeded()
        startRealtimeAfterLogin()
    }

    /**
     * Поднимает realtime-стримы после входа: ProcessLifecycleOwner.onStart уже отработал
     * (на экране логина, где токенов ещё не было) и повторно не вызовется.
     * Оба resume() идемпотентны — при холодном старте залогиненного пользователя ничего не делают.
     */
    private fun startRealtimeAfterLogin() {
        val app = applicationContext as BarkFluffApplication
        app.realtimeService.resume()
        app.callEventsService.resume()
        app.outgoingMessageQueue.resume()
    }

    private fun registerPrekeyBundleIfNeeded() {
        if (GlobalParam(this).secretChatsEnabled) {
            lifecycleScope.launch {
                com.barkfluff.client.crypto.E2EBootstrap.ensurePrekeyBundleRegistered(this@MainActivity)
            }
        }
        // Подписка на входящие E2E-инвайты (приватные и секретные)
        com.barkfluff.client.crypto.EncryptedInviteHandler.attach(this, this)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
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

    internal fun handleChatIntent(intent: Intent?) {
        if (intent == null) return
        val notificationMarker = intent.hasExtra(NotificationHelper.EXTRA_KIND) ||
            intent.hasExtra(NotificationHelper.EXTRA_OPEN_CHAT_LIST) ||
            intent.data?.let { it.scheme == "barkfluff" && it.authority == "message-notification" } == true ||
            (intent.action == Intent.ACTION_VIEW && intent.hasExtra("chat_id"))
        val target = if (notificationMarker) NotificationHelper.currentTarget(this, intent) else null
        if (notificationMarker && target == null) {
            Log.w("MainActivity", "Rejected an unscoped or invalid notification route")
            clearConsumedRouteIntent()
            notificationRouteJob?.cancel()
            pendingNotificationRoute = null
            showNotificationRouteError()
            return
        }

        val route = if (target != null) {
            NotificationRoute(
                scopeId = target.scopeId,
                kind = target.kind,
                chatId = target.chatId,
                eventId = target.eventId,
                threadId = target.threadId,
                dataUri = intent.data?.toString(),
                isNotification = true,
                isPrivateInvite = intent.getBooleanExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT, false),
            )
        } else {
            val chatId = intent.getStringExtra(NotificationHelper.EXTRA_CHAT_ID)?.takeIf(String::isNotBlank) ?: return
            NotificationRoute(
                scopeId = CacheScope.from(GlobalParam(this))?.id,
                kind = NotificationHelper.KIND_REGULAR,
                chatId = chatId,
                eventId = "",
                threadId = chatId,
                dataUri = null,
                isNotification = false,
                isPrivateInvite = intent.getBooleanExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT, false),
            )
        }

        clearConsumedRouteIntent()
        pendingNotificationRoute = route
        notificationRouteJob?.cancel()
        notificationRouteJob = lifecycleScope.launch { navigateNotificationRoute(route) }
    }

    private suspend fun navigateNotificationRoute(route: NotificationRoute) {
        val scopeNow = CacheScope.from(GlobalParam(this))?.id
        if (route.isNotification && route.scopeId != scopeNow) {
            pendingNotificationRoute = null
            showNotificationRouteError()
            return
        }
        if (route.isNotification && route.kind != NotificationHelper.KIND_REGULAR && !route.isPrivateInvite) {
            showChatsAfterNotification()
            pendingNotificationRoute = null
            return
        }
        if (clientRegistry.messagesClient == null || clientRegistry.usersClient == null) {
            val globalParam = GlobalParam(this)
            val isExpired = System.currentTimeMillis() + 5 * 60 * 1000L >= globalParam.accessTokenExpiration
            if (globalParam.accessToken == null || isExpired) {
                startActivity(splashIntent(route))
                finish()
                return
            }
            clientRegistry.initAllClients(globalParam, this)
        }
        if (route.isNotification && CacheScope.from(GlobalParam(this))?.id != route.scopeId) {
            pendingNotificationRoute = null
            showNotificationRouteError()
            return
        }

        if (route.isPrivateInvite) {
            startActivity(ChatActivity.privateChatIntent(this, route.chatId, "").apply {
                flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            })
            pendingNotificationRoute = null
            return
        }
        if (route.isNotification) {
            val scope = CacheScope(route.scopeId ?: return)
            val cached = chatCacheRepository.readChatList(scope)?.chats?.firstOrNull { it.id == route.chatId }
            val freshResult = try {
                chatDirectoryGateway.chat(route.chatId)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: Exception) {
                Result.failure(error)
            }
            if (CacheScope.from(GlobalParam(this))?.id != route.scopeId || pendingNotificationRoute != route) {
                pendingNotificationRoute = null
                showNotificationRouteError()
                return
            }
            val freshChat = freshResult.getOrNull()
            val permanentFailure = freshResult.exceptionOrNull()?.isPermanentNotificationChatFailure() == true
            val cachedRegular = cached?.takeIf {
                it.chatType == barkfluff.shared.Shared.ChatType.CHAT_TYPE_REGULAR
            }
            if (permanentFailure ||
                freshChat?.chatType?.let { it != barkfluff.shared.Shared.ChatType.CHAT_TYPE_REGULAR } == true ||
                (freshChat == null && cachedRegular == null)) {
                pendingNotificationRoute = null
                showChatsAfterNotification()
                Toast.makeText(this, R.string.notification_chat_unavailable, Toast.LENGTH_LONG).show()
                return
            }
            val chat = freshChat ?: cachedRegular ?: return
            val title = chat.title.ifBlank { cachedRegular?.title.orEmpty() }
            val avatarId = chat.picturePreviewFileId.ifBlank { chat.pictureFileId }
            val otherUserId = if (chat.isGroupChat) 0L else
                chat.memberIds.firstOrNull { it != GlobalParam(this).userId } ?: 0L
            showChatsAfterNotification()
            val chatIntent = Intent(this, ChatActivity::class.java).apply {
                putExtra("chat_id", route.chatId)
                putExtra("chat_title", title)
                putExtra("chat_avatar_file_id", avatarId)
                putExtra("is_group_chat", chat.isGroupChat)
                putExtra("other_user_id", otherUserId)
                putExtra("first_unread_message_id", chat.firstUnreadMessageId)
                putExtra(NotificationHelper.EXTRA_SCOPE_ID, route.scopeId)
                flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
            }
            startActivity(chatIntent)
            pendingNotificationRoute = null
            return
        }

        startActivity(Intent(this, ChatActivity::class.java).apply {
            putExtra("chat_id", route.chatId)
            flags = Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP
        })
        pendingNotificationRoute = null
    }

    private fun clearConsumedRouteIntent() {
        setIntent(Intent(this, MainActivity::class.java))
    }

    private fun Throwable.isPermanentNotificationChatFailure(): Boolean {
        val errorCodeKey = Metadata.Key.of("x-error-code", Metadata.ASCII_STRING_MARSHALLER)
        val chain = generateSequence(this) { it.cause }.toList()
        val errorCode = chain.firstNotNullOfOrNull { error ->
            when (error) {
                is StatusException -> error.trailers?.get(errorCodeKey)
                is StatusRuntimeException -> error.trailers?.get(errorCodeKey)
                else -> null
            }
        }
        val permanentCodes = setOf(
            "604DD334-0484-4C6B-8113-354B9D2FDF2A", // NoAccessToChat
            "7506386A-8940-4F3B-87B8-315DD0A7AB08", // ChatNotFound
            "D0AC27C0-3381-401D-8EF4-E35610C372AB", // ChatTypeMismatch
        )
        return errorCode in permanentCodes || chain.any { error ->
            when (error) {
                is StatusException -> error.status.code in setOf(Status.Code.NOT_FOUND, Status.Code.PERMISSION_DENIED,
                    Status.Code.INVALID_ARGUMENT)
                is StatusRuntimeException -> error.status.code in setOf(Status.Code.NOT_FOUND, Status.Code.PERMISSION_DENIED,
                    Status.Code.INVALID_ARGUMENT)
                else -> false
            }
        }
    }

    private fun splashIntent(route: NotificationRoute) = Intent(this, SplashActivity::class.java).apply {
        if (route.isNotification) {
            action = Intent.ACTION_VIEW
            data = route.dataUri?.let(android.net.Uri::parse)
            putExtra(NotificationHelper.EXTRA_SCOPE_ID, route.scopeId)
            putExtra(NotificationHelper.EXTRA_KIND, route.kind)
            putExtra(NotificationHelper.EXTRA_CHAT_ID, route.chatId)
            putExtra(NotificationHelper.EXTRA_EVENT_ID, route.eventId)
            putExtra(NotificationHelper.EXTRA_THREAD_ID, route.threadId)
            putExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT, route.isPrivateInvite)
        } else {
            putExtra(NotificationHelper.EXTRA_CHAT_ID, route.chatId)
            putExtra(NotificationHelper.EXTRA_IS_PRIVATE_CHAT, route.isPrivateInvite)
        }
    }

    private fun showChatsAfterNotification() {
        if (currentTabIndex != TAB_CHATS) switchTab(TAB_CHATS)
        binding.bottomNavigation.selectedItemId = R.id.navigation_chats
    }

    private fun showNotificationRouteError() {
        showChatsAfterNotification()
        Toast.makeText(this, R.string.notification_chat_unavailable, Toast.LENGTH_LONG).show()
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
        lifecycleScope.launch {
            val searchResult = userDirectoryGateway.search(username, size = 20)
            if (searchResult.isFailure) {
                Toast.makeText(this@MainActivity, R.string.main_user_search_failed, Toast.LENGTH_SHORT).show()
                return@launch
            }

            val users = searchResult.getOrNull() ?: emptyList()
            val user = users.find { it.username.equals(username, ignoreCase = true) }

            if (user == null) {
                Toast.makeText(this@MainActivity, getString(R.string.deep_link_user_not_found, username), Toast.LENGTH_SHORT).show()
                return@launch
            }

            val chatResult = userDirectoryGateway.personChatId(user.userId)
            if (chatResult.isFailure) {
                Toast.makeText(this@MainActivity, R.string.deep_link_chat_open_failed, Toast.LENGTH_SHORT).show()
                return@launch
            }

            val chatId = chatResult.getOrNull()!!
            val displayName = getString(R.string.register_full_name_format, user.firstName, user.lastName)
                .trim().ifBlank { user.username }
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
        pendingNotificationRoute?.let { route ->
            outState.putString(KEY_ROUTE_SCOPE, route.scopeId)
            outState.putString(KEY_ROUTE_KIND, route.kind)
            outState.putString(KEY_ROUTE_CHAT, route.chatId)
            outState.putString(KEY_ROUTE_EVENT, route.eventId)
            outState.putString(KEY_ROUTE_THREAD, route.threadId)
            outState.putString(KEY_ROUTE_DATA, route.dataUri)
            outState.putBoolean(KEY_ROUTE_NOTIFICATION, route.isNotification)
            outState.putBoolean(KEY_ROUTE_PRIVATE_INVITE, route.isPrivateInvite)
        }
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
                return
            }
        }

        if (!NotificationManagerCompat.from(this).areNotificationsEnabled()) {
            MaterialAlertDialogBuilder(this)
                .setTitle(R.string.notification_settings_title)
                .setMessage(R.string.incoming_call_notifications_message)
                .setNegativeButton(R.string.incoming_call_fullscreen_later, null)
                .setPositiveButton(R.string.incoming_call_fullscreen_open_settings) { _, _ ->
                    runCatching { startActivity(NotificationHelper.notificationSettingsIntent(this)) }
                        .onFailure { Log.w("MainActivity", "Failed to open notification settings", it) }
                }
                .show()
            return
        }

        requestFullScreenIntentPermission()
    }

    private fun requestFullScreenIntentPermission() {
        val settingsIntent = NotificationHelper.fullScreenIntentSettingsIntent(this) ?: return

        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.incoming_call_fullscreen_title)
            .setMessage(R.string.incoming_call_fullscreen_message)
            .setNegativeButton(R.string.incoming_call_fullscreen_later, null)
            .setPositiveButton(R.string.incoming_call_fullscreen_open_settings) { _, _ ->
                runCatching { startActivity(settingsIntent) }
                    .onFailure { Log.w("MainActivity", "Failed to open full-screen intent settings", it) }
            }
            .show()
    }

    @SuppressLint("MissingSuperCall")
    @Suppress("DEPRECATION")
    override fun onBackPressed() {
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.logout_dialog_title)
            .setMessage(R.string.logout_dialog_message)
            .setPositiveButton(R.string.logout_dialog_confirm) { _, _ ->
                finishAffinity()
            }
            .setNegativeButton(R.string.btn_cancel, null)
            .show()
    }
}
