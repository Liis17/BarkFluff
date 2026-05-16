package com.barkfluff.client.share

import android.net.Uri
import android.os.Bundle
import android.provider.OpenableColumns
import android.util.Log
import android.util.Size
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.Toast
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import coil.load
import com.barkfluff.client.R
import com.barkfluff.client.databinding.SheetShareConfirmBinding
import com.barkfluff.client.editor.EditedVideoSpec
import com.barkfluff.client.send.AttachmentSpec
import com.barkfluff.client.send.MediaSendService
import com.barkfluff.client.send.SendJob
import com.google.android.material.bottomsheet.BottomSheetDialogFragment
import java.text.DecimalFormat

/**
 * Bottom-sheet с превью контента из share-intent и полем подписи.
 * По кнопке «Отправить» ставит задачу в [MediaSendService] и завершает share-сессию.
 */
class ShareConfirmBottomSheet : BottomSheetDialogFragment() {

    private var _binding: SheetShareConfirmBinding? = null
    private val binding get() = _binding!!

    private lateinit var chatId: String
    private lateinit var chatTitle: String

    /** Берётся из родительской [ShareReceiverActivity], чтобы не парселить Uri-списки. */
    private val payload: SharePayload?
        get() = (activity as? ShareReceiverActivity)?.payload

    companion object {
        private const val ARG_CHAT_ID = "chat_id"
        private const val ARG_CHAT_TITLE = "chat_title"
        private const val TAG = "ShareConfirmSheet"

        fun newInstance(chatId: String, chatTitle: String, payload: SharePayload): ShareConfirmBottomSheet {
            // payload не передаём через args — он хранится в активити.
            val f = ShareConfirmBottomSheet()
            f.arguments = Bundle().apply {
                putString(ARG_CHAT_ID, chatId)
                putString(ARG_CHAT_TITLE, chatTitle)
            }
            // payload остаётся в activity.payload — fragment читает его через property.
            @Suppress("UNUSED_VARIABLE") val unused = payload
            return f
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        chatId = requireArguments().getString(ARG_CHAT_ID).orEmpty()
        chatTitle = requireArguments().getString(ARG_CHAT_TITLE).orEmpty()
    }

    override fun onCreateView(inflater: LayoutInflater, container: ViewGroup?, savedInstanceState: Bundle?): View {
        _binding = SheetShareConfirmBinding.inflate(inflater, container, false)
        return binding.root
    }

    override fun onViewCreated(view: View, savedInstanceState: Bundle?) {
        super.onViewCreated(view, savedInstanceState)
        val p = payload
        if (p == null) {
            dismissAllowingStateLoss()
            return
        }

        binding.sheetTitle.text = getString(R.string.share_send_to, chatTitle)
        bindPreview(p)
        binding.sendButton.setOnClickListener { onSendClicked() }
    }

    private fun bindPreview(p: SharePayload) {
        when (p) {
            is SharePayload.Text -> {
                binding.textField.setText(p.text)
                binding.textField.hint = null
            }
            is SharePayload.SingleFile -> bindSingleFile(p)
            is SharePayload.MultipleFiles -> bindMultiple(p)
        }
    }

    private fun bindSingleFile(p: SharePayload.SingleFile) {
        when {
            p.mime.startsWith("image/") -> {
                binding.previewImage.visibility = View.VISIBLE
                binding.previewImage.load(p.uri)
            }
            p.mime.startsWith("video/") -> {
                binding.previewImage.visibility = View.VISIBLE
                loadVideoThumb(p.uri, binding.previewImage)
            }
            else -> bindDocument(p.uri, p.mime)
        }
    }

    private fun bindDocument(uri: Uri, mime: String) {
        val (name, size) = queryNameAndSize(uri)
        binding.previewDocument.visibility = View.VISIBLE
        binding.previewDocName.text = name ?: uri.lastPathSegment ?: "Файл"
        binding.previewDocMeta.text = buildString {
            append(formatSize(size))
            if (mime.isNotBlank()) {
                append(" · ")
                append(mime)
            }
        }
    }

    private fun bindMultiple(p: SharePayload.MultipleFiles) {
        binding.previewMulti.visibility = View.VISIBLE
        binding.previewMulti.layoutManager =
            LinearLayoutManager(requireContext(), LinearLayoutManager.HORIZONTAL, false)
        binding.previewMulti.adapter = ThumbAdapter(p.items)
        // Показать суммарную метку под полем
        binding.previewDocument.visibility = View.GONE
        binding.previewImage.visibility = View.GONE
    }

    private fun loadVideoThumb(uri: Uri, target: com.google.android.material.imageview.ShapeableImageView) {
        // На API 29+ доступен ContentResolver.loadThumbnail для image/video.
        try {
            val bmp = requireContext().contentResolver.loadThumbnail(uri, Size(512, 512), null)
            target.setImageBitmap(bmp)
        } catch (e: Exception) {
            Log.w(TAG, "loadThumbnail failed for $uri", e)
            target.setImageResource(android.R.drawable.ic_menu_gallery)
        }
    }

    private fun queryNameAndSize(uri: Uri): Pair<String?, Long> {
        var name: String? = null
        var size: Long = 0L
        try {
            requireContext().contentResolver.query(uri, null, null, null, null)?.use { c ->
                if (c.moveToFirst()) {
                    val nameIdx = c.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    if (nameIdx >= 0) name = c.getString(nameIdx)
                    val sizeIdx = c.getColumnIndex(OpenableColumns.SIZE)
                    if (sizeIdx >= 0) size = c.getLong(sizeIdx)
                }
            }
        } catch (e: Exception) {
            Log.w(TAG, "query failed for $uri", e)
        }
        return name to size
    }

    private fun formatSize(bytes: Long): String {
        if (bytes <= 0) return ""
        val units = arrayOf("B", "KB", "MB", "GB")
        var v = bytes.toDouble()
        var i = 0
        while (v >= 1024 && i < units.lastIndex) {
            v /= 1024.0
            i++
        }
        val df = DecimalFormat("#.#")
        return "${df.format(v)} ${units[i]}"
    }

    private fun onSendClicked() {
        val p = payload ?: run { dismissAllowingStateLoss(); return }
        val caption = binding.textField.text?.toString().orEmpty()

        val (text, attachments) = when (p) {
            is SharePayload.Text -> {
                val finalText = caption.ifBlank { p.text }
                finalText to emptyList<AttachmentSpec>()
            }
            is SharePayload.SingleFile -> {
                caption to listOf(toAttachment(p.uri, p.mime))
            }
            is SharePayload.MultipleFiles -> {
                caption to p.items.map { toAttachment(it.uri, it.mime) }
            }
        }

        if (text.isBlank() && attachments.isEmpty()) {
            Toast.makeText(requireContext(), R.string.share_empty, Toast.LENGTH_SHORT).show()
            return
        }

        val job = SendJob(
            chatId = chatId,
            chatTitle = chatTitle,
            text = text,
            attachments = attachments,
            replyId = 0L,
            sendSeparately = false,
            sendAsFile = false
        )
        MediaSendService.enqueue(requireContext().applicationContext, job)

        Toast.makeText(
            requireContext(),
            getString(R.string.share_sent_toast, chatTitle),
            Toast.LENGTH_SHORT
        ).show()
        dismissAllowingStateLoss()
        activity?.finish()
    }

    private fun toAttachment(uri: Uri, mime: String): AttachmentSpec = when {
        mime.startsWith("image/") -> AttachmentSpec.RawImage(uri)
        mime.startsWith("video/") -> AttachmentSpec.Video(EditedVideoSpec(uri = uri))
        else -> AttachmentSpec.Document(uri)
    }

    override fun onDestroyView() {
        super.onDestroyView()
        _binding = null
    }

    private inner class ThumbAdapter(
        private val items: List<SharePayload.MultipleFiles.Item>
    ) : RecyclerView.Adapter<ThumbAdapter.VH>() {

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
            val view = LayoutInflater.from(parent.context)
                .inflate(R.layout.item_share_preview_thumb, parent, false)
            return VH(view as com.google.android.material.imageview.ShapeableImageView)
        }

        override fun getItemCount(): Int = items.size

        override fun onBindViewHolder(holder: VH, position: Int) {
            val item = items[position]
            when {
                item.mime.startsWith("image/") -> holder.image.load(item.uri)
                item.mime.startsWith("video/") -> loadVideoThumb(item.uri, holder.image)
                else -> holder.image.setImageResource(android.R.drawable.ic_menu_save)
            }
        }

        inner class VH(val image: com.google.android.material.imageview.ShapeableImageView) :
            RecyclerView.ViewHolder(image)
    }
}
