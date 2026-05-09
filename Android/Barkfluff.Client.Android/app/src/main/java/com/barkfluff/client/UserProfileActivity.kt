package com.barkfluff.client

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.AttachmentPreviewAdapter
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

    private var chatId: String = ""
    private var otherUserId: Long = 0L
    private var isGroupChat: Boolean = false
    private var chatTitle: String = ""
    private var chatAvatarFileId: String? = null

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

        chatId = intent.getStringExtra(EXTRA_CHAT_ID) ?: run { finish(); return }
        otherUserId = intent.getLongExtra(EXTRA_OTHER_USER_ID, 0L)
        isGroupChat = intent.getBooleanExtra(EXTRA_IS_GROUP_CHAT, false)
        chatTitle = intent.getStringExtra(EXTRA_CHAT_TITLE) ?: ""
        chatAvatarFileId = intent.getStringExtra(EXTRA_CHAT_AVATAR_FILE_ID)

        binding.backButton.setOnClickListener { finish() }

        setupIdsBlock()
        setupAttachmentsRecycler()
        loadProfileData()
        loadAttachmentsDefault()
    }

    private fun setupIdsBlock() {
        val globalParam = GlobalParam(this)
        if (!globalParam.showIdsInProfile) {
            binding.profileIdsBlock.visibility = View.GONE
            return
        }
        binding.profileIdsBlock.visibility = View.VISIBLE
        binding.profileUserIdText.text = "UserId: $otherUserId"
        binding.profileChatIdText.text = "ChatId: $chatId"
        binding.profileUserIdText.setOnClickListener {
            copyToClipboard("UserId", otherUserId.toString())
        }
        binding.profileChatIdText.setOnClickListener {
            copyToClipboard("ChatId", chatId)
        }
    }

    private fun copyToClipboard(label: String, text: String) {
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText(label, text))
        Toast.makeText(this, "$label скопирован", Toast.LENGTH_SHORT).show()
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
            }
        )
        binding.attachmentsRecyclerView.adapter = attachmentAdapter
    }

    private fun loadAttachmentsDefault() {
        binding.chipPhotos.isChecked = true
        binding.attachmentsRecyclerView.layoutManager = GridLayoutManager(this, 3)
        loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.IMAGE)

        binding.chipPhotos.setOnClickListener {
            if (binding.chipPhotos.isChecked) {
                binding.attachmentsRecyclerView.layoutManager = GridLayoutManager(this, 3)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.IMAGE)
            } else {
                binding.attachmentsContainer.visibility = View.GONE
            }
        }
        binding.chipVideos.setOnClickListener {
            if (binding.chipVideos.isChecked) {
                binding.attachmentsRecyclerView.layoutManager = GridLayoutManager(this, 3)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.VIDEO)
            } else {
                binding.attachmentsContainer.visibility = View.GONE
            }
        }
        binding.chipFiles.setOnClickListener {
            if (binding.chipFiles.isChecked) {
                binding.attachmentsRecyclerView.layoutManager = LinearLayoutManager(this)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT)
            } else {
                binding.attachmentsContainer.visibility = View.GONE
            }
        }
    }

    private fun loadAttachments(type: barkfluff.shared.Shared.MessageAttachmentType) {
        binding.attachmentsContainer.visibility = View.VISIBLE
        binding.attachmentsLoading.visibility = View.VISIBLE
        binding.attachmentsRecyclerView.visibility = View.GONE
        binding.attachmentsEmpty.visibility = View.GONE

        lifecycleScope.launch {
            try {
                val result = chatRepository.getChatAttachments(chatId, type)
                if (result.isSuccess) {
                    val attachments = result.getOrNull()!!
                    if (attachments.isEmpty()) {
                        binding.attachmentsLoading.visibility = View.GONE
                        binding.attachmentsEmpty.visibility = View.VISIBLE
                        binding.attachmentsEmpty.text = when (type) {
                            barkfluff.shared.Shared.MessageAttachmentType.IMAGE -> "Нет фото"
                            barkfluff.shared.Shared.MessageAttachmentType.VIDEO -> "Нет видео"
                            else -> "Нет файлов"
                        }
                    } else {
                        attachmentAdapter?.submitList(attachments)
                        binding.attachmentsLoading.visibility = View.GONE
                        binding.attachmentsRecyclerView.visibility = View.VISIBLE
                    }
                } else {
                    binding.attachmentsLoading.visibility = View.GONE
                    binding.attachmentsEmpty.visibility = View.VISIBLE
                    binding.attachmentsEmpty.text = "Ошибка загрузки"
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading attachments", e)
                binding.attachmentsLoading.visibility = View.GONE
                binding.attachmentsEmpty.visibility = View.VISIBLE
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
        binding.profileOnlineStatusTextView.visibility = View.GONE
        binding.profileBioTextView.visibility = View.GONE

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

                    if (user.bio.isNotBlank()) {
                        binding.profileBioTextView.text = user.bio
                        binding.profileBioTextView.visibility = View.VISIBLE
                    }

                    // Аватар — через AvatarLoader с кэшированием URL
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

                    // Постер — кэшируем URL через AvatarLoader.urlCache
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
                                ContextCompat.getColor(this@UserProfileActivity, R.color.primary)
                            )
                            binding.onlineIndicator.visibility = View.VISIBLE
                        } else {
                            binding.profileOnlineStatusTextView.text = formatLastSeen(userStatus.lastSeen.seconds * 1000)
                            binding.profileOnlineStatusTextView.setTextColor(
                                ContextCompat.getColor(this@UserProfileActivity, R.color.on_surface_variant)
                            )
                            binding.onlineIndicator.visibility = View.GONE
                        }
                    } else {
                        binding.profileOnlineStatusTextView.text = "был(а) недавно"
                        binding.onlineIndicator.visibility = View.GONE
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error loading online status", e)
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /**
     * Загружает постер профиля с кэшированием URL в AvatarLoader.urlCache.
     * Благодаря кэшу повторные открытия экрана не делают gRPC-запрос за URL.
     */
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
                    // Сохраняем в оба кэша
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
