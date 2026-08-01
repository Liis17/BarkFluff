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
import androidx.core.widget.doAfterTextChanged
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.barkfluff.client.adapter.AttachmentPreviewAdapter
import com.barkfluff.client.adapter.GroupMemberAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityGroupInfoBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.OnlineTimeFormatter
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.color.MaterialColors
import com.yalantis.ucrop.UCrop
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File

/**
 * Экран управления групповым чатом: аватар, название, участники, вложения.
 */
class GroupInfoActivity : AppCompatActivity() {

    private lateinit var binding: ActivityGroupInfoBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatRepository: ChatRepository
    private lateinit var globalParam: GlobalParam
    private lateinit var memberAdapter: GroupMemberAdapter
    private data class AttachmentsPanel(
        val container: View,
        val loading: View,
        val recyclerView: RecyclerView,
        val empty: TextView,
        val adapter: AttachmentPreviewAdapter
    )

    private lateinit var mediaAttachmentsPanel: AttachmentsPanel
    private lateinit var filesAttachmentsPanel: AttachmentsPanel
    private var selectedAttachmentTab: Tab = Tab.MEMBERS
    private var fileSearchJob: Job? = null
    private val attachmentLoadVersions = mutableMapOf<Tab, Int>()

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
        binding.chooseBackgroundButton.setOnClickListener { showChatBackgroundDialog() }
        binding.addMemberButton.setOnClickListener {
            addMemberLauncher.launch(AddGroupMemberActivity.createIntent(this, chatId))
        }

        setupInfoCard()

        setupMembersList()
        setupAttachmentsRecycler()
        setupTabs()
        renderHeader()
        loadMembers()
    }

    private fun showChatBackgroundDialog() {
        lifecycleScope.launch {
            val fileIds = grpcManager.getPersonalization().getOrElse {
                Toast.makeText(this@GroupInfoActivity, "Не удалось загрузить фоны", Toast.LENGTH_SHORT).show()
                return@launch
            }
            val labels = listOf("Использовать глобальный фон") + fileIds.map { "Фон ${it.take(8)}" }
            val current = globalParam.chatBackgroundOverrides[chatId]
            var selected = fileIds.indexOf(current).takeIf { it >= 0 }?.plus(1) ?: 0
            MaterialAlertDialogBuilder(this@GroupInfoActivity)
                .setTitle("Фон чата")
                .setSingleChoiceItems(labels.toTypedArray(), selected) { _, which -> selected = which }
                .setNegativeButton("Отмена", null)
                .setPositiveButton("Применить") { _, _ ->
                    lifecycleScope.launch {
                        val fileId = if (selected == 0) "" else fileIds[selected - 1]
                        val result = grpcManager.setChatBackground(chatId, fileId)
                        if (result.isSuccess) {
                            globalParam.setChatBackgroundOverride(chatId, fileId)
                            Toast.makeText(this@GroupInfoActivity, "Фон чата обновлён", Toast.LENGTH_SHORT).show()
                        } else {
                            Toast.makeText(this@GroupInfoActivity, "Не удалось установить фон", Toast.LENGTH_SHORT).show()
                        }
                    }
                }
                .show()
        }
    }

    private fun setupInfoCard() {
        val showIds = globalParam.showIdsInProfile
        val chatIdCard = binding.rowChatId.parent as View
        chatIdCard.visibility = if (showIds) View.VISIBLE else View.GONE
        if (showIds) {
            binding.groupChatIdValue.text = chatId
            binding.rowChatId.setOnClickListener { copyToClipboard("ChatId", chatId) }
        }
    }

    // ── Табы ──────────────────────────────────────────────────────────────────

    private fun setupTabs() {
        binding.tabMembers.setOnClickListener { selectTab(Tab.MEMBERS) }
        binding.tabMedia.setOnClickListener { selectTab(Tab.MEDIA) }
        binding.tabFiles.setOnClickListener { selectTab(Tab.FILES) }
        selectTab(Tab.MEMBERS)
    }

    private fun selectTab(tab: Tab) {
        selectedAttachmentTab = tab
        styleTab(binding.tabMembers, tab == Tab.MEMBERS)
        styleTab(binding.tabMedia, tab == Tab.MEDIA)
        styleTab(binding.tabFiles, tab == Tab.FILES)

        val membersMode = tab == Tab.MEMBERS
        binding.membersRecyclerView.visibility = if (membersMode) View.VISIBLE else View.GONE
        binding.addMemberButton.visibility = if (membersMode) View.VISIBLE else View.GONE
        binding.attachmentsContainer.visibility = if (membersMode) View.GONE else View.VISIBLE
        mediaAttachmentsPanel.container.visibility = if (tab == Tab.MEDIA) View.VISIBLE else View.GONE
        filesAttachmentsPanel.container.visibility = if (tab == Tab.FILES) View.VISIBLE else View.GONE

        when (tab) {
            Tab.MEMBERS -> Unit
            Tab.MEDIA -> loadMedia()
            Tab.FILES -> loadFiles(binding.fileSearchEditText.text?.toString().orEmpty())
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

    // ── Вложения ──────────────────────────────────────────────────────────────

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
        binding.mediaAttachmentsRecyclerView.layoutManager = GridLayoutManager(this, 3)
        binding.filesAttachmentsRecyclerView.layoutManager = LinearLayoutManager(this)
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
            onAttachmentClick = { info -> openAttachment(info, adapter) },
            downloadToCache = { fileId -> FileCache.getFile(fileId) ?: chatRepository.downloadFile(fileId) },
            scope = lifecycleScope
        )
        recyclerView.adapter = adapter
        return AttachmentsPanel(container, loading, recyclerView, empty, adapter)
    }

    private fun openAttachment(
        info: barkfluff.messages.MessagesApiOuterClass.ChatAttachmentInfo,
        adapter: AttachmentPreviewAdapter
    ) {
        val att = info.attachment
        when (att.type) {
            barkfluff.shared.Shared.MessageAttachmentType.IMAGE,
            barkfluff.shared.Shared.MessageAttachmentType.GIF -> {
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

    private fun panelFor(tab: Tab): AttachmentsPanel = when (tab) {
        Tab.MEDIA -> mediaAttachmentsPanel
        Tab.FILES -> filesAttachmentsPanel
        Tab.MEMBERS -> error("Members tab has no attachments panel")
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
                val attachments = chatRepository.getChatAttachments(
                    chatId,
                    barkfluff.shared.Shared.MessageAttachmentType.DOCUMENT,
                    pageSize = if (query.isBlank()) 100 else 30,
                    fileNameQuery = query
                ).getOrNull()
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

    // ── Участники ─────────────────────────────────────────────────────────────

    private fun setupMembersList() {
        memberAdapter = GroupMemberAdapter(
            getFileUrl = { fileId -> grpcManager.getFileDownloadUrl(fileId).getOrNull() },
            onMemberClick = { member -> openMemberProfile(member) },
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
                    val avatarFileId = grpcManager.getUserData(member.userId).getOrNull()?.let { user ->
                        avatarSourceFor(user)
                    }
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

    private fun avatarSourceFor(user: GrpcManager.UserData): String? {
        return user.profilePicturePreviewUrl
            .ifBlank { user.profilePictureUrl }
            .ifBlank { user.profilePicturePreviewFileId }
            .ifBlank { user.profilePictureFileId }
            .ifBlank { null }
    }

    private fun openMemberProfile(member: GroupMemberAdapter.MemberItem) {
        lifecycleScope.launch {
            val personalChatId = if (member.userId != globalParam.userId) {
                grpcManager.getPersonChatId(member.userId).getOrNull()
            } else {
                null
            }

            startActivity(
                UserProfileActivity.createIntent(
                    this@GroupInfoActivity,
                    chatId = personalChatId ?: chatId,
                    otherUserId = member.userId,
                    isGroupChat = false,
                    chatTitle = member.name,
                    chatAvatarFileId = member.avatarFileId
                )
            )
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
        return if (status.first) "в сети" else OnlineTimeFormatter.formatLastSeen(this, status.second)
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

    override fun onDestroy() {
        fileSearchJob?.cancel()
        super.onDestroy()
        chatRepository.close()
    }
}
