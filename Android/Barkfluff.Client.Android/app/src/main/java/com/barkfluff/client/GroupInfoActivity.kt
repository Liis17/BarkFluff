package com.barkfluff.client

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.EditText
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.AttachmentPreviewAdapter
import com.barkfluff.client.adapter.GroupMemberAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityGroupInfoBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.yalantis.ucrop.UCrop
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.TimeUnit

/**
 * Экран управления групповым чатом: аватар, название, участники, вложения.
 */
class GroupInfoActivity : AppCompatActivity() {

    private lateinit var binding: ActivityGroupInfoBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatRepository: ChatRepository
    private lateinit var globalParam: GlobalParam
    private lateinit var memberAdapter: GroupMemberAdapter
    private var attachmentAdapter: AttachmentPreviewAdapter? = null

    private var chatId: String = ""
    private var chatTitle: String = ""
    private var chatAvatarFileId: String? = null

    private enum class Tab { MEMBERS, MEDIA, FILES }

    companion object {
        private const val TAG = "GroupInfoActivity"
        private const val EXTRA_CHAT_ID = "chat_id"
        private const val EXTRA_CHAT_TITLE = "chat_title"
        private const val EXTRA_CHAT_AVATAR_FILE_ID = "chat_avatar_file_id"

        fun createIntent(
            context: Context,
            chatId: String,
            chatTitle: String,
            chatAvatarFileId: String?
        ): Intent {
            return Intent(context, GroupInfoActivity::class.java).apply {
                putExtra(EXTRA_CHAT_ID, chatId)
                putExtra(EXTRA_CHAT_TITLE, chatTitle)
                putExtra(EXTRA_CHAT_AVATAR_FILE_ID, chatAvatarFileId)
            }
        }
    }

    private val pickMedia = registerForActivityResult(ActivityResultContracts.PickVisualMedia()) { uri ->
        if (uri != null) startUCrop(uri)
    }

    private val ucropLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK) {
            val uri = UCrop.getOutput(result.data!!)
            if (uri != null) uploadAndSetAvatar(uri)
        } else if (result.resultCode == UCrop.RESULT_ERROR) {
            val error = UCrop.getError(result.data!!)
            Toast.makeText(this, "Ошибка: ${error?.message}", Toast.LENGTH_SHORT).show()
        }
    }

    private val addMemberLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK) loadMembers()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityGroupInfoBinding.inflate(layoutInflater)
        setContentView(binding.root)

        val app = application as BarkFluffApplication
        grpcManager = app.grpcManager
        chatRepository = ChatRepository(this, grpcManager)
        globalParam = GlobalParam(this)

        chatId = intent.getStringExtra(EXTRA_CHAT_ID) ?: run { finish(); return }
        chatTitle = intent.getStringExtra(EXTRA_CHAT_TITLE) ?: ""
        chatAvatarFileId = intent.getStringExtra(EXTRA_CHAT_AVATAR_FILE_ID)

        binding.backButton.setOnClickListener { finish() }
        binding.changeAvatarButton.setOnClickListener {
            pickMedia.launch(PickVisualMediaRequest(ActivityResultContracts.PickVisualMedia.ImageOnly))
        }
        binding.changeNameButton.setOnClickListener { showChangeNameDialog() }
        binding.addMemberButton.setOnClickListener {
            addMemberLauncher.launch(AddGroupMemberActivity.createIntent(this, chatId))
        }

        binding.groupChatIdValue.text = chatId
        binding.rowChatId.setOnClickListener { copyToClipboard("ChatId", chatId) }

        setupMembersList()
        setupAttachmentsRecycler()
        setupTabs()
        renderHeader()
        loadMembers()
    }

    // ── Табы ──────────────────────────────────────────────────────────────────

    private fun setupTabs() {
        binding.tabMembers.setOnClickListener { selectTab(Tab.MEMBERS) }
        binding.tabMedia.setOnClickListener { selectTab(Tab.MEDIA) }
        binding.tabFiles.setOnClickListener { selectTab(Tab.FILES) }
        selectTab(Tab.MEMBERS)
    }

    private fun selectTab(tab: Tab) {
        styleTab(binding.tabMembers, tab == Tab.MEMBERS)
        styleTab(binding.tabMedia, tab == Tab.MEDIA)
        styleTab(binding.tabFiles, tab == Tab.FILES)

        val membersMode = tab == Tab.MEMBERS
        binding.membersRecyclerView.visibility = if (membersMode) View.VISIBLE else View.GONE
        binding.addMemberButton.visibility = if (membersMode) View.VISIBLE else View.GONE
        binding.attachmentsContainer.visibility = if (membersMode) View.GONE else View.VISIBLE

        when (tab) {
            Tab.MEMBERS -> Unit
            Tab.MEDIA -> {
                binding.attachmentsRecyclerView.layoutManager = GridLayoutManager(this, 3)
                loadMedia()
            }
            Tab.FILES -> {
                binding.attachmentsRecyclerView.layoutManager = LinearLayoutManager(this)
                loadAttachments(barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT)
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

    // ── Вложения ──────────────────────────────────────────────────────────────

    private fun setupAttachmentsRecycler() {
        attachmentAdapter = AttachmentPreviewAdapter(
            getFileUrl = { fileId -> chatRepository.getFileDownloadUrl(fileId).getOrNull() },
            onAttachmentClick = { info -> openAttachment(info) },
            downloadToCache = { fileId -> FileCache.getFile(fileId) ?: chatRepository.downloadFile(fileId) },
            scope = lifecycleScope
        )
        binding.attachmentsRecyclerView.adapter = attachmentAdapter
    }

    private fun openAttachment(info: barkfluff.messages.MessagesApiOuterClass.ChatAttachmentInfo) {
        val att = info.attachment
        when (att.type) {
            barkfluff.shared.Shared.MessageAttachmentType.IMAGE,
            barkfluff.shared.Shared.MessageAttachmentType.GIF -> {
                val adapter = attachmentAdapter ?: return
                val allFileIds = adapter.currentList.map { it.attachment.fileId }
                val allPreviewUrls = adapter.currentList.map { it.attachment.previewUrl }
                val position = adapter.currentList.indexOf(info).coerceAtLeast(0)
                startActivity(ImageViewerActivity.createIntent(this, allFileIds, allPreviewUrls, position))
            }
            barkfluff.shared.Shared.MessageAttachmentType.VIDEO -> {
                val cachedPath = FileCache.getFile(att.fileId)?.absolutePath
                startActivity(
                    MediaViewerActivity.createIntent(this, att.fileId, att.fileName.ifBlank { "Видео" }, cachedPath)
                )
            }
            else -> {
                lifecycleScope.launch {
                    try {
                        val file = withContext(Dispatchers.IO) {
                            FileCache.getFile(att.fileId) ?: chatRepository.downloadFile(att.fileId)
                        }
                        if (file != null) {
                            val uri = androidx.core.content.FileProvider.getUriForFile(
                                this@GroupInfoActivity, "${packageName}.fileprovider", file
                            )
                            val intent = Intent(Intent.ACTION_VIEW).apply {
                                setDataAndType(uri, "*/*")
                                addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                            }
                            startActivity(Intent.createChooser(intent, "Открыть с помощью"))
                        }
                    } catch (e: Exception) {
                        Log.e(TAG, "Error opening file", e)
                    }
                }
            }
        }
    }

    private fun showLoading() {
        binding.attachmentsLoading.visibility = View.VISIBLE
        binding.attachmentsRecyclerView.visibility = View.GONE
        binding.attachmentsEmpty.visibility = View.GONE
    }

    private fun showEmpty() {
        binding.attachmentsLoading.visibility = View.GONE
        binding.attachmentsRecyclerView.visibility = View.GONE
        binding.attachmentsEmpty.visibility = View.VISIBLE
        binding.attachmentsEmpty.text = getString(R.string.media_no_attachments)
    }

    private fun showList(items: List<barkfluff.messages.MessagesApiOuterClass.ChatAttachmentInfo>) {
        attachmentAdapter?.submitList(items)
        binding.attachmentsLoading.visibility = View.GONE
        binding.attachmentsEmpty.visibility = View.GONE
        binding.attachmentsRecyclerView.visibility = View.VISIBLE
    }

    private fun loadMedia() {
        showLoading()
        lifecycleScope.launch {
            try {
                val images = chatRepository.getChatAttachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.IMAGE).getOrNull().orEmpty()
                val videos = chatRepository.getChatAttachments(chatId, barkfluff.shared.Shared.MessageAttachmentType.VIDEO).getOrNull().orEmpty()
                val merged = (images + videos).sortedByDescending { it.sentAt.seconds }
                if (merged.isEmpty()) showEmpty() else showList(merged)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading media", e)
                showEmpty()
            }
        }
    }

    private fun loadAttachments(type: barkfluff.shared.Shared.MessageAttachmentType) {
        showLoading()
        lifecycleScope.launch {
            try {
                val attachments = chatRepository.getChatAttachments(chatId, type).getOrNull()
                if (attachments.isNullOrEmpty()) showEmpty() else showList(attachments)
            } catch (e: Exception) {
                Log.e(TAG, "Error loading attachments", e)
                showEmpty()
            }
        }
    }

    // ── Участники ─────────────────────────────────────────────────────────────

    private fun setupMembersList() {
        memberAdapter = GroupMemberAdapter(
            getFileUrl = { fileId -> grpcManager.getFileDownloadUrl(fileId).getOrNull() },
            onRemove = { member -> confirmRemove(member) }
        )
        binding.membersRecyclerView.apply {
            layoutManager = LinearLayoutManager(this@GroupInfoActivity)
            adapter = memberAdapter
        }
    }

    private fun renderHeader() {
        binding.groupName.text = chatTitle
        AvatarLoader.loadByFileId(
            imageView = binding.groupAvatar,
            placeholderView = binding.groupAvatarPlaceholder,
            fileId = chatAvatarFileId,
            displayName = chatTitle,
            userId = 0
        ) { chatAvatarFileId?.let { grpcManager.getFileDownloadUrl(it).getOrNull() } }
    }

    private fun loadMembers() {
        lifecycleScope.launch {
            val result = grpcManager.listChatMembers(chatId)
            if (result.isFailure) {
                Toast.makeText(this@GroupInfoActivity, "Ошибка загрузки участников", Toast.LENGTH_SHORT).show()
                return@launch
            }

            val members = result.getOrNull() ?: emptyList()
            val currentUserId = globalParam.userId

            // Онлайн-статусы участников (batch).
            val statuses = loadOnlineStatuses(members.map { it.userId })

            val items = members.map { member ->
                async(Dispatchers.IO) {
                    val name = "${member.firstName} ${member.lastName}".trim()
                        .ifBlank { "ID ${member.userId}" }
                    val avatarFileId = grpcManager.getUserData(member.userId).getOrNull()?.let { u ->
                        u.profilePicturePreviewFileId.ifBlank { u.profilePictureFileId }
                    }?.ifBlank { null }
                    val status = statuses[member.userId]
                    GroupMemberAdapter.MemberItem(
                        userId = member.userId,
                        name = name,
                        avatarFileId = avatarFileId,
                        canRemove = member.userId != currentUserId,
                        online = status?.first == true,
                        subtitle = memberSubtitle(status)
                    )
                }
            }.awaitAll()

            memberAdapter.submitList(items)

            val onlineCount = items.count { it.online }
            binding.groupSubtitle.text = getString(R.string.group_online_count, items.size, onlineCount)
        }
    }

    /** Возвращает карту userId -> (isOnline, lastSeenMs). */
    private suspend fun loadOnlineStatuses(userIds: List<Long>): Map<Long, Pair<Boolean, Long>> {
        if (userIds.isEmpty()) return emptyMap()
        return try {
            val onlinerClient = grpcManager.onlinerClient ?: return emptyMap()
            val request = barkfluff.onliner.OnlinerApiOuterClass.GetOnlineStatusRequest.newBuilder()
                .addAllUserIds(userIds)
                .build()
            val response = onlinerClient.getOnlineStatus(request)
            response.usersStatusesList.associate { st ->
                val online = st.status.getNumber() ==
                        barkfluff.onliner.OnlinerApiOuterClass.StatusTypeId.STATUS_ONLINE.getNumber()
                st.userId to (online to st.lastSeen.seconds * 1000)
            }
        } catch (e: Exception) {
            Log.e(TAG, "Error loading online statuses", e)
            emptyMap()
        }
    }

    private fun memberSubtitle(status: Pair<Boolean, Long>?): String {
        if (status == null) return ""
        return if (status.first) "в сети" else formatLastSeen(status.second)
    }

    private fun confirmRemove(member: GroupMemberAdapter.MemberItem) {
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.group_remove_member)
            .setMessage("Удалить ${member.name} из группы?")
            .setPositiveButton("Удалить") { _, _ ->
                lifecycleScope.launch {
                    val result = grpcManager.kickUser(chatId, member.userId)
                    if (result.isSuccess) {
                        loadMembers()
                    } else {
                        Toast.makeText(this@GroupInfoActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                    }
                }
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private fun showChangeNameDialog() {
        val editText = EditText(this).apply {
            setText(chatTitle)
            setSelection(text.length)
        }
        MaterialAlertDialogBuilder(this)
            .setTitle(R.string.group_change_name)
            .setView(editText)
            .setPositiveButton("Сохранить") { _, _ ->
                val newName = editText.text.toString().trim()
                if (newName.isEmpty()) {
                    Toast.makeText(this, "Название не может быть пустым", Toast.LENGTH_SHORT).show()
                    return@setPositiveButton
                }
                lifecycleScope.launch {
                    val result = grpcManager.updateGroupChat(chatId, title = newName)
                    if (result.isSuccess) {
                        chatTitle = newName
                        binding.groupName.text = newName
                    } else {
                        Toast.makeText(this@GroupInfoActivity, "Ошибка: ${result.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                    }
                }
            }
            .setNegativeButton("Отмена", null)
            .show()
    }

    private fun startUCrop(uri: Uri) {
        val destinationUri = Uri.fromFile(File(cacheDir, "cropped_group_avatar_${System.currentTimeMillis()}.jpg"))
        val options = UCrop.Options().apply {
            setCompressionFormat(Bitmap.CompressFormat.JPEG)
            setCompressionQuality(80)
            withAspectRatio(1f, 1f)
            withMaxResultSize(512, 512)
        }
        val uCrop = UCrop.of(uri, destinationUri)
            .withAspectRatio(1f, 1f)
            .withMaxResultSize(512, 512)
            .withOptions(options)
        ucropLauncher.launch(uCrop.getIntent(this))
    }

    private fun uploadAndSetAvatar(uri: Uri) {
        lifecycleScope.launch {
            try {
                val bytes = withContext(Dispatchers.IO) {
                    val inputStream = contentResolver.openInputStream(uri)
                    val bitmap = BitmapFactory.decodeStream(inputStream)
                    inputStream?.close()
                    val outputStream = ByteArrayOutputStream()
                    bitmap.compress(Bitmap.CompressFormat.JPEG, 80, outputStream)
                    outputStream.toByteArray()
                }

                val uploadResult = chatRepository.uploadFile(
                    bytes,
                    barkfluff.files.FilesApiOuterClass.UploadFileType.CHAT_PICTURE
                )
                if (uploadResult.isFailure) {
                    Toast.makeText(this@GroupInfoActivity, "Ошибка загрузки: ${uploadResult.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                    return@launch
                }

                val fileId = uploadResult.getOrNull()!!
                val updateResult = grpcManager.updateGroupChat(chatId, pictureFileId = fileId)
                if (updateResult.isSuccess) {
                    chatAvatarFileId = updateResult.getOrNull()?.pictureFileId?.ifBlank { fileId } ?: fileId
                    renderHeader()
                    Toast.makeText(this@GroupInfoActivity, "Аватар обновлён", Toast.LENGTH_SHORT).show()
                } else {
                    Toast.makeText(this@GroupInfoActivity, "Ошибка: ${updateResult.exceptionOrNull()?.message}", Toast.LENGTH_SHORT).show()
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка обновления аватара группы", e)
                Toast.makeText(this@GroupInfoActivity, "Ошибка обновления аватара", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun copyToClipboard(label: String, text: String) {
        val cm = getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager
        cm.setPrimaryClip(ClipData.newPlainText(label, text))
        Toast.makeText(this, getString(R.string.profile_copied), Toast.LENGTH_SHORT).show()
    }

    private fun formatLastSeen(lastSeenMs: Long): String {
        if (lastSeenMs <= 0) return "был(а) давно"
        val now = System.currentTimeMillis()
        val diff = now - lastSeenMs
        return when {
            diff < TimeUnit.MINUTES.toMillis(1) -> "был(а) только что"
            diff < TimeUnit.HOURS.toMillis(1) -> "был(а) ${TimeUnit.MILLISECONDS.toMinutes(diff)} мин. назад"
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

    override fun onDestroy() {
        super.onDestroy()
        chatRepository.close()
    }
}
