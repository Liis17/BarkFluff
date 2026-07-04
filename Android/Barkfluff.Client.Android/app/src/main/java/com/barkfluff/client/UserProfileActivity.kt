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
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.LinearLayoutManager
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
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.TimeUnit

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

        binding.profileChatIdValue.text = chatId
        binding.rowChatId.setOnClickListener { copyToClipboard("ChatId", chatId) }

        if (showIds) {
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

        binding.actionMessageButton.setOnClickListener { finish() }
        binding.actionCallButton.setOnClickListener { startCall() }

        isChatMuted = chatId in globalParam.mutedChatIds
        updateNotifyIcon()
        binding.actionNotifyButton.setOnClickListener { toggleChatMute() }
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

    private var attachmentAdapter: AttachmentPreviewAdapter? = null

    private fun setupAttachmentsRecycler() {
        attachmentAdapter = AttachmentPreviewAdapter(
            getFileUrl = { fileId -> chatRepository.getFileDownloadUrl(fileId).getOrNull() },
            onAttachmentClick = { attachmentInfo ->
                val att = attachmentInfo.attachment
                when (att.type) {
                    barkfluff.shared.Shared.MessageAttachmentType.IMAGE,
                    barkfluff.shared.Shared.MessageAttachmentType.GIF -> {
                        val adapter = attachmentAdapter
                        if (adapter != null) {
                            val allFileIds = adapter.currentList.map { it.attachment.fileId }
                            val allPreviewUrls = adapter.currentList.map { it.attachment.previewUrl }
                            val position = adapter.currentList.indexOf(attachmentInfo).coerceAtLeast(0)
                            startActivity(
                                ImageViewerActivity.createIntent(
                                    this, allFileIds, allPreviewUrls, position
                                )
                            )
                        }
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
        binding.attachmentsRecyclerView.adapter = attachmentAdapter
    }

    // ── Табы вложений ─────────────────────────────────────────────────────────

    private fun setupTabs() {
        binding.tabMedia.setOnClickListener { selectTab(Tab.MEDIA) }
        binding.tabFiles.setOnClickListener { selectTab(Tab.FILES) }
        binding.tabVoice.setOnClickListener { selectTab(Tab.VOICE) }
        selectTab(Tab.MEDIA)
    }

    private fun selectTab(tab: Tab) {
        styleTab(binding.tabMedia, tab == Tab.MEDIA)
        styleTab(binding.tabFiles, tab == Tab.FILES)
        styleTab(binding.tabVoice, tab == Tab.VOICE)

        when (tab) {
            Tab.MEDIA -> {
                binding.attachmentsRecyclerView.layoutManager = GridLayoutManager(this, 3)
                loadMedia()
            }
            Tab.FILES -> {
                binding.attachmentsRecyclerView.layoutManager = LinearLayoutManager(this)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT)
            }
            Tab.VOICE -> {
                binding.attachmentsRecyclerView.layoutManager = LinearLayoutManager(this)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.AUDIO)
            }
        }
    }

    private fun styleTab(view: TextView, selected: Boolean) {
        if (selected) {
            view.setBackgroundResource(R.drawable.bg_pill_selected)
            view.setTextColor(ContextCompat.getColor(this, R.color.profile_on_accent))
        } else {
            view.background = null
            view.setTextColor(ContextCompat.getColor(this, R.color.profile_chip_text))
        }
    }

    private fun showLoading() {
        binding.attachmentsLoading.visibility = View.VISIBLE
        binding.attachmentsRecyclerView.visibility = View.GONE
        binding.attachmentsEmpty.visibility = View.GONE
    }

    private fun showEmpty(text: String) {
        binding.attachmentsLoading.visibility = View.GONE
        binding.attachmentsRecyclerView.visibility = View.GONE
        binding.attachmentsEmpty.visibility = View.VISIBLE
        binding.attachmentsEmpty.text = text
    }

    private fun showList(items: List<barkfluff.messages.MessagesApiOuterClass.ChatAttachmentInfo>) {
        attachmentAdapter?.submitList(items)
        binding.attachmentsLoading.visibility = View.GONE
        binding.attachmentsEmpty.visibility = View.GONE
        binding.attachmentsRecyclerView.visibility = View.VISIBLE
    }

    /** Медиа = фото + видео, объединённые и отсортированные по дате отправки. */
    private fun loadMedia() {
        showLoading()
        lifecycleScope.launch {
            try {
                val images = chatRepository.getChatAttachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.IMAGE).getOrNull().orEmpty()
                val videos = chatRepository.getChatAttachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.VIDEO).getOrNull().orEmpty()
                val merged = (images + videos).sortedByDescending { it.sentAt.seconds }
                if (merged.isEmpty()) showEmpty(getString(R.string.media_no_attachments)) else showList(merged)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading media", e)
                showEmpty(getString(R.string.media_no_attachments))
            }
        }
    }

    private fun loadAttachments(type: barkfluff.shared.Shared.MessageAttachmentType) {
        showLoading()
        lifecycleScope.launch {
            try {
                val result = chatRepository.getChatAttachments(chatId, type)
                val attachments = result.getOrNull()
                if (attachments == null) {
                    showEmpty(getString(R.string.media_no_attachments))
                } else if (attachments.isEmpty()) {
                    showEmpty(
                        if (type == barkfluff.shared.Shared.MessageAttachmentType.AUDIO)
                            getString(R.string.profile_no_voice)
                        else getString(R.string.media_no_attachments)
                    )
                } else {
                    showList(attachments)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading attachments", e)
                showEmpty(getString(R.string.media_no_attachments))
            }
        }
    }

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
                userId = chatId.hashCode().toLong(),
                size = 240
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

                    val avatarFileId = user.profilePictureFileId
                    if (!avatarFileId.isNullOrBlank()) {
                        AvatarLoader.loadByFileId(
                            imageView = binding.profileAvatarImageView,
                            placeholderView = binding.profileAvatarPlaceholder,
                            fileId = avatarFileId,
                            displayName = displayName.ifBlank { user.username },
                            userId = otherUserId,
                            size = 240
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
                            binding.profileOnlineStatusTextView.text = formatLastSeen(userStatus.lastSeen.seconds * 1000)
                            binding.profileOnlineStatusTextView.setTextColor(
                                ContextCompat.getColor(this@UserProfileActivity, R.color.profile_text_dim)
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

    private fun formatLastSeen(lastSeenMs: Long): String {
        if (lastSeenMs <= 0) return "был(а) давно"
        val now = System.currentTimeMillis()
        val diff = now - lastSeenMs
        return when {
            diff < TimeUnit.MINUTES.toMillis(1) -> "был(а) только что"
            diff < TimeUnit.HOURS.toMillis(1) -> {
                val mins = TimeUnit.MILLISECONDS.toMinutes(diff)
                "был(а) $mins мин. назад"
            }
            diff < TimeUnit.DAYS.toMillis(1) -> {
                val sdf = SimpleDateFormat("HH:mm", Locale.getDefault())
                "был(а) сегодня в ${sdf.format(Date(lastSeenMs))}"
            }
            else -> {
                val sdf = SimpleDateFormat("d MMM", Locale("ru"))
                "был(а) ${sdf.format(Date(lastSeenMs))}"
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
        super.onDestroy()
        chatRepository.close()
    }
}
