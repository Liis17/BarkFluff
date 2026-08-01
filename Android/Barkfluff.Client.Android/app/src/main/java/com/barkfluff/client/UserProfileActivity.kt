package com.barkfluff.client

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.widget.doAfterTextChanged
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import barkfluff.calls.CallsApiOuterClass
import com.barkfluff.client.adapter.AttachmentPreviewAdapter
import com.barkfluff.client.calls.CallActivity
import com.barkfluff.client.calls.CallExtras
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityUserProfileBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import coil.load
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.OnlineTimeFormatter
import com.google.android.material.color.MaterialColors
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Экран профиля пользователя / группового чата.
 * Открывается как отдельная Activity из ChatActivity.
 */
class UserProfileActivity : AppCompatActivity() {

    private lateinit var binding: ActivityUserProfileBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatRepository: ChatRepository
    private lateinit var globalParam: GlobalParam

    private var chatId: String = ""
    private var otherUserId: Long = 0L
    private var isGroupChat: Boolean = false
    private var chatTitle: String = ""
    private var chatAvatarFileId: String? = null
    private var isChatMuted: Boolean = false

    private enum class Tab { MEDIA, FILES, VOICE }

    companion object {
        private const val TAG = "UserProfileActivity"
        private const val EXTRA_CHAT_ID = "chat_id"
        private const val EXTRA_OTHER_USER_ID = "other_user_id"
        private const val EXTRA_IS_GROUP_CHAT = "is_group_chat"
        private const val EXTRA_CHAT_TITLE = "chat_title"
        private const val EXTRA_CHAT_AVATAR_FILE_ID = "chat_avatar_file_id"

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

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager
        chatRepository = ChatRepository(this, grpcManager)
        globalParam = GlobalParam(this)

        chatId = intent.getStringExtra(EXTRA_CHAT_ID) ?: run { finish(); return }
        otherUserId = intent.getLongExtra(EXTRA_OTHER_USER_ID, 0L)
        isGroupChat = intent.getBooleanExtra(EXTRA_IS_GROUP_CHAT, false)
        chatTitle = intent.getStringExtra(EXTRA_CHAT_TITLE) ?: ""
        chatAvatarFileId = intent.getStringExtra(EXTRA_CHAT_AVATAR_FILE_ID)

        binding.backButton.setOnClickListener { finish() }

        setupInfoCard()
        setupActions()
        setupAttachmentsRecycler()
        setupTabs()
        loadProfileData()
    }

    // ── Инфо-карта ────────────────────────────────────────────────────────────

    private fun setupInfoCard() {
        val showIds = globalParam.showIdsInProfile
        val showUserId = showIds && !isGroupChat && otherUserId > 0L

        binding.rowChatId.visibility = if (showIds) View.VISIBLE else View.GONE
        binding.dividerChatId.visibility = if (showIds) View.VISIBLE else View.GONE
        if (showIds) {
            binding.profileChatIdValue.text = chatId
            binding.rowChatId.setOnClickListener { copyToClipboard("ChatId", chatId) }
        }

        if (showUserId) {
            binding.rowUserId.visibility = View.VISIBLE
            binding.dividerUserId.visibility = View.VISIBLE
            binding.profileUserIdValue.text = formatId(otherUserId)
            binding.rowUserId.setOnClickListener { copyToClipboard("UserId", otherUserId.toString()) }
        } else {
            binding.rowUserId.visibility = View.GONE
            binding.dividerUserId.visibility = View.GONE
        }
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

    // ── Кнопки действий ───────────────────────────────────────────────────────

    private fun setupActions() {
        if (isGroupChat) {
            binding.profileActionsRow.visibility = View.GONE
            return
        }

        binding.actionMessageButton.setOnClickListener { openChat() }
        binding.actionCallButton.setOnClickListener { startCall() }

        isChatMuted = chatId in globalParam.mutedChatIds
        updateNotifyIcon()
        binding.actionNotifyButton.setOnClickListener { toggleChatMute() }
        binding.actionBackgroundButton.setOnClickListener { showChatBackgroundDialog() }
    }

    private fun showChatBackgroundDialog() {
        lifecycleScope.launch {
            val fileIds = grpcManager.getPersonalization().getOrElse {
                Toast.makeText(this@UserProfileActivity, "Не удалось загрузить фоны", Toast.LENGTH_SHORT).show()
                return@launch
            }
            val labels = listOf("Использовать глобальный фон") + fileIds.map { "Фон ${it.take(8)}" }
            val current = globalParam.chatBackgroundOverrides[chatId]
            var selected = fileIds.indexOf(current).takeIf { it >= 0 }?.plus(1) ?: 0
            MaterialAlertDialogBuilder(this@UserProfileActivity)
                .setTitle("Фон чата")
                .setSingleChoiceItems(labels.toTypedArray(), selected) { _, which -> selected = which }
                .setNegativeButton("Отмена", null)
                .setPositiveButton("Применить") { _, _ ->
                    lifecycleScope.launch {
                        val fileId = if (selected == 0) "" else fileIds[selected - 1]
                        val result = grpcManager.setChatBackground(chatId, fileId)
                        if (result.isSuccess) {
                            globalParam.setChatBackgroundOverride(chatId, fileId)
                            Toast.makeText(this@UserProfileActivity, "Фон чата обновлён", Toast.LENGTH_SHORT).show()
                        } else {
                            Toast.makeText(this@UserProfileActivity, "Не удалось установить фон", Toast.LENGTH_SHORT).show()
                        }
                    }
                }
                .show()
        }
    }

    private fun updateNotifyIcon() {
        binding.actionNotifyIcon.setImageResource(
            if (isChatMuted) R.drawable.ic_notifications_off else R.drawable.ic_notifications
        )
    }

    private fun toggleChatMute() {
        val newMuted = !isChatMuted
        lifecycleScope.launch {
            val result = grpcManager.setChatMuted(chatId, newMuted)
            if (result.isSuccess) {
                isChatMuted = newMuted
                globalParam.setChatMutedLocal(chatId, newMuted)
                updateNotifyIcon()
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
            if (!ensureCallsClient()) return@launch
            if (otherUserId <= 0L) {
                Toast.makeText(this@UserProfileActivity, "Не удалось определить пользователя для звонка", Toast.LENGTH_SHORT).show()
                return@launch
            }

            val app = application as BarkFluffApplication
            val result = app.callRepository.initiateDirect(otherUserId, CallsApiOuterClass.CallMediaType.CALL_MEDIA_AUDIO)
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
                Toast.makeText(this@UserProfileActivity, "Не удалось начать звонок", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun ensureCallsClient(): Boolean {
        val app = application as BarkFluffApplication
        if (app.grpcManager.callsClient != null) return true

        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) {
            Toast.makeText(this, "Сервер звонков не настроен", Toast.LENGTH_SHORT).show()
            return false
        }

        val result = app.grpcManager.createCallsClient(callsAddress, this, includeDeviceInfo = true)
        if (result.isFailure) {
            Toast.makeText(this, "Не удалось подключиться к серверу звонков", Toast.LENGTH_SHORT).show()
        }
        return result.isSuccess
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
            getFileUrl = { fileId -> chatRepository.getFileDownloadUrl(fileId).getOrNull() },
            onAttachmentClick = { attachmentInfo ->
                val att = attachmentInfo.attachment
                when (att.type) {
                    barkfluff.shared.Shared.MessageAttachmentType.IMAGE,
                    barkfluff.shared.Shared.MessageAttachmentType.GIF -> {
                        val allFileIds = adapter.currentList.map { it.attachment.fileId }
                        val allPreviewUrls = adapter.currentList.map { it.attachment.previewUrl }
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
                                att.fileName.ifBlank { "Видео" },
                                cachedPath
                            )
                        )
                    }
                    else -> {
                        lifecycleScope.launch {
                            try {
                                val file = withContext(Dispatchers.IO) {
                                    FileCache.getFile(att.fileId)
                                        ?: chatRepository.downloadFile(att.fileId)
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
                                    startActivity(Intent.createChooser(intent, "Открыть с помощью"))
                                } else {
                                    Toast.makeText(this@UserProfileActivity, "Не удалось скачать файл", Toast.LENGTH_SHORT).show()
                                }
                            } catch (e: Exception) {
                                Log.e(TAG, "Error opening file", e)
                                Toast.makeText(this@UserProfileActivity, "Ошибка открытия файла", Toast.LENGTH_SHORT).show()
                            }
                        }
                    }
                }
            },
            downloadToCache = { fileId ->
                FileCache.getFile(fileId) ?: chatRepository.downloadFile(fileId)
            },
            scope = lifecycleScope
        )
        recyclerView.adapter = adapter
        return AttachmentsPanel(container, loading, recyclerView, empty, adapter)
    }

    // ── Табы вложений ─────────────────────────────────────────────────────────

    private fun setupTabs() {
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
                val images = chatRepository.getChatAttachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.IMAGE).getOrNull().orEmpty()
                val gifs = chatRepository.getChatAttachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.GIF).getOrNull().orEmpty()
                val videos = chatRepository.getChatAttachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.VIDEO).getOrNull().orEmpty()
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
                val result = chatRepository.getChatAttachments(
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
                val attachments = chatRepository.getChatAttachments(chatId, type).getOrNull()
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
        }
    }

    private fun loadGroupProfile() {
        binding.profileNameTextView.text = chatTitle.trim()
        binding.profileUsernameTextView.visibility = View.GONE
        binding.profileStatusRow.visibility = View.GONE
        binding.profileBioTextView.visibility = View.GONE
        binding.rowUserId.visibility = View.GONE
        binding.dividerUserId.visibility = View.GONE
        binding.rowRegistration.visibility = View.GONE
        binding.dividerChatId.visibility = View.GONE

        if (!chatAvatarFileId.isNullOrBlank()) {
            AvatarLoader.loadByFileId(
                imageView = binding.profileAvatarImageView,
                placeholderView = binding.profileAvatarPlaceholder,
                fileId = chatAvatarFileId!!,
                displayName = chatTitle,
                userId = chatId.hashCode().toLong()
            ) {
                chatRepository.getFileDownloadUrl(chatAvatarFileId!!).getOrNull()
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
                val userResult = chatRepository.getUserData(otherUserId)
                if (userResult.isSuccess) {
                    val user = userResult.getOrNull()!!
                    val displayName = "${user.firstName} ${user.lastName}".trim()

                    binding.profileNameTextView.text = if (displayName.isNotBlank()) displayName else user.username
                    binding.profileUsernameTextView.text = "@${user.username}"
                    binding.profileUsernameTextView.visibility = View.VISIBLE

                    if (user.registrationDate > 0) {
                        val sdf = SimpleDateFormat("d MMMM yyyy", Locale("ru"))
                        binding.profileRegistrationValue.text = sdf.format(Date(user.registrationDate))
                        binding.rowRegistration.visibility = View.VISIBLE
                    } else {
                        binding.rowRegistration.visibility = View.GONE
                        binding.dividerChatId.visibility = View.GONE
                    }

                    if (user.bio.isNotBlank()) {
                        binding.profileBioTextView.text = user.bio
                        binding.profileBioTextView.visibility = View.VISIBLE
                    }

                    val avatarFileId = avatarSourceFor(user)
                    if (!avatarFileId.isNullOrBlank()) {
                        AvatarLoader.loadByFileId(
                            imageView = binding.profileAvatarImageView,
                            placeholderView = binding.profileAvatarPlaceholder,
                            fileId = avatarFileId,
                            displayName = displayName.ifBlank { user.username },
                            userId = otherUserId
                        ) {
                            chatRepository.getFileDownloadUrl(avatarFileId).getOrNull()
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

        // Онлайн-статус
        lifecycleScope.launch {
            try {
                val onlinerClient = grpcManager.onlinerClient
                if (onlinerClient != null) {
                    val request = barkfluff.onliner.OnlinerApiOuterClass.GetOnlineStatusRequest.newBuilder()
                        .addUserIds(otherUserId)
                        .build()
                    val response = onlinerClient.getOnlineStatus(request)
                    val userStatus = response.usersStatusesList.firstOrNull()
                    if (userStatus != null) {
                        val isOnline = userStatus.status.getNumber() ==
                                barkfluff.onliner.OnlinerApiOuterClass.StatusTypeId.STATUS_ONLINE.getNumber()
                        if (isOnline) {
                            binding.profileOnlineStatusTextView.text = "в сети"
                            binding.profileOnlineStatusTextView.setTextColor(
                                ContextCompat.getColor(this@UserProfileActivity, R.color.profile_presence_online)
                            )
                            binding.statusDot.visibility = View.VISIBLE
                            binding.onlineIndicator.visibility = View.VISIBLE
                        } else {
                            binding.profileOnlineStatusTextView.text =
                                OnlineTimeFormatter.formatLastSeen(this@UserProfileActivity, userStatus.lastSeen.seconds * 1000)
                            binding.profileOnlineStatusTextView.setTextColor(
                                MaterialColors.getColor(
                                    binding.root,
                                    com.google.android.material.R.attr.colorOnSurfaceVariant
                                )
                            )
                            binding.statusDot.visibility = View.GONE
                            binding.onlineIndicator.visibility = View.GONE
                        }
                    } else {
                        binding.profileOnlineStatusTextView.text = "был(а) недавно"
                        binding.statusDot.visibility = View.GONE
                        binding.onlineIndicator.visibility = View.GONE
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading online status", e)
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private fun avatarSourceFor(user: GrpcManager.UserData): String? {
        return user.profilePicturePreviewUrl
            .ifBlank { user.profilePictureUrl }
            .ifBlank { user.profilePicturePreviewFileId }
            .ifBlank { user.profilePictureFileId }
            .ifBlank { null }
    }

    private fun loadPosterImage(posterFileId: String) {
        val cachedUrl = AvatarLoader.urlCache[posterFileId]
            ?: AvatarLoader.getUrlFromCache(posterFileId)

        if (cachedUrl != null) {
            binding.profilePosterPlaceholder.visibility = View.GONE
            binding.profilePosterImageView.visibility = View.VISIBLE
            binding.profilePosterImageView.load(cachedUrl, AvatarLoader.getImageLoader(this)) {
                crossfade(true)
            }
            return
        }

        lifecycleScope.launch {
            try {
                val urlResult = chatRepository.getFileDownloadUrl(posterFileId)
                val url = urlResult.getOrNull()
                if (!url.isNullOrBlank()) {
                    AvatarLoader.urlCache[posterFileId] = url
                    AvatarLoader.putUrlInCache(posterFileId, url)

                    binding.profilePosterPlaceholder.visibility = View.GONE
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
        fileSearchJob?.cancel()
        super.onDestroy()
        chatRepository.close()
    }
}
