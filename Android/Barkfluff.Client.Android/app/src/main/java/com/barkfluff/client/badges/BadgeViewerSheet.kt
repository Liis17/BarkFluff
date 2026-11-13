package com.barkfluff.client.badges

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.view.animation.DecelerateInterpolator
import com.barkfluff.client.R
import com.barkfluff.client.databinding.DialogBadgeViewerBinding
import com.barkfluff.client.domain.model.UserBadgeInfo
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import java.text.DateFormat
import java.util.Date

class BadgeViewerSheet : BottomSheetDialogFragment() {

    private var _binding: DialogBadgeViewerBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?,
    ): View {
        _binding = DialogBadgeViewerBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        val args = requireArguments()
        val badge = args.readBadge()

        binding.badgeImage.contentDescription = badge.name
        BadgeImageLoader.load(binding.badgeImage, badge.imageUrl)
        binding.badgeName.text = badge.name
        binding.badgeDescription.text = badge.description
        binding.badgeDescription.visibility = if (badge.description.isBlank()) View.GONE else View.VISIBLE

        if (badge.assignedAtMillis > 0L) {
            val date = DateFormat.getDateInstance(DateFormat.LONG, resources.configuration.locales[0])
                .format(Date(badge.assignedAtMillis))
            binding.badgeReceived.text = getString(R.string.badge_received_date, date)
            binding.badgeReceived.visibility = View.VISIBLE
        } else {
            binding.badgeReceived.visibility = View.GONE
        }

        val canChangePrimary = args.getBoolean(ARG_CAN_CHANGE_PRIMARY)
        binding.primaryAction.visibility = if (canChangePrimary) View.VISIBLE else View.GONE
        if (canChangePrimary) {
            binding.primaryAction.setText(
                if (badge.isPrimary) R.string.badge_action_remove_primary else R.string.badge_action_set_primary
            )
            binding.primaryAction.setOnClickListener {
                parentFragmentManager.setFragmentResult(
                    RESULT_KEY,
                    Bundle().apply {
                        putInt(RESULT_BADGE_ID, if (badge.isPrimary) 0 else badge.badgeId)
                    },
                )
                dismiss()
            }
        }

        binding.badgeImage.apply {
            scaleX = 0.76f
            scaleY = 0.76f
            alpha = 0f
            animate()
                .scaleX(1f)
                .scaleY(1f)
                .alpha(1f)
                .setDuration(240L)
                .setInterpolator(DecelerateInterpolator(1.5f))
                .start()
        }
    }

    override fun onDestroyView() {
        _binding = null
        super.onDestroyView()
    }

    companion object {
        const val RESULT_KEY = "badge-primary-change"
        const val RESULT_BADGE_ID = "badge-id"

        private const val ARG_ID = "badge-id"
        private const val ARG_NAME = "badge-name"
        private const val ARG_DESCRIPTION = "badge-description"
        private const val ARG_IMAGE_URL = "badge-image-url"
        private const val ARG_MINI_IMAGE_URL = "badge-mini-image-url"
        private const val ARG_ASSIGNED_AT = "badge-assigned-at"
        private const val ARG_IS_PRIMARY = "badge-is-primary"
        private const val ARG_CAN_CHANGE_PRIMARY = "can-change-primary"

        fun newInstance(badge: UserBadgeInfo, canChangePrimary: Boolean = false) = BadgeViewerSheet().apply {
            arguments = Bundle().apply {
                putInt(ARG_ID, badge.badgeId)
                putString(ARG_NAME, badge.name)
                putString(ARG_DESCRIPTION, badge.description)
                putString(ARG_IMAGE_URL, badge.imageUrl)
                putString(ARG_MINI_IMAGE_URL, badge.miniImageUrl)
                putLong(ARG_ASSIGNED_AT, badge.assignedAtMillis)
                putBoolean(ARG_IS_PRIMARY, badge.isPrimary)
                putBoolean(ARG_CAN_CHANGE_PRIMARY, canChangePrimary)
            }
        }

        private fun Bundle.readBadge() = UserBadgeInfo(
            badgeId = getInt(ARG_ID),
            name = getString(ARG_NAME).orEmpty(),
            description = getString(ARG_DESCRIPTION).orEmpty(),
            imageUrl = getString(ARG_IMAGE_URL).orEmpty(),
            miniImageUrl = getString(ARG_MINI_IMAGE_URL).orEmpty(),
            assignedAtMillis = getLong(ARG_ASSIGNED_AT),
            isPrimary = getBoolean(ARG_IS_PRIMARY),
        )
    }
}
