package com.barkfluff.client

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import androidx.core.os.bundleOf
import com.barkfluff.client.databinding.SheetCreateChatBinding
import com.google.android.material.bottomsheet.BottomSheetDialogFragment

class CreateChatBottomSheet : BottomSheetDialogFragment() {

    private var _binding: SheetCreateChatBinding? = null
    private val binding get() = _binding!!

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = SheetCreateChatBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        val arguments = requireArguments()
        binding.privateChatButton.visibility = if (arguments.getBoolean(ARG_PRIVATE_ENABLED)) View.VISIBLE else View.GONE
        binding.secretChatButton.visibility = if (arguments.getBoolean(ARG_SECRET_ENABLED)) View.VISIBLE else View.GONE
        binding.regularChatButton.setOnClickListener { publish(TYPE_REGULAR) }
        binding.groupChatButton.setOnClickListener { publish(TYPE_GROUP) }
        binding.privateChatButton.setOnClickListener { publish(TYPE_PRIVATE) }
        binding.secretChatButton.setOnClickListener { publish(TYPE_SECRET) }
    }

    override fun onStart() {
        super.onStart()
        dialog?.window?.setDimAmount(0.42f)
        (dialog as? com.google.android.material.bottomsheet.BottomSheetDialog)
            ?.findViewById<FrameLayout>(com.google.android.material.R.id.design_bottom_sheet)
            ?.setBackgroundResource(R.drawable.bg_create_chat_sheet)
    }

    private fun publish(type: String) {
        parentFragmentManager.setFragmentResult(RESULT_KEY, bundleOf(RESULT_TYPE to type))
        dismiss()
    }

    override fun onDestroyView() {
        _binding = null
        super.onDestroyView()
    }

    companion object {
        const val TAG = "create_chat_sheet"
        const val RESULT_KEY = "create_chat_type"
        const val RESULT_TYPE = "type"
        const val TYPE_REGULAR = "regular"
        const val TYPE_GROUP = "group"
        const val TYPE_PRIVATE = "private"
        const val TYPE_SECRET = "secret"
        private const val ARG_PRIVATE_ENABLED = "private_enabled"
        private const val ARG_SECRET_ENABLED = "secret_enabled"

        fun newInstance(privateEnabled: Boolean, secretEnabled: Boolean) = CreateChatBottomSheet().apply {
            arguments = bundleOf(
                ARG_PRIVATE_ENABLED to privateEnabled,
                ARG_SECRET_ENABLED to secretEnabled
            )
        }
    }
}
