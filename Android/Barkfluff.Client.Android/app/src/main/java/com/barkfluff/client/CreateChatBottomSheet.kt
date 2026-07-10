package com.barkfluff.client

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
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
        binding.privateChatButton.visibility = if (requireArguments().getBoolean(ARG_PRIVATE_ENABLED)) View.VISIBLE else View.GONE
        binding.regularChatButton.setOnClickListener { publish(TYPE_REGULAR) }
        binding.groupChatButton.setOnClickListener { publish(TYPE_GROUP) }
        binding.privateChatButton.setOnClickListener { publish(TYPE_PRIVATE) }
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
        private const val ARG_PRIVATE_ENABLED = "private_enabled"

        fun newInstance(privateEnabled: Boolean) = CreateChatBottomSheet().apply {
            arguments = bundleOf(ARG_PRIVATE_ENABLED to privateEnabled)
        }
    }
}
