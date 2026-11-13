package com.barkfluff.client

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.animation.ArgbEvaluator
import android.animation.ValueAnimator
import android.content.res.ColorStateList
import android.graphics.Color
import android.os.Bundle
import com.barkfluff.client.data.AutoDownloadSettingsStore
import com.barkfluff.client.domain.media.AutoDownloadNetworkState
import com.barkfluff.client.adapter.AttachmentAutoDownloadViews
import com.barkfluff.client.adapter.FileMediaAttachmentLoader
import android.util.Log
import android.view.View
import android.view.animation.DecelerateInterpolator
import android.widget.TextView
import androidx.activity.OnBackPressedCallback
import androidx.annotation.DrawableRes
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.widget.doAfterTextChanged
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.repeatOnLifecycle
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import barkfluff.calls.CallsApiOuterClass
import com.barkfluff.client.adapter.AttachmentPreviewAdapter
import com.barkfluff.client.badges.BadgeIconAdapter
import com.barkfluff.client.badges.BadgeImageLoader
import com.barkfluff.client.badges.BadgeViewerSheet
import com.barkfluff.client.cache.UserBadgeRepository
import com.barkfluff.client.calls.CallActivity
import com.barkfluff.client.calls.CallExtras
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityUserProfileBinding
import com.barkfluff.client.databinding.ItemProfileInfoRowBinding
import com.barkfluff.client.domain.gateway.CallGateway
import com.barkfluff.client.domain.gateway.FileMediaGateway
import com.barkfluff.client.domain.gateway.MessageGateway
import com.barkfluff.client.domain.gateway.PresenceGateway
import com.barkfluff.client.domain.gateway.UserProfileGateway
import com.barkfluff.client.domain.gateway.UserSettingsGateway
import com.barkfluff.client.domain.model.UserBadgeInfo
import com.barkfluff.client.domain.model.UserProfile
import com.barkfluff.client.domain.model.splitUserBadges
import coil.load
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.FileMediaUrl
import com.barkfluff.client.utils.OnlineTimeFormatter
import com.barkfluff.client.utils.applyImeInsetsToBottomPadding
import com.barkfluff.client.view.M3ContextMenu
import com.barkfluff.client.view.PullToExpandScrollView
import com.google.android.material.color.MaterialColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.DateFormat
import java.util.Date
import java.util.Locale

/**
 * Экран профиля пользователя / группового чата.
 * Открывается как отдельная Activity из ChatActivity.
 */
@AndroidEntryPoint
class UserProfileActivity : AppCompatActivity() {

    @javax.inject.Inject lateinit var audioPlayback: com.barkfluff.client.audio.AudioPlayback
    private lateinit var binding: ActivityUserProfileBinding
    private lateinit var globalParam: GlobalParam

    @javax.inject.Inject lateinit var callGateway: CallGateway
    @javax.inject.Inject lateinit var fileMediaGateway: FileMediaGateway
    @javax.inject.Inject lateinit var autoDownloadSettings: AutoDownloadSettingsStore
    @javax.inject.Inject lateinit var autoDownloadNetwork: AutoDownloadNetworkState
    @javax.inject.Inject lateinit var messageGateway: MessageGateway
    @javax.inject.Inject lateinit var presenceGateway: PresenceGateway
    @javax.inject.Inject lateinit var userProfileGateway: UserProfileGateway
    @javax.inject.Inject lateinit var userSettingsGateway: UserSettingsGateway
    @javax.inject.Inject lateinit var userBadgeRepository: UserBadgeRepository

    @javax.inject.Inject lateinit var userDirectoryGateway: com.barkfluff.client.domain.gateway.UserDirectoryGateway

    private val isOwnProfile: Boolean
        get() = !isGroupChat && otherUserId > 0 && otherUserId == globalParam.userId

    private var chatId: String = ""
    private var otherUserId: Long = 0L
    private var isGroupChat: Boolean = false
    private var chatTitle: String = ""
    private var chatAvatarFileId: String? = null
    private var isChatMuted: Boolean = false
    private lateinit var badgeIconAdapter: BadgeIconAdapter
    private var primaryBadge: UserBadgeInfo? = null

    private enum class Tab { MEDIA, FILES, VOICE }

    companion object {
        private const val TAG = "UserProfileActivity"
        private const val PROFILE_BADGE_VIEWER_TAG = "profile-badge-viewer"
        private const val EXTRA_CHAT_ID = "chat_id"
        private const val EXTRA_OTHER_USER_ID = "other_user_id"
        private const val EXTRA_IS_GROUP_CHAT = "is_group_chat"
        private const val EXTRA_CHAT_TITLE = "chat_title"
        private const val EXTRA_CHAT_AVATAR_FILE_ID = "chat_avatar_file_id"
        private const val MENU_TOGGLE_MUTE = 1
        private const val MENU_CHAT_BACKGROUND = 2

        fun createIntent(
            context: Context,
            chatId: String,
            otherUserId: Long,
            isGroupChat: Boolean,
            chatTitle: String,
            chatAvatarFileId: String?
        ): Intent {
            return Intent(context, UserProfileActivity::class.java).apply {
                putExtra(EXTRA_CHAT_ID, chatId)
                putExtra(EXTRA_OTHER_USER_ID, otherUserId)
                putExtra(EXTRA_IS_GROUP_CHAT, isGroupChat)
                putExtra(EXTRA_CHAT_TITLE, chatTitle)
                putExtra(EXTRA_CHAT_AVATAR_FILE_ID, chatAvatarFileId)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityUserProfileBinding.inflate(layoutInflater)
        setContentView(binding.root)
        binding.root.applyImeInsetsToBottomPadding()

        globalParam = GlobalParam(this)

        chatId = intent.getStringExtra(EXTRA_CHAT_ID) ?: run { finish(); return }
        otherUserId = intent.getLongExtra(EXTRA_OTHER_USER_ID, 0L)
        isGroupChat = intent.getBooleanExtra(EXTRA_IS_GROUP_CHAT, false)
        chatTitle = intent.getStringExtra(EXTRA_CHAT_TITLE) ?: ""
        chatAvatarFileId = intent.getStringExtra(EXTRA_CHAT_AVATAR_FILE_ID)

        binding.backButton.setOnClickListener { onBackPressedDispatcher.onBackPressed() }

        setupInfoCard()
        setupUserBadges()
        setupAvatarExpansion()
        setupActions()
        setupAttachmentsRecycler()
        setupTabs()
        loadProfileData()
    }

    // ── Инфо-карта ────────────────────────────────────────────────────────────

    private fun setupInfoCard() {
        binding.rowUsername.bind(R.drawable.ic_person, R.string.profile_username_label)
        binding.rowStatus.bind(R.drawable.ic_history, R.string.profile_status_label)
        binding.rowBio.bind(R.drawable.ic_info, R.string.profile_bio_label)
        binding.rowUserId.bind(R.drawable.ic_badge, R.string.profile_user_id_label)
        binding.rowChatId.bind(R.drawable.ic_forum, R.string.profile_chat_id_label)
        binding.rowRegistration.bind(R.drawable.ic_calendar_month, R.string.profile_registration_label)

        val showIds = globalParam.showIdsInProfile
        val showUserId = showIds && !isGroupChat && otherUserId > 0L

        if (showIds && chatId.isNotBlank()) {
            binding.rowChatId.show(chatId, copyLabel = getString(R.string.profile_chat_id_label))
        }
        if (showUserId) {
            binding.rowUserId.show(
                formatId(otherUserId),
                copyLabel = getString(R.string.profile_user_id_label),
                copyText = otherUserId.toString()
            )
        }
        updateInfoCardVisibility()
    }

    private fun setupUserBadges() {
        badgeIconAdapter = BadgeIconAdapter(::showBadge)
        binding.profileBadgesRecyclerView.layoutManager =
            LinearLayoutManager(this, LinearLayoutManager.HORIZONTAL, false)
        binding.profileBadgesRecyclerView.adapter = badgeIconAdapter
        binding.profilePrimaryBadge.setOnClickListener { primaryBadge?.let(::showBadge) }
    }

    private fun loadUserBadges() {
        if (otherUserId <= 0L) return
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) {
                userBadgeRepository.observe(otherUserId).collect(::renderUserBadges)
            }
        }
        lifecycleScope.launch {
            val result = userBadgeRepository.refresh(otherUserId)
            result.exceptionOrNull()?.let { Log.w(TAG, "Не удалось обновить бейджи пользователя $otherUserId", it) }
        }
    }

    private fun renderUserBadges(badges: List<UserBadgeInfo>) {
        val sections = splitUserBadges(badges)
        primaryBadge = sections.primary
        binding.profilePrimaryBadge.visibility = if (primaryBadge == null) View.GONE else View.VISIBLE
        primaryBadge?.let { badge ->
            BadgeImageLoader.loadMini(binding.profilePrimaryBadge, badge)
            binding.profilePrimaryBadge.contentDescription = badge.name
        }
        badgeIconAdapter.submitList(sections.others)
        binding.profileBadgesCard.visibility = if (sections.others.isEmpty()) View.GONE else View.VISIBLE
    }

    private fun showBadge(badge: UserBadgeInfo) {
        BadgeViewerSheet.newInstance(badge)
            .show(supportFragmentManager, PROFILE_BADGE_VIEWER_TAG)
    }

    private fun ItemProfileInfoRowBinding.bind(@DrawableRes icon: Int, label: Int) {
        rowIcon.setImageResource(icon)
        rowLabel.setText(label)
    }

    /** Показывает строку; при [copyLabel] строка копирует [copyText] (по умолчанию — значение). */
    private fun ItemProfileInfoRowBinding.show(value: CharSequence, copyLabel: String? = null, copyText: String = value.toString()) {
        rowValue.text = value
        root.visibility = View.VISIBLE
        if (copyLabel != null) {
            rowTrailingIcon.visibility = View.VISIBLE
            root.setBackgroundResource(selectableItemBackgroundRes())
            root.setOnClickListener { copyToClipboard(copyLabel, copyText) }
        }
        updateInfoCardVisibility()
    }

    private fun selectableItemBackgroundRes(): Int {
        val value = android.util.TypedValue()
        theme.resolveAttribute(android.R.attr.selectableItemBackground, value, true)
        return value.resourceId
    }

    private fun updateInfoCardVisibility() {
        val rows = listOf(
            binding.rowUsername, binding.rowStatus, binding.rowBio,
            binding.rowUserId, binding.rowChatId, binding.rowRegistration
        )
        binding.profileInfoCard.visibility =
            if (rows.any { it.root.visibility == View.VISIBLE }) View.VISIBLE else View.GONE
    }

    /** Форматирует число ID с разделением на группы по 3 цифры. */
    private fun formatId(id: Long): String {
        return "%,d".format(Locale.getDefault(), id).replace(',', ' ')
    }

    private fun copyToClipboard(label: String, text: String) {
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText(label, text))
        Toast.makeText(this, getString(R.string.profile_copied), Toast.LENGTH_SHORT).show()
    }

    // ── Раскрытие аватара оттягиванием ────────────────────────────────────────

    /** 0 — круглый аватар, 1 — квадратное фото на всю ширину под именем и кнопками. */
    private var avatarExpansion = 0f
    private var avatarStretchMax = 0f
    private var avatarPullStartedExpanded = false
    private var avatarExpansionAnimator: ValueAnimator? = null
    private val argbEvaluator = ArgbEvaluator()
    private val avatarCollapseOnBack = object : OnBackPressedCallback(false) {
        override fun handleOnBackPressed() = animateAvatarExpansion(0f)
    }

    private fun setupAvatarExpansion() {
        onBackPressedDispatcher.addCallback(this, avatarCollapseOnBack)
        binding.profileHeader.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ -> applyAvatarMorph() }
        // AvatarLoader changes GONE to VISIBLE after loading. That changes the photo's
        // own bounds without changing the header bounds, so refresh its translation too.
        binding.profileAvatarImageView.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ -> applyAvatarMorph() }
        binding.profileAvatarFrame.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ -> applyAvatarMorph() }
        binding.profileNameRow.addOnLayoutChangeListener { _, _, _, _, _, _, _, _, _ -> applyAvatarMorph() }
        binding.profileScrollView.listener = object : PullToExpandScrollView.Listener {
            override fun canPull(dy: Float): Boolean =
                isAvatarExpandable() && (dy > 0f || avatarExpansion > 0f)

            override fun onPull(dy: Float): Boolean {
                if (avatarExpansionAnimator?.isRunning == true) {
                    avatarExpansionAnimator?.cancel()
                } else if (avatarExpansion == 0f && dy > 0f) {
                    avatarPullStartedExpanded = false
                }
                if (avatarStretchMax <= 0f) avatarStretchMax = computeAvatarStretchMax()
                if (avatarStretchMax <= 0f) return false
                setAvatarExpansion((avatarExpansion + dy / avatarStretchMax).coerceIn(0f, 1f))
                return avatarExpansion > 0f || dy > 0f
            }

            override fun onPullReleased() {
                // Порог зависит от направления: лёгкий рывок раскрывает, лёгкий возврат — сворачивает.
                val threshold = if (avatarPullStartedExpanded) 0.8f else 0.2f
                animateAvatarExpansion(if (avatarExpansion >= threshold) 1f else 0f)
            }
        }
    }

    private fun isAvatarExpandable(): Boolean =
        binding.profileAvatarImageView.visibility == View.VISIBLE && binding.profileAvatarImageView.drawable != null

    /** Насколько вытянуть шапку, чтобы её низ совпал с низом квадратного фото. */
    private fun computeAvatarStretchMax(): Float {
        val header = binding.profileHeader
        val collapsedHeight = header.height - binding.profileHeaderStretch.height
        return (header.width - collapsedHeight).toFloat()
    }

    private fun animateAvatarExpansion(target: Float) {
        avatarExpansionAnimator?.cancel()
        if (avatarStretchMax <= 0f) avatarStretchMax = computeAvatarStretchMax()
        avatarExpansionAnimator = ValueAnimator.ofFloat(avatarExpansion, target).apply {
            duration = 260L
            interpolator = DecelerateInterpolator(1.6f)
            addUpdateListener { setAvatarExpansion(it.animatedValue as Float) }
            start()
        }
        avatarPullStartedExpanded = target == 1f
    }

    private fun setAvatarExpansion(value: Float) {
        avatarExpansion = value
        avatarCollapseOnBack.isEnabled = value > 0f
        val stretch = (avatarStretchMax * value).toInt().coerceAtLeast(0)
        val stretchView = binding.profileHeaderStretch
        if (stretchView.minimumHeight != stretch) {
            stretchView.minimumHeight = stretch
            stretchView.requestLayout()
        }
        applyAvatarMorph()
    }

    /** Ставит фото по прогрессу: круг внутри кольца аватара → квадрат на всю ширину. */
    private fun applyAvatarMorph() {
        val photo = binding.profileAvatarImageView
        val frame = binding.profileAvatarFrame
        val width = binding.profileHeader.width.toFloat()
        if (width <= 0f || frame.width <= 0) return
        val p = avatarExpansion

        // Рамка уже может быть разложена с прежней растяжкой — учитываем целевую.
        val pendingStretch = binding.profileHeaderStretch.minimumHeight - binding.profileHeaderStretch.height
        val circleSize = (frame.width - frame.paddingLeft - frame.paddingRight).toFloat()
        val circleLeft = (frame.left + frame.paddingLeft).toFloat()
        val circleTop = (frame.top + frame.paddingTop + pendingStretch).toFloat()

        val size = lerp(circleSize, width, p)
        photo.setAvatarBounds(
            left = lerp(circleLeft, 0f, p) - photo.left,
            top = lerp(circleTop, 0f, p) - photo.top,
            size = size,
            cornerRadius = (1f - p) * size / 2f,
        )

        val nameRow = binding.profileNameRow
        val leftInset = 20f * resources.displayMetrics.density
        nameRow.translationX = (leftInset - nameRow.left) * p

        frame.alpha = 1f - p
        binding.profileAvatarExpandedScrim.alpha = p
        applyHeaderColors(p)
    }

    private var appliedHeaderColorsProgress = 0f

    private fun applyHeaderColors(p: Float) {
        if (p == appliedHeaderColorsProgress) return
        appliedHeaderColorsProgress = p
        val white = Color.WHITE
        val overlay = 0x40FFFFFF
        binding.profileNameTextView.setTextColor(blend(ContextCompat.getColor(this, R.color.profile_text), white, p))
        binding.profileNameTextView.setShadowLayer(4f * p, 0f, 1f, 0x66000000)

        val onAccent = blend(ContextCompat.getColor(this, R.color.profile_on_accent), white, p)
        binding.actionMessageButton.backgroundTintList =
            ColorStateList.valueOf(blend(ContextCompat.getColor(this, R.color.profile_accent), overlay, p))
        binding.actionMessageButton.setTextColor(onAccent)
        binding.actionMessageButton.iconTint = ColorStateList.valueOf(onAccent)

        binding.actionCallButton.backgroundTintList = if (p == 0f) null else ColorStateList.valueOf(
            blend(
                MaterialColors.getColor(binding.actionCallButton, com.google.android.material.R.attr.colorSurfaceContainerHigh),
                overlay,
                p
            )
        )
        binding.actionCallIcon.imageTintList =
            ColorStateList.valueOf(blend(ContextCompat.getColor(this, R.color.profile_accent), white, p))
    }

    private fun blend(from: Int, to: Int, p: Float): Int = argbEvaluator.evaluate(p, from, to) as Int

    private fun lerp(from: Float, to: Float, p: Float): Float = from + (to - from) * p

    // ── Кнопки действий ───────────────────────────────────────────────────────

    private fun setupActions() {
        if (isOwnProfile) {
            binding.profileActionsRow.visibility = View.VISIBLE
            binding.actionCallButton.visibility = View.GONE
            binding.moreButton.visibility = View.GONE
            binding.actionMessageButton.setText(R.string.saved_messages_title)
            binding.actionMessageButton.setIconResource(R.drawable.ic_saved_messages)
            binding.actionMessageButton.setOnClickListener { openSavedMessages() }
            return
        }
        if (chatId.isBlank()) {
            binding.profileActionsRow.visibility = View.GONE
            binding.moreButton.visibility = View.GONE
            return
        }
        if (isGroupChat) {
            binding.profileActionsRow.visibility = View.GONE
            return
        }

        binding.actionMessageButton.setOnClickListener { openChat() }
        binding.actionCallButton.setOnClickListener { startCall() }

        isChatMuted = chatId in globalParam.mutedChatIds
        binding.moreButton.visibility = View.VISIBLE
        binding.moreButton.setOnClickListener { showProfileMenu(it) }
    }

    /** Меню «три точки»: уведомления чата и фон чата. */
    private fun showProfileMenu(anchor: View) {
        M3ContextMenu.show(
            anchor = anchor,
            items = listOf(
                M3ContextMenu.Item(
                    id = MENU_TOGGLE_MUTE,
                    title = getString(if (isChatMuted) R.string.chat_menu_unmute else R.string.chat_menu_mute),
                    icon = if (isChatMuted) R.drawable.ic_notifications else R.drawable.ic_notifications_off,
                ),
                M3ContextMenu.Item(
                    id = MENU_CHAT_BACKGROUND,
                    title = getString(R.string.profile_chat_background),
                    icon = R.drawable.ic_photo_library,
                ),
            ),
            alignEnd = true,
            onItemSelected = { item ->
                when (item.id) {
                    MENU_TOGGLE_MUTE -> toggleChatMute()
                    MENU_CHAT_BACKGROUND -> showChatBackgroundDialog()
                }
            },
        )
    }

    private fun showChatBackgroundDialog() {
        lifecycleScope.launch {
            val fileIds = userSettingsGateway.personalization().getOrElse {
                Toast.makeText(this@UserProfileActivity, R.string.profile_background_load_error, Toast.LENGTH_SHORT).show()
                return@launch
            }
            val labels = listOf(getString(R.string.profile_use_global_background)) +
                fileIds.map { getString(R.string.profile_background_named, it.take(8)) }
            val current = globalParam.chatBackgroundOverrides[chatId]
            var selected = fileIds.indexOf(current).takeIf { it >= 0 }?.plus(1) ?: 0
            MaterialAlertDialogBuilder(this@UserProfileActivity)
                .setTitle(R.string.profile_chat_background)
                .setSingleChoiceItems(labels.toTypedArray(), selected) { _, which -> selected = which }
                .setNegativeButton(R.string.btn_cancel, null)
                .setPositiveButton(R.string.btn_apply) { _, _ ->
                    lifecycleScope.launch {
                        val fileId = if (selected == 0) "" else fileIds[selected - 1]
                        val result = userSettingsGateway.setChatBackground(chatId, fileId)
                        if (result.isSuccess) {
                            globalParam.setChatBackgroundOverride(chatId, fileId)
                            Toast.makeText(this@UserProfileActivity, R.string.profile_background_updated, Toast.LENGTH_SHORT).show()
                        } else {
                            Toast.makeText(this@UserProfileActivity, R.string.profile_background_set_error, Toast.LENGTH_SHORT).show()
                        }
                    }
                }
                .show()
        }
    }

    private fun toggleChatMute() {
        val newMuted = !isChatMuted
        lifecycleScope.launch {
            val result = userSettingsGateway.setChatMuted(chatId, newMuted)
            if (result.isSuccess) {
                isChatMuted = newMuted
                globalParam.setChatMutedLocal(chatId, newMuted)
                Toast.makeText(
                    this@UserProfileActivity,
                    if (newMuted) getString(R.string.chat_muted) else getString(R.string.chat_unmuted),
                    Toast.LENGTH_SHORT
                ).show()
            } else {
                Toast.makeText(this@UserProfileActivity, getString(R.string.chat_mute_error), Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun openSavedMessages() {
        if (!binding.actionMessageButton.isEnabled) return
        binding.actionMessageButton.isEnabled = false
        lifecycleScope.launch {
            try {
                val savedChatId = chatId.ifBlank {
                    userDirectoryGateway.personChatId(globalParam.userId).getOrThrow()
                }
                if (savedChatId.isBlank()) return@launch
                startActivity(Intent(this@UserProfileActivity, ChatActivity::class.java).apply {
                    putExtra("chat_id", savedChatId)
                    putExtra("chat_title", getString(R.string.saved_messages_title))
                    putExtra("is_group_chat", false)
                    putExtra("other_user_id", globalParam.userId)
                    flags = Intent.FLAG_ACTIVITY_CLEAR_TOP
                })
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                Toast.makeText(this@UserProfileActivity,
                    getString(R.string.chat_open_failed_detail, e.message.orEmpty()), Toast.LENGTH_SHORT).show()
            } finally {
                binding.actionMessageButton.isEnabled = true
            }
        }
    }

    private fun openChat() {
        startActivity(Intent(this, ChatActivity::class.java).apply {
            putExtra("chat_id", chatId)
            putExtra("chat_title", chatTitle)
            putExtra("chat_avatar_file_id", chatAvatarFileId)
            putExtra("is_group_chat", isGroupChat)
            putExtra("other_user_id", otherUserId)
            flags = Intent.FLAG_ACTIVITY_CLEAR_TOP
        })
        finish()
    }

    private fun startCall() {
        lifecycleScope.launch {
            if (otherUserId <= 0L) {
                Toast.makeText(this@UserProfileActivity, R.string.chat_call_user_missing, Toast.LENGTH_SHORT).show()
                return@launch
            }

            val result = callGateway.initiateDirect(otherUserId, CallsApiOuterClass.CallMediaType.CALL_MEDIA_AUDIO)
            result.onSuccess { response ->
                startActivity(Intent(this@UserProfileActivity, CallActivity::class.java).apply {
                    putExtra(CallExtras.EXTRA_CALL_ID, response.callId)
                    putExtra(CallExtras.EXTRA_CALLER_NAME, chatTitle)
                    putExtra(CallExtras.EXTRA_CHAT_ID, chatId)
                    putExtra(CallExtras.EXTRA_MEDIA_TYPE, "audio")
                    putExtra(CallExtras.EXTRA_LIVEKIT_URL, response.livekitUrl.ifBlank { globalParam.livekitUrl })
                    putExtra(CallExtras.EXTRA_ACCESS_TOKEN, response.accessToken)
                })
            }.onFailure { error ->
                Log.e(TAG, "Failed to start call", error)
                Toast.makeText(this@UserProfileActivity, R.string.call_start_failed, Toast.LENGTH_SHORT).show()
            }
        }
    }

    // ── Attachments ───────────────────────────────────────────────────────────

    private data class AttachmentsPanel(
        val container: View,
        val loading: View,
        val recyclerView: RecyclerView,
        val empty: TextView,
        val adapter: AttachmentPreviewAdapter
    )

    private lateinit var mediaAttachmentsPanel: AttachmentsPanel
    private lateinit var filesAttachmentsPanel: AttachmentsPanel
    private lateinit var voiceAttachmentsPanel: AttachmentsPanel
    private var selectedAttachmentTab = Tab.MEDIA
    private var fileSearchJob: Job? = null
    private val attachmentLoadVersions = mutableMapOf<Tab, Int>()

    private fun setupAttachmentsRecycler() {
        mediaAttachmentsPanel = createAttachmentsPanel(
            binding.mediaAttachmentsPanel,
            binding.mediaAttachmentsLoading,
            binding.mediaAttachmentsRecyclerView,
            binding.mediaAttachmentsEmpty
        )
        filesAttachmentsPanel = createAttachmentsPanel(
            binding.filesAttachmentsPanel,
            binding.filesAttachmentsLoading,
            binding.filesAttachmentsRecyclerView,
            binding.filesAttachmentsEmpty
        )
        voiceAttachmentsPanel = createAttachmentsPanel(
            binding.voiceAttachmentsPanel,
            binding.voiceAttachmentsLoading,
            binding.voiceAttachmentsRecyclerView,
            binding.voiceAttachmentsEmpty
        )

        binding.mediaAttachmentsRecyclerView.layoutManager = GridLayoutManager(this, 3)
        binding.filesAttachmentsRecyclerView.layoutManager = LinearLayoutManager(this)
        binding.voiceAttachmentsRecyclerView.layoutManager = LinearLayoutManager(this)
        binding.fileSearchEditText.doAfterTextChanged { rawQuery -> scheduleFileSearch(rawQuery?.toString().orEmpty()) }
    }

    private fun createAttachmentsPanel(
        container: View,
        loading: View,
        recyclerView: RecyclerView,
        empty: TextView
    ): AttachmentsPanel {
        lateinit var adapter: AttachmentPreviewAdapter
        adapter = AttachmentPreviewAdapter(
            playback = audioPlayback,
            playbackChatId = chatId,
            playbackChatTitle = chatTitle,
            playbackIsGroupChat = isGroupChat,
            playbackOtherUserId = otherUserId,
            resolveSender = { userId ->
                userProfileGateway.user(userId).getOrNull()?.let { user ->
                    listOf(user.firstName, user.lastName).filter(String::isNotBlank).joinToString(" ").ifBlank { user.username }
                }
            },
            getFileUrl = { fileId -> fileMediaGateway.downloadUrl(fileId).getOrNull() },
            onAttachmentClick = { attachmentInfo ->
                val att = attachmentInfo.attachment
                when (att.type) {
                    barkfluff.shared.Shared.MessageAttachmentType.IMAGE,
                    barkfluff.shared.Shared.MessageAttachmentType.GIF -> {
                        val allFileIds = adapter.currentList.map { it.attachment.fileId }
                        val allPreviewUrls = adapter.currentList.map { FileMediaUrl.rewrite(this@UserProfileActivity, it.attachment.previewUrl) }
                        val allFileNames = adapter.currentList.map { it.attachment.fileName }
                        val sourceMessageIds = adapter.currentList.map { it.messageId }
                        val position = adapter.currentList.indexOf(attachmentInfo).coerceAtLeast(0)
                        startActivity(
                            ImageViewerActivity.createIntent(
                                this,
                                allFileIds,
                                allPreviewUrls,
                                position,
                                fileNames = allFileNames,
                                sourceMessageIds = sourceMessageIds
                            )
                        )
                    }
                    barkfluff.shared.Shared.MessageAttachmentType.VIDEO -> {
                        val cachedPath = FileCache.getFile(att.fileId)?.absolutePath
                        startActivity(
                            MediaViewerActivity.createIntent(
                                this,
                                att.fileId,
                                att.fileName.ifBlank { getString(R.string.attachment_video) },
                                cachedPath
                            )
                        )
                    }
                    else -> {
                        lifecycleScope.launch {
                            try {
                                val file = withContext(Dispatchers.IO) {
                                    FileCache.getFile(att.fileId)
                                        ?: fileMediaGateway.download(att.fileId)
                                }
                                if (file != null) {
                                    val uri = androidx.core.content.FileProvider.getUriForFile(
                                        this@UserProfileActivity,
                                        "${packageName}.fileprovider",
                                        file
                                    )
                                    val mimeType = getMimeType(att.fileName) ?: "*/*"
                                    val intent = Intent(Intent.ACTION_VIEW).apply {
                                        setDataAndType(uri, mimeType)
                                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                                    }
                                    startActivity(Intent.createChooser(intent, getString(R.string.open_with)))
                                } else {
                                    Toast.makeText(this@UserProfileActivity, R.string.profile_download_failed, Toast.LENGTH_SHORT).show()
                                }
                            } catch (e: Exception) {
                                Log.e(TAG, "Error opening file", e)
                                Toast.makeText(this@UserProfileActivity, R.string.profile_file_open_error, Toast.LENGTH_SHORT).show()
                            }
                        }
                    }
                }
            },
            downloadToCache = { fileId ->
                FileCache.getFile(fileId) ?: fileMediaGateway.download(fileId)
            },
            scope = lifecycleScope,
            autoDownloadViews = AttachmentAutoDownloadViews(FileMediaAttachmentLoader(fileMediaGateway), this, autoDownloadSettings, autoDownloadNetwork),
        )
        recyclerView.adapter = adapter
        return AttachmentsPanel(container, loading, recyclerView, empty, adapter)
    }

    // ── Табы вложений ─────────────────────────────────────────────────────────

    private fun setupTabs() {
        if (chatId.isBlank()) {
            binding.attachmentsTitle.visibility = View.GONE
            binding.attachmentTabs.visibility = View.GONE
            binding.attachmentsContainer.visibility = View.GONE
            return
        }
        binding.tabMedia.setOnClickListener { selectTab(Tab.MEDIA) }
        binding.tabFiles.setOnClickListener { selectTab(Tab.FILES) }
        binding.tabVoice.setOnClickListener { selectTab(Tab.VOICE) }
        selectTab(Tab.MEDIA)
    }

    private fun selectTab(tab: Tab) {
        selectedAttachmentTab = tab
        styleTab(binding.tabMedia, tab == Tab.MEDIA)
        styleTab(binding.tabFiles, tab == Tab.FILES)
        styleTab(binding.tabVoice, tab == Tab.VOICE)
        mediaAttachmentsPanel.container.visibility = if (tab == Tab.MEDIA) View.VISIBLE else View.GONE
        filesAttachmentsPanel.container.visibility = if (tab == Tab.FILES) View.VISIBLE else View.GONE
        voiceAttachmentsPanel.container.visibility = if (tab == Tab.VOICE) View.VISIBLE else View.GONE

        when (tab) {
            Tab.MEDIA -> loadMedia()
            Tab.FILES -> loadFiles(binding.fileSearchEditText.text?.toString().orEmpty())
            Tab.VOICE -> loadAttachments(Tab.VOICE, barkfluff.shared.Shared.MessageAttachmentType.VOICE)
        }
    }

    private fun styleTab(view: TextView, selected: Boolean) {
        if (selected) {
            view.setBackgroundResource(R.drawable.bg_pill_selected)
            view.setTextColor(
                MaterialColors.getColor(view, com.google.android.material.R.attr.colorOnPrimary)
            )
        } else {
            view.background = null
            view.setTextColor(
                MaterialColors.getColor(view, com.google.android.material.R.attr.colorOnSurfaceVariant)
            )
        }
    }

    private fun panelFor(tab: Tab): AttachmentsPanel = when (tab) {
        Tab.MEDIA -> mediaAttachmentsPanel
        Tab.FILES -> filesAttachmentsPanel
        Tab.VOICE -> voiceAttachmentsPanel
    }

    private fun showLoading(tab: Tab) {
        panelFor(tab).run {
            loading.visibility = View.VISIBLE
            recyclerView.visibility = View.GONE
            empty.visibility = View.GONE
        }
    }

    private fun showEmpty(tab: Tab, text: String) {
        panelFor(tab).run {
            loading.visibility = View.GONE
            recyclerView.visibility = View.GONE
            empty.visibility = View.VISIBLE
            empty.text = text
        }
    }

    private fun showList(tab: Tab, items: List<barkfluff.messages.MessagesApiOuterClass.ChatAttachmentInfo>) {
        panelFor(tab).run {
            adapter.submitList(items)
            loading.visibility = View.GONE
            empty.visibility = View.GONE
            recyclerView.visibility = View.VISIBLE
        }
    }

    /** Медиа = фото + GIF + видео, объединённые и отсортированные по дате отправки. */
    private fun loadMedia() {
        val version = nextLoadVersion(Tab.MEDIA)
        val requestedChatId = chatId
        showLoading(Tab.MEDIA)
        lifecycleScope.launch {
            try {
                val images = messageGateway.attachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.IMAGE).getOrNull().orEmpty()
                val gifs = messageGateway.attachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.GIF).getOrNull().orEmpty()
                val videos = messageGateway.attachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.VIDEO).getOrNull().orEmpty()
                val merged = (images + gifs + videos).sortedByDescending { it.sentAt.seconds }
                if (!isCurrentLoad(Tab.MEDIA, version, requestedChatId)) return@launch
                if (merged.isEmpty()) showEmpty(Tab.MEDIA, getString(R.string.media_no_attachments)) else showList(Tab.MEDIA, merged)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading media", e)
                if (isCurrentLoad(Tab.MEDIA, version, requestedChatId)) {
                    showEmpty(Tab.MEDIA, getString(R.string.media_no_attachments))
                }
            }
        }
    }

    private fun loadFiles(rawQuery: String) {
        val query = rawQuery.trim()
        val version = nextLoadVersion(Tab.FILES)
        val requestedChatId = chatId
        showLoading(Tab.FILES)
        lifecycleScope.launch {
            try {
                val result = messageGateway.attachments(
                    chatId,
                    barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT,
                    pageSize = if (query.isBlank()) 100 else 30,
                    fileNameQuery = query
                )
                val attachments = result.getOrNull()
                if (!isCurrentLoad(Tab.FILES, version, requestedChatId)) return@launch
                if (attachments.isNullOrEmpty()) {
                    showEmpty(Tab.FILES, getString(if (query.isBlank()) R.string.media_no_attachments else R.string.profile_files_not_found))
                } else showList(Tab.FILES, attachments)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading attachments", e)
                if (isCurrentLoad(Tab.FILES, version, requestedChatId)) {
                    showEmpty(Tab.FILES, getString(if (query.isBlank()) R.string.media_no_attachments else R.string.profile_files_not_found))
                }
            }
        }
    }

    private fun loadAttachments(tab: Tab, type: barkfluff.shared.Shared.MessageAttachmentType) {
        val version = nextLoadVersion(tab)
        val requestedChatId = chatId
        showLoading(tab)
        lifecycleScope.launch {
            try {
                val attachments = messageGateway.attachments(chatId, type).getOrNull()
                if (!isCurrentLoad(tab, version, requestedChatId)) return@launch
                if (attachments.isNullOrEmpty()) showEmpty(tab, getString(R.string.profile_no_voice)) else showList(tab, attachments)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading attachments", e)
                if (isCurrentLoad(tab, version, requestedChatId)) showEmpty(tab, getString(R.string.profile_no_voice))
            }
        }
    }

    private fun scheduleFileSearch(rawQuery: String) {
        fileSearchJob?.cancel()
        invalidateLoad(Tab.FILES)
        fileSearchJob = lifecycleScope.launch {
            delay(300)
            if (selectedAttachmentTab == Tab.FILES) loadFiles(rawQuery)
        }
    }

    private fun nextLoadVersion(tab: Tab): Int {
        val next = (attachmentLoadVersions[tab] ?: 0) + 1
        attachmentLoadVersions[tab] = next
        return next
    }

    private fun invalidateLoad(tab: Tab) {
        nextLoadVersion(tab)
    }

    private fun isCurrentLoad(tab: Tab, version: Int, requestedChatId: String): Boolean =
        selectedAttachmentTab == tab && attachmentLoadVersions[tab] == version && chatId == requestedChatId

    // ── Profile data ──────────────────────────────────────────────────────────

    private fun loadProfileData() {
        if (isGroupChat) {
            loadGroupProfile()
        } else {
            loadUserProfile()
            loadUserBadges()
        }
    }

    private fun loadGroupProfile() {
        binding.profileNameTextView.text = chatTitle.trim()

        if (!chatAvatarFileId.isNullOrBlank()) {
            AvatarLoader.loadByFileId(
                imageView = binding.profileAvatarImageView,
                placeholderView = binding.profileAvatarPlaceholder,
                fileId = chatAvatarFileId!!,
                displayName = chatTitle,
                userId = chatId.hashCode().toLong(),
                circleCrop = false
            ) {
                fileMediaGateway.downloadUrl(chatAvatarFileId!!).getOrNull()
            }
        } else {
            AvatarLoader.showPlaceholder(binding.profileAvatarPlaceholder, chatTitle, chatId.hashCode().toLong())
            binding.profileAvatarImageView.visibility = View.GONE
        }
    }

    private fun loadUserProfile() {
        if (otherUserId <= 0) return

        lifecycleScope.launch {
            try {
                val userResult = userProfileGateway.user(otherUserId)
                if (userResult.isSuccess) {
                    val user = userResult.getOrNull()!!
                    val displayName = "${user.firstName} ${user.lastName}".trim()

                    binding.profileNameTextView.text = if (displayName.isNotBlank()) displayName else user.username
                    if (user.username.isNotBlank()) {
                        val username = getString(R.string.register_username_format, user.username)
                        binding.rowUsername.show(username, copyLabel = getString(R.string.profile_username_label))
                    }

                    if (user.bio.isNotBlank()) {
                        binding.rowBio.show(user.bio.trim())
                    }

                    if (user.registrationDate > 0) {
                        binding.rowRegistration.show(
                            DateFormat.getDateInstance(
                                DateFormat.LONG,
                                resources.configuration.locales[0]
                            ).format(Date(user.registrationDate))
                        )
                    }

                    val avatarFileId = avatarSourceFor(user)
                    if (!avatarFileId.isNullOrBlank()) {
                        AvatarLoader.loadByFileId(
                            imageView = binding.profileAvatarImageView,
                            placeholderView = binding.profileAvatarPlaceholder,
                            fileId = avatarFileId,
                            displayName = displayName.ifBlank { user.username },
                            userId = otherUserId,
                            circleCrop = false
                        ) {
                            fileMediaGateway.downloadUrl(avatarFileId).getOrNull()
                        }
                    } else {
                        AvatarLoader.showPlaceholder(
                            binding.profileAvatarPlaceholder,
                            displayName.ifBlank { user.username },
                            otherUserId
                        )
                        binding.profileAvatarImageView.visibility = View.GONE
                    }

                    val posterFileId = user.profilePosterFileId
                    if (posterFileId.isNotBlank()) {
                        loadPosterImage(posterFileId)
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading user profile", e)
            }
        }

        // Собственный профиль не показывает presence и не запрашивает его.
        if (isOwnProfile) {
            binding.rowStatus.root.visibility = View.GONE
            updateInfoCardVisibility()
            return
        }

        // Онлайн-статус
        lifecycleScope.launch {
            try {
                val userStatus = presenceGateway.status(listOf(otherUserId)).getOrNull()?.firstOrNull()
                val isOnline = userStatus?.isOnline == true
                binding.rowStatus.show(
                    when {
                        userStatus == null -> getString(R.string.profile_last_seen_recently)
                        isOnline -> getString(R.string.profile_online)
                        else -> OnlineTimeFormatter.formatLastSeen(this@UserProfileActivity, userStatus.lastSeenEpochMillis)
                    }
                )
                binding.rowStatus.rowValue.setTextColor(
                    ContextCompat.getColor(
                        this@UserProfileActivity,
                        if (isOnline) R.color.profile_presence_online else R.color.profile_text
                    )
                )
            } catch (e: Exception) {
                Log.e(TAG, "Error loading online status", e)
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private fun avatarSourceFor(user: UserProfile): String? {
        // Оригинал важнее превью: аватар раскрывается на всю ширину экрана.
        return user.profilePictureFileId
            .ifBlank { user.profilePictureUrl }
            .ifBlank { user.profilePicturePreviewFileId }
            .ifBlank { user.profilePicturePreviewUrl }
            .ifBlank { null }
    }

    // The placeholder stays measured: its bottom anchors the collapsed avatar and header stretch.
    private fun loadPosterImage(posterFileId: String) {
        val cachedUrl = AvatarLoader.urlCache[posterFileId]
            ?: AvatarLoader.getUrlFromCache(posterFileId)

        if (cachedUrl != null) {
            binding.profilePosterPlaceholder.visibility = View.INVISIBLE
            binding.profilePosterImageView.visibility = View.VISIBLE
            binding.profilePosterImageView.load(cachedUrl, AvatarLoader.getImageLoader(this)) {
                crossfade(true)
            }
            return
        }

        lifecycleScope.launch {
            try {
                val urlResult = fileMediaGateway.downloadUrl(posterFileId)
                val url = urlResult.getOrNull()
                if (!url.isNullOrBlank()) {
                    AvatarLoader.urlCache[posterFileId] = url
                    AvatarLoader.putUrlInCache(posterFileId, url)

                    binding.profilePosterPlaceholder.visibility = View.INVISIBLE
                    binding.profilePosterImageView.visibility = View.VISIBLE
                    binding.profilePosterImageView.load(url, AvatarLoader.getImageLoader(this@UserProfileActivity)) {
                        crossfade(true)
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading poster image", e)
            }
        }
    }

    private fun getMimeType(fileName: String): String? {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        return when (ext) {
            "pdf" -> "application/pdf"
            "doc", "docx" -> "application/msword"
            "xls", "xlsx" -> "application/vnd.ms-excel"
            "zip" -> "application/zip"
            "mp3" -> "audio/mpeg"
            "mp4" -> "video/mp4"
            "jpg", "jpeg" -> "image/jpeg"
            "png" -> "image/png"
            "gif" -> "image/gif"
            else -> null
        }
    }

    override fun onDestroy() {
        binding.mediaAttachmentsRecyclerView.adapter = null
        binding.filesAttachmentsRecyclerView.adapter = null
        binding.voiceAttachmentsRecyclerView.adapter = null
        fileSearchJob?.cancel()
        avatarExpansionAnimator?.cancel()
        super.onDestroy()
    }
}
