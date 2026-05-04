package com.barkfluff.client.dialog

import android.app.Dialog
import android.os.Bundle
import android.util.Log
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.LinearLayoutManager
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.adapter.ForwardChatPickerAdapter
import com.barkfluff.client.databinding.BottomSheetForwardChatsBinding
import com.barkfluff.client.repository.ChatRepository
import com.google.android.material.bottomsheet.BottomSheetBehavior
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.launch

/**
 * Модалка для пересылки сообщения в один или несколько чатов.
 * Принимает ID исходного сообщения, отправляет в выбранные чаты с тем же forwarded_message_id.
 */
class ForwardChatPickerBottomSheet : BottomSheetDialogFragment() {

    companion object {
        private const val TAG = "ForwardPicker"
        private const val ARG_MESSAGE_ID = "messageId"

        fun newInstance(messageId: Long): ForwardChatPickerBottomSheet {
            return ForwardChatPickerBottomSheet().apply {
                arguments = Bundle().apply {
                    putLong(ARG_MESSAGE_ID, messageId)
                }
            }
        }
    }

    private var _binding: BottomSheetForwardChatsBinding? = null
    private val binding get() = _binding!!

    private lateinit var chatRepository: ChatRepository
    private lateinit var adapter: ForwardChatPickerAdapter
    private var messageId: Long = 0L

    override fun onCreateDialog(savedInstanceState: Bundle?): Dialog {
        val dialog = super.onCreateDialog(savedInstanceState) as BottomSheetDialog
        dialog.setOnShowListener {
            val sheet = dialog.findViewById<View>(com.google.android.material.R.id.design_bottom_sheet)
            sheet?.let {
                BottomSheetBehavior.from(it).apply {
                    state = BottomSheetBehavior.STATE_EXPANDED
                    skipCollapsed = true
                }
            }
        }
        return dialog
    }

    override fun onCreateView(
        inflater: LayoutInflater,
        container: ViewGroup?,
        savedInstanceState: Bundle?
    ): View {
        _binding = BottomSheetForwardChatsBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)

        messageId = arguments?.getLong(ARG_MESSAGE_ID, 0L) ?: 0L
        if (messageId == 0L) {
            dismissAllowingStateLoss()
            return
        }

        val app = requireActivity().application as BarkFluffApplication
        val grpcManager = app.grpcManager
        chatRepository = ChatRepository(requireContext(), grpcManager)

        adapter = ForwardChatPickerAdapter(
            getFileUrl = { fileId -> chatRepository.getFileDownloadUrl(fileId).getOrNull() },
            onSelectionChanged = { count ->
                binding.sendButton.isEnabled = count > 0
                binding.sendButton.text = if (count > 0) "Переслать ($count)" else "Переслать"
            }
        )

        binding.chatsRecyclerView.apply {
            layoutManager = LinearLayoutManager(requireContext())
            adapter = this@ForwardChatPickerBottomSheet.adapter
        }

        binding.sendButton.setOnClickListener {
            performForward()
        }

        loadChats(grpcManager)
    }

    private fun loadChats(grpcManager: com.barkfluff.client.grpc.GrpcManager) {
        binding.loadingProgress.visibility = View.VISIBLE
        binding.chatsRecyclerView.visibility = View.GONE
        lifecycleScope.launch {
            val result = grpcManager.getChats()
            binding.loadingProgress.visibility = View.GONE
            binding.chatsRecyclerView.visibility = View.VISIBLE
            if (result.isSuccess) {
                adapter.submitList(result.getOrNull().orEmpty())
            } else {
                Toast.makeText(
                    requireContext(),
                    "Не удалось загрузить чаты: ${result.exceptionOrNull()?.message}",
                    Toast.LENGTH_SHORT
                ).show()
            }
        }
    }

    private fun performForward() {
        val selected = adapter.getSelectedIds()
        if (selected.isEmpty()) return

        val comment = binding.commentEditText.text?.toString()?.trim().orEmpty()

        binding.sendButton.isEnabled = false
        binding.sendButton.text = "Отправка..."

        lifecycleScope.launch {
            val results = selected.map { chatId ->
                async {
                    chatRepository.sendMessage(
                        chatId = chatId,
                        text = comment,
                        forwardedMessageId = messageId
                    )
                }
            }.awaitAll()

            val successCount = results.count { it.isSuccess }
            val failCount = results.size - successCount

            val msg = when {
                failCount == 0 -> "Переслано в $successCount ${chatPlural(successCount)}"
                successCount == 0 -> "Не удалось переслать"
                else -> "Переслано в $successCount, ошибок: $failCount"
            }
            Toast.makeText(requireContext(), msg, Toast.LENGTH_SHORT).show()

            if (failCount > 0) {
                Log.w(TAG, "Some forwards failed: ${results.filter { it.isFailure }.map { it.exceptionOrNull()?.message }}")
            }

            dismissAllowingStateLoss()
        }
    }

    private fun chatPlural(n: Int): String {
        val mod10 = n % 10
        val mod100 = n % 100
        return when {
            mod10 == 1 && mod100 != 11 -> "чат"
            mod10 in 2..4 && mod100 !in 12..14 -> "чата"
            else -> "чатов"
        }
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }
}
