package com.barkfluff.client

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.EditText
import android.widget.Toast
import androidx.activity.result.PickVisualMediaRequest
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.adapter.GroupMemberAdapter
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityGroupInfoBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.yalantis.ucrop.UCrop
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.io.File

/**
 * Экран управления групповым чатом: аватар, название, участники.
 */
class GroupInfoActivity : AppCompatActivity() {

    private lateinit var binding: ActivityGroupInfoBinding
    private lateinit var grpcManager: GrpcManager
    private lateinit var chatRepository: ChatRepository
    private lateinit var globalParam: GlobalParam
    private lateinit var memberAdapter: GroupMemberAdapter

    private var chatId: String = ""
    private var chatTitle: String = ""
    private var chatAvatarFileId: String? = null

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

        setupMembersList()
        renderHeader()
        loadMembers()
    }

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

            // Резолвим аватары участников параллельно через getUserData.
            val items = members.map { member ->
                async(Dispatchers.IO) {
                    val name = "${member.firstName} ${member.lastName}".trim()
                        .ifBlank { "ID ${member.userId}" }
                    val avatarFileId = grpcManager.getUserData(member.userId).getOrNull()?.let { u ->
                        u.profilePicturePreviewFileId.ifBlank { u.profilePictureFileId }
                    }?.ifBlank { null }
                    GroupMemberAdapter.MemberItem(
                        userId = member.userId,
                        name = name,
                        avatarFileId = avatarFileId,
                        canRemove = member.userId != currentUserId
                    )
                }
            }.awaitAll()

            memberAdapter.submitList(items)
        }
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
}
