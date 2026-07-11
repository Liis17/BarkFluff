package com.barkfluff.client

import android.content.Intent
import android.graphics.drawable.Drawable
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import androidx.fragment.app.Fragment
import androidx.lifecycle.lifecycleScope
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.FragmentProfileBinding
import com.barkfluff.client.grpc.GrpcManager
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.LogoutHelper
import com.barkfluff.client.utils.UpdateChecker
import com.google.android.material.dialog.MaterialAlertDialogBuilder
import com.google.android.material.color.MaterialColors
import kotlinx.coroutines.launch
import java.util.Locale

class ProfileFragment : Fragment() {

    private var _binding: FragmentProfileBinding? = null
    private val binding get() = _binding!!

    private lateinit var globalParam: GlobalParam
    private lateinit var grpcManager: GrpcManager
    private var previousMainBackground: Drawable? = null
    private var mainBackgroundCaptured = false

    companion object {
        private const val TAG = "ProfileFragment"
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = FragmentProfileBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        val app = requireActivity().application as BarkFluffApplication
        globalParam = GlobalParam(requireContext())
        grpcManager = app.grpcManager

        setupClickListeners()
        updateUI()
        checkForUpdates()
    }

    override fun onResume() {
        super.onResume()
        applyProfileSystemBarBackground()
        refreshUserData()
    }

    override fun onPause() {
        restoreMainBackground()
        super.onPause()
    }

    override fun onHiddenChanged(hidden: Boolean) {
        super.onHiddenChanged(hidden)
        if (hidden) restoreMainBackground() else applyProfileSystemBarBackground()
    }

    private fun setupClickListeners() {
        binding.itemAccount.setOnClickListener {
            startActivity(Intent(requireContext(), AccountSettingsActivity::class.java))
        }
        binding.itemSecurity.setOnClickListener {
            startActivity(Intent(requireContext(), SecuritySettingsActivity::class.java))
        }
        binding.itemPrivacy.setOnClickListener {
            startActivity(Intent(requireContext(), PrivacySettingsActivity::class.java))
        }
        binding.itemPersonalization.setOnClickListener {
            startActivity(Intent(requireContext(), PersonalizationSettingsActivity::class.java))
        }
        binding.itemWidgets.setOnClickListener {
            startActivity(Intent(requireContext(), WidgetsSettingsActivity::class.java))
        }
        binding.itemChatFolders.setOnClickListener {
            startActivity(Intent(requireContext(), ChatFoldersSettingsActivity::class.java))
        }
        binding.itemStorage.setOnClickListener {
            startActivity(Intent(requireContext(), StorageSettingsActivity::class.java))
        }
        binding.itemDevices.setOnClickListener {
            startActivity(Intent(requireContext(), DevicesActivity::class.java))
        }
        binding.itemNotifications.setOnClickListener {
            startActivity(Intent(requireContext(), NotificationSettingsActivity::class.java))
        }
        binding.itemLanguage.setOnClickListener {
            startActivity(Intent(requireContext(), LanguageSettingsActivity::class.java))
        }
        binding.itemUpdate.setOnClickListener {
            startActivity(Intent(requireContext(), UpdateActivity::class.java))
        }
        binding.itemAbout.setOnClickListener {
            startActivity(Intent(requireContext(), AboutActivity::class.java))
        }
        binding.itemTesting.setOnClickListener {
            startActivity(Intent(requireContext(), TestingSettingsActivity::class.java))
        }
        binding.buttonLogout.setOnClickListener {
            MaterialAlertDialogBuilder(requireContext())
                .setTitle(R.string.logout_dialog_title)
                .setMessage(R.string.logout_dialog_message)
                .setPositiveButton(R.string.logout_dialog_confirm) { _, _ ->
                    lifecycleScope.launch {
                        LogoutHelper.performFullLogout(requireContext(), grpcManager)
                    }
                }
                .setNegativeButton(R.string.btn_cancel, null)
                .show()
        }
    }

    private fun updateUI() {
        val fullName = "${globalParam.firstName} ${globalParam.lastName}".trim()
        binding.textFullName.text = fullName.ifEmpty { getString(R.string.profile_user_placeholder) }
        binding.textUsername.text = if (globalParam.userName.isNotEmpty()) "@${globalParam.userName}" else ""
        binding.textLanguageValue.text = getLanguageDisplayName()

        loadAvatar()
    }

    private fun loadAvatar() {
        val fileId = globalParam.pictureFileId
        val displayName = "${globalParam.firstName} ${globalParam.lastName}".trim()

        Log.d(TAG, "loadAvatar: displayName='$displayName', userId=${globalParam.userId}")
        Log.d(TAG, "loadAvatar: profilePictureUrl='${globalParam.profilePictureUrl}'")
        Log.d(TAG, "loadAvatar: pictureFileId='$fileId'")

        val urlToUse = globalParam.profilePictureUrl

        // Основной аватар (tab Profile)
        if (urlToUse.isNotBlank()) {
            binding.avatarPlaceholderIcon.visibility = View.GONE
            AvatarLoader.load(
                imageView = binding.avatarImage,
                placeholderView = binding.avatarPlaceholder,
                avatarUrl = urlToUse,
                displayName = displayName,
                userId = globalParam.userId,
                circleCrop = false
            )
        } else if (fileId.isNotEmpty()) {
            binding.avatarPlaceholderIcon.visibility = View.GONE
            AvatarLoader.loadByFileId(
                binding.avatarImage,
                binding.avatarPlaceholder,
                fileId,
                displayName,
                globalParam.userId,
                size = 192,
                circleCrop = false
            ) {
                val result = grpcManager.getFileDownloadUrl(fileId)
                if (result.isSuccess) result.getOrNull() else null
            }
        } else {
            binding.avatarPlaceholder.visibility = View.GONE
            binding.avatarPlaceholderIcon.visibility = View.VISIBLE
            binding.avatarImage.visibility = View.GONE
        }
    }

    private fun getLanguageDisplayName(): String {
        val locale = when (globalParam.appLanguage) {
            GlobalParam.LANGUAGE_RU -> Locale("ru")
            GlobalParam.LANGUAGE_EN -> Locale.ENGLISH
            GlobalParam.LANGUAGE_DE -> Locale.GERMAN
            GlobalParam.LANGUAGE_ES -> Locale("es")
            GlobalParam.LANGUAGE_ZH -> Locale.SIMPLIFIED_CHINESE
            else -> Locale.getDefault()
        }
        val displayName = locale.getDisplayLanguage(locale)
        return displayName.replaceFirstChar { character ->
            if (character.isLowerCase()) character.titlecase(locale) else character.toString()
        }
    }

    private fun applyProfileSystemBarBackground() {
        val mainRoot = activity?.findViewById<View>(R.id.mainRoot) ?: return
        if (!mainBackgroundCaptured) {
            previousMainBackground = mainRoot.background
            mainBackgroundCaptured = true
        }
        mainRoot.setBackgroundColor(
            MaterialColors.getColor(
                mainRoot,
                com.google.android.material.R.attr.colorSurfaceContainer
            )
        )
    }

    private fun restoreMainBackground() {
        if (!mainBackgroundCaptured) return
        activity?.findViewById<View>(R.id.mainRoot)?.background = previousMainBackground
        previousMainBackground = null
        mainBackgroundCaptured = false
    }

    private fun checkForUpdates() {
        viewLifecycleOwner.lifecycleScope.launch {
            try {
                val currentVersion = GlobalParam.getAppVersion(requireContext())
                val hasUpdate = UpdateChecker.hasUpdate(currentVersion)
                if (_binding != null) {
                    binding.badgeUpdate.visibility = if (hasUpdate) View.VISIBLE else View.GONE
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error checking for updates", e)
            }
        }
    }

    private fun refreshUserData() {
        viewLifecycleOwner.lifecycleScope.launch {
            try {
                val result = grpcManager.getCurrentUserData()
                if (result.isSuccess) {
                    val userData = result.getOrNull() ?: return@launch
                    globalParam.userName = userData.username
                    globalParam.firstName = userData.firstName
                    globalParam.lastName = userData.lastName
                    globalParam.description = userData.bio
                    globalParam.pictureFileId = userData.profilePictureFileId
                    globalParam.picturePreviewFileId = userData.profilePicturePreviewFileId
                    globalParam.picturePreviewUrl = userData.profilePicturePreviewUrl
                    globalParam.profilePictureUrl = userData.profilePictureUrl

                    if (_binding != null) {
                        updateUI()
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Ошибка обновления данных профиля", e)
            }
        }
    }

    override fun onDestroyView() {
        restoreMainBackground()
        super.onDestroyView()
        _binding = null
    }
}
