package com.barkfluff.client.adapter

import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.Looper
import android.provider.MediaStore
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.webkit.MimeTypeMap
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.PopupMenu
import android.widget.SeekBar
import android.widget.Toast
import androidx.core.content.FileProvider
import androidx.core.view.updateLayoutParams
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.load
import com.barkfluff.client.ImageViewerActivity
import com.barkfluff.client.MediaViewerActivity
import com.barkfluff.client.R
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ItemAttachmentAudioBinding
import com.google.android.material.card.MaterialCardView
import com.google.android.material.imageview.ShapeableImageView
import com.google.android.material.shape.CornerFamily
import com.google.android.material.shape.ShapeAppearanceModel
import com.barkfluff.client.databinding.ItemAttachmentDocumentBinding
import com.barkfluff.client.databinding.ItemAttachmentVideoBinding
import com.barkfluff.client.databinding.ItemMessageDateSeparatorBinding
import com.barkfluff.client.databinding.ItemMessageReceivedBinding
import com.barkfluff.client.databinding.ItemMessageSentBinding
import com.barkfluff.client.databinding.ViewMessageQuoteBinding
import com.barkfluff.client.utils.AudioCallbacks
import com.barkfluff.client.utils.AudioPlayerHelper
import com.barkfluff.client.utils.AudioWaveformExtractor
import com.barkfluff.client.utils.FileCache
import com.barkfluff.client.utils.FileMediaUrl
import com.barkfluff.client.utils.ImageLoadHelper
import com.barkfluff.client.utils.MarkdownRenderer
import com.barkfluff.client.utils.AvatarLoader
import barkfluff.shared.Shared
import android.content.res.ColorStateList
import android.graphics.drawable.GradientDrawable
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileInputStream
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Адаптер для отображения сообщений в чате с разделителями дат.
 * Поддерживает четыре типа view: отправленные сообщения, полученные сообщения,
 * разделители дат и разделитель непрочитанных сообщений.
 */
class MessageAdapter(
    private val currentUserId: Long,
    private val isGroupChat: Boolean,
    private val getFileUrl: suspend (String) -> String? = { null },
    private val downloadToCache: suspend (fileId: String, onProgress: (Int) -> Unit) -> java.io.File? = { _, _ -> null },
    private val scope: CoroutineScope = CoroutineScope(Dispatchers.Main),
    /** Закругление облачков сообщений в dp (0..30). */
    var messageCornerRadiusDp: Int = 28,
    /** Размер стикеров в чате в dp. */
    var stickerSizeDp: Int = GlobalParam.DEFAULT_STICKER_SIZE_DP,
    /** Вызывается при клике на сообщение — открыть меню действий. rawX/rawY = абсолютные координаты касания на экране. */
    private val onMessageActionRequested: ((anchor: View, item: MessageItem, rawX: Float, rawY: Float) -> Unit)? = null,
    /** Вызывается при клике на reply-цитату внутри сообщения — переход к оригиналу. */
    private val onReplyQuoteClick: ((originalMessageId: Long) -> Unit)? = null,
    /** Резолвер информации об отправителе в групповом чате: senderId -> (имя, URL/fileId аватара). null = брать из самого MessageItem. */
    private val senderInfoProvider: ((senderId: Long) -> Pair<String?, String?>?)? = null
) : ListAdapter<MessageItem, RecyclerView.ViewHolder>(MessageDiffCallback()) {

    /** Возвращает MessageItem по позиции для обработчика свайпа (ItemTouchHelper). null если позиция вне диапазона или не сообщение. */
    fun getMessageAt(position: Int): MessageItem? {
        if (position < 0 || position >= itemCount) return null
        val item = getItem(position)
        return if (item.type == MessageType.MESSAGE) item else null
    }

    companion object {
        private const val VIEW_TYPE_SENT = 1
        private const val VIEW_TYPE_RECEIVED = 2
        private const val VIEW_TYPE_DATE_SEPARATOR = 3
        private const val VIEW_TYPE_UNREAD_SEPARATOR = 4
        private const val VIEW_TYPE_FOOTER = 5
        private const val VIEW_TYPE_SYSTEM = 6
        private const val VOICE_AUTO_DOWNLOAD_LIMIT_BYTES = 2L * 1024L * 1024L

        /**
         * Payload для случая, когда подгрузился кэш участников группы: меняются только
         * имя и мини-аватар отправителя, полный ребинд (с перезапуском загрузки вложений)
         * не нужен.
         */
        const val PAYLOAD_SENDER_INFO = "sender_info"

        /** «Хвостик» последнего пузыря в серии. */
        private const val BUBBLE_TAIL_CORNER_DP = 8f
        /** Отступ между сериями сообщений. */
        private const val BUBBLE_GROUP_GAP_DP = 10
        /** Отступ между сообщениями внутри одной серии. */
        private const val BUBBLE_INNER_GAP_DP = 3

        private val voiceAutoDownloads = mutableSetOf<String>()
        private val voiceWaveformCache = mutableMapOf<String, FloatArray>()

        private val FOOTER_ITEM = MessageItem(
            messageId = Long.MIN_VALUE,
            senderId = 0,
            text = "",
            timestamp = 0,
            attachments = emptyList(),
            type = MessageType.FOOTER
        )
    }

    /** Удаляет все footer-элементы из списка и добавляет один в конец. */
    private fun MutableList<MessageItem>.ensureFooter() {
        removeAll { it.type == MessageType.FOOTER }
        add(FOOTER_ITEM)
    }

    /**
     * Отфильтровывает повторяющиеся по messageId items типа MESSAGE/SYSTEM —
     * страховка от случайных дублей при пересечении realtime-event и пагинации.
     */
    private fun List<MessageItem>.dedupMessages(): List<MessageItem> {
        val seen = HashSet<Long>()
        return filter { item ->
            if (item.type != MessageType.MESSAGE && item.type != MessageType.SYSTEM) true
            else seen.add(item.messageId)
        }
    }

    /** Резолвит цветной theme-атрибут (?attr/colorPrimary и т.п.) в int color. */
    private fun resolveThemeColor(ctx: Context, attr: Int): Int {
        val tv = android.util.TypedValue()
        ctx.theme.resolveAttribute(attr, tv, true)
        return tv.data
    }

    /**
     * Применяет иконку статуса доставки к ImageView. Маппинг ReadStatus → drawable + tint
     * соответствует M3 Expressive feedback (часы → одна галочка → две → две filled primary).
     * FAILED перекрывает текущий tint на colorError.
     */
    private fun applyDeliveryStatusIcon(
        view: ImageView,
        status: ReadStatus,
        useLightTint: Boolean = false
    ) {
        val ctx = view.context
        val lightTint = if (useLightTint) ColorStateList.valueOf(android.graphics.Color.WHITE) else null
        when (status) {
            ReadStatus.NONE -> view.visibility = View.GONE
            ReadStatus.SENDING -> {
                view.setImageResource(R.drawable.ic_status_sending)
                view.imageTintList = lightTint
                view.visibility = View.VISIBLE
            }
            ReadStatus.SENT -> {
                view.setImageResource(R.drawable.ic_status_sent)
                view.imageTintList = lightTint
                view.visibility = View.VISIBLE
            }
            ReadStatus.DELIVERED -> {
                view.setImageResource(R.drawable.ic_status_delivered)
                view.imageTintList = lightTint
                view.visibility = View.VISIBLE
            }
            ReadStatus.READ -> {
                view.setImageResource(R.drawable.ic_status_read)
                view.imageTintList = lightTint
                    ?: ColorStateList.valueOf(resolveThemeColor(ctx, androidx.appcompat.R.attr.colorPrimary))
                view.visibility = View.VISIBLE
            }
            ReadStatus.FAILED -> {
                view.setImageResource(R.drawable.ic_status_error)
                view.imageTintList = ColorStateList.valueOf(resolveThemeColor(ctx, androidx.appcompat.R.attr.colorError))
                view.visibility = View.VISIBLE
            }
        }
    }

    /** Переопределяем submitList — footer всегда в конце списка. */
    override fun submitList(list: List<MessageItem>?) {
        val mutable = (list ?: emptyList()).dedupMessages().toMutableList()
        mutable.ensureFooter()
        super.submitList(mutable)
    }

    override fun submitList(list: List<MessageItem>?, commitCallback: Runnable?) {
        val mutable = (list ?: emptyList()).dedupMessages().toMutableList()
        mutable.ensureFooter()
        super.submitList(mutable, commitCallback)
    }

    override fun getItemViewType(position: Int): Int {
        val item = getItem(position)
        return when (item.type) {
            MessageType.FOOTER -> VIEW_TYPE_FOOTER
            MessageType.DATE_SEPARATOR -> VIEW_TYPE_DATE_SEPARATOR
            MessageType.UNREAD_SEPARATOR -> VIEW_TYPE_UNREAD_SEPARATOR
            MessageType.SYSTEM -> VIEW_TYPE_SYSTEM
            MessageType.MESSAGE -> if (item.senderId == currentUserId) VIEW_TYPE_SENT else VIEW_TYPE_RECEIVED
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): RecyclerView.ViewHolder {
        return when (viewType) {
            VIEW_TYPE_SENT -> SentMessageViewHolder(
                ItemMessageSentBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
            VIEW_TYPE_RECEIVED -> ReceivedMessageViewHolder(
                ItemMessageReceivedBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
            VIEW_TYPE_UNREAD_SEPARATOR -> UnreadSeparatorViewHolder(
                ItemMessageDateSeparatorBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
            VIEW_TYPE_FOOTER -> FooterViewHolder(
                LayoutInflater.from(parent.context)
                    .inflate(R.layout.item_message_footer, parent, false)
            )
            VIEW_TYPE_SYSTEM -> SystemMessageViewHolder(
                LayoutInflater.from(parent.context)
                    .inflate(R.layout.item_message_system, parent, false)
            )
            else -> DateSeparatorViewHolder(
                ItemMessageDateSeparatorBinding.inflate(LayoutInflater.from(parent.context), parent, false)
            )
        }
    }

    override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
        val item = getItem(position)
        when (holder) {
            is SentMessageViewHolder -> holder.bind(item, groupPositionOf(position))
            is ReceivedMessageViewHolder -> holder.bind(item, groupPositionOf(position))
            is DateSeparatorViewHolder -> holder.bind(item)
            is UnreadSeparatorViewHolder -> holder.bind(item)
            is SystemMessageViewHolder -> holder.bind(item)
            is FooterViewHolder -> { /* спейсер, биндинг не нужен */ }
        }
    }

    override fun onBindViewHolder(
        holder: RecyclerView.ViewHolder,
        position: Int,
        payloads: MutableList<Any>
    ) {
        // Незнакомый payload или пустой список — обычный полный бинд.
        if (payloads.isEmpty() || payloads.any { it != PAYLOAD_SENDER_INFO }) {
            super.onBindViewHolder(holder, position, payloads)
            return
        }
        if (holder is ReceivedMessageViewHolder) {
            holder.bindSenderInfo(getItem(position))
        }
    }

    /** Место сообщения в серии подряд идущих сообщений одного отправителя. */
    data class GroupPosition(val isFirst: Boolean, val isLast: Boolean)

    private fun groupPositionOf(position: Int): GroupPosition {
        val item = getItem(position)
        val previous = if (position > 0) getItem(position - 1) else null
        val next = if (position < itemCount - 1) getItem(position + 1) else null
        fun continues(neighbour: MessageItem?) =
            neighbour != null && neighbour.type == MessageType.MESSAGE && neighbour.senderId == item.senderId
        return GroupPosition(isFirst = !continues(previous), isLast = !continues(next))
    }

    /**
     * Форма пузыря по макету M3E: серия сообщений одного отправителя срастается,
     * «хвостик» (маленький угол) остаётся только у последнего сообщения серии.
     */
    private fun applyBubbleShape(card: MaterialCardView, group: GroupPosition, isSentByMe: Boolean) {
        val density = card.resources.displayMetrics.density
        val big = messageCornerRadiusDp * density
        val mid = big / 2f
        val small = minOf(BUBBLE_TAIL_CORNER_DP * density, mid)

        val builder = ShapeAppearanceModel.builder()
        if (isSentByMe) {
            builder.setTopLeftCornerSize(big)
                .setTopRightCornerSize(if (group.isFirst) big else mid)
                .setBottomRightCornerSize(if (group.isLast) small else mid)
                .setBottomLeftCornerSize(big)
        } else {
            builder.setTopLeftCornerSize(if (group.isFirst) big else mid)
                .setTopRightCornerSize(big)
                .setBottomRightCornerSize(big)
                .setBottomLeftCornerSize(if (group.isLast) small else mid)
        }
        card.shapeAppearanceModel = builder.build()
    }

    /** Сообщения внутри серии стоят плотнее, чем соседние серии. */
    private fun applyGroupSpacing(root: View, group: GroupPosition) {
        val density = root.resources.displayMetrics.density
        val spacing = if (group.isLast) BUBBLE_GROUP_GAP_DP else BUBBLE_INNER_GAP_DP
        root.updateLayoutParams<ViewGroup.MarginLayoutParams> {
            bottomMargin = (spacing * density).toInt()
        }
    }

    inner class SystemMessageViewHolder(view: View) : RecyclerView.ViewHolder(view) {
        private val systemText: android.widget.TextView = view.findViewById(R.id.systemText)
        fun bind(item: MessageItem) {
            systemText.text = item.text.ifBlank { item.dateText }
        }
    }

    // ─── Sent Message ViewHolder ───────────────────────────────────────────────

    inner class FooterViewHolder(view: View) : RecyclerView.ViewHolder(view)

    inner class SentMessageViewHolder(
        private val binding: ItemMessageSentBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        private var lastTouchRawX: Float = 0f
        private var lastTouchRawY: Float = 0f

        @android.annotation.SuppressLint("ClickableViewAccessibility")
        fun bind(item: MessageItem, group: GroupPosition) {
            applyGroupSpacing(binding.root, group)
            // Перехват raw координат касания для позиционирования popup
            binding.root.setOnTouchListener { _, event ->
                if (event.action == android.view.MotionEvent.ACTION_DOWN) {
                    lastTouchRawX = event.rawX
                    lastTouchRawY = event.rawY
                }
                false
            }
            // Click по корневому FrameLayout (вне bubble) — открывает action menu в точке касания
            binding.root.setOnClickListener { v ->
                onMessageActionRequested?.invoke(v, item, lastTouchRawX, lastTouchRawY)
            }

            // Цитата reply (выше текста) и forward (ниже текста) — выбираем какую показать
            bindQuoteSplit(binding.replyQuote, binding.forwardQuotesContainer, item.attachments, item.replyTo)

            // Вложения для основного отображения (без FORWARDED_MESSAGE — он рендерится через quote)
            val displayedAttachments = item.attachments.filter {
                it.type != Shared.MessageAttachmentType.FORWARDED_MESSAGE
            }

            // Определяем, является ли сообщение «чистым стикером»
            val isPureSticker = item.text.isBlank() &&
                displayedAttachments.size == 1 &&
                displayedAttachments[0].type == Shared.MessageAttachmentType.STICKER

            if (isPureSticker) {
                // Показать стикер без облачка
                binding.messageCard.visibility = View.GONE
                binding.stickerContainer.visibility = View.VISIBLE
                binding.stickerTimeStatusLayout.visibility = View.VISIBLE

                val attachment = displayedAttachments[0]
                applyStickerSize(binding.stickerImageView)
                loadStickerImage(binding.stickerImageView, attachment)

                binding.stickerTimeTextView.text = formatTime(item.timestamp)
                applyDeliveryStatusIcon(binding.stickerReadStatusImageView, item.readStatus, useLightTint = true)
            } else {
                // Обычное сообщение с облачком
                binding.messageCard.visibility = View.VISIBLE
                binding.stickerContainer.visibility = View.GONE
                binding.stickerTimeStatusLayout.visibility = View.GONE

                // Форма пузыря: базовый радиус — из настроек персонализации
                applyBubbleShape(binding.messageCard, group, isSentByMe = true)

                if (item.text.isNotBlank()) {
                    MarkdownRenderer.renderMessageInto(
                        binding.messageMarkdownContainer,
                        binding.messageTextView,
                        item.text
                    )
                    binding.messageTextView.visibility = View.VISIBLE
                } else {
                    MarkdownRenderer.clearMessageContent(
                        binding.messageMarkdownContainer,
                        binding.messageTextView
                    )
                }

                binding.timeTextView.text = formatTime(item.timestamp)
                binding.editedLabelTextView.visibility = if (item.isEdited) View.VISIBLE else View.GONE

                applyDeliveryStatusIcon(binding.readStatusImageView, item.readStatus)

                val showMediaTimeOverlay = item.text.isBlank() &&
                    (item.localPreviewUris.isNotEmpty() || displayedAttachments.isPureMedia())
                binding.timeStatusLayout.visibility = if (showMediaTimeOverlay) View.GONE else View.VISIBLE

                if (item.localPreviewUris.isNotEmpty()) {
                    // Оптимистичное сообщение: вложения ещё не на сервере — рендерим локальные превью.
                    val mediaWidthPx = calcMediaWidthPx(binding.root.context)
                    binding.attachmentsContainer.layoutParams = binding.attachmentsContainer.layoutParams.also {
                        it.width = mediaWidthPx
                    }
                    binding.attachmentsContainer.removeAllViews()
                    binding.attachmentsContainer.addView(
                        buildLocalMediaGrid(binding.root.context, item.localPreviewUris, mediaWidthPx)
                    )
                    if (showMediaTimeOverlay) {
                        bindMediaTimeOverlay(binding.attachmentsContainer, item)
                    }
                    binding.attachmentsContainer.visibility = View.VISIBLE
                } else if (displayedAttachments.isNotEmpty()) {
                    val hasMedia = displayedAttachments.any {
                        it.type == Shared.MessageAttachmentType.IMAGE ||
                        it.type == Shared.MessageAttachmentType.GIF  ||
                        it.type == Shared.MessageAttachmentType.VIDEO
                    }
                    val mediaWidthPx = if (hasMedia) calcMediaWidthPx(binding.root.context) else 0
                    binding.attachmentsContainer.layoutParams = binding.attachmentsContainer.layoutParams.also {
                        it.width = if (mediaWidthPx > 0) mediaWidthPx else ViewGroup.LayoutParams.WRAP_CONTENT
                    }
                    setupAttachmentsContainer(
                        binding.attachmentsContainer,
                        displayedAttachments,
                        mediaWidthPx,
                        isSentByMe = true,
                        sourceMessageId = item.messageId
                    )
                    if (showMediaTimeOverlay) {
                        bindMediaTimeOverlay(binding.attachmentsContainer, item)
                    }
                    binding.attachmentsContainer.visibility = View.VISIBLE
                } else {
                    binding.attachmentsContainer.layoutParams = binding.attachmentsContainer.layoutParams.also {
                        it.width = ViewGroup.LayoutParams.WRAP_CONTENT
                    }
                    binding.attachmentsContainer.visibility = View.GONE
                    binding.attachmentsContainer.removeAllViews()
                }

                // Inline upload progress overlay (M3 Expressive feedback) — рендерится
                // поверх attachmentsContainer пока активен upload.
                val progress = item.uploadProgress
                if (progress != null) {
                    binding.uploadProgressOverlay.visibility = View.VISIBLE
                    binding.uploadProgressBar.progress = progress
                    binding.uploadProgressLabel.text = binding.root.context.getString(
                        R.string.message_upload_progress,
                        progress
                    )
                    binding.uploadProgressOverlay.layoutParams = binding.uploadProgressOverlay.layoutParams.also {
                        it.width = if (binding.attachmentsContainer.visibility == View.VISIBLE)
                            binding.attachmentsContainer.layoutParams.width
                        else ViewGroup.LayoutParams.MATCH_PARENT
                        it.height = ViewGroup.LayoutParams.MATCH_PARENT
                    }
                } else {
                    binding.uploadProgressOverlay.visibility = View.GONE
                }
            }
        }
    }

    // ─── Received Message ViewHolder ──────────────────────────────────────────

    inner class ReceivedMessageViewHolder(
        private val binding: ItemMessageReceivedBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        private var lastTouchRawX: Float = 0f
        private var lastTouchRawY: Float = 0f

        /** Вызывается и из полного bind, и из частичного обновления по PAYLOAD_SENDER_INFO. */
        fun bindSenderInfo(item: MessageItem) {
            if (!isGroupChat) {
                binding.senderInfoLayout.visibility = View.GONE
                return
            }
            binding.senderInfoLayout.visibility = View.VISIBLE

            // Имя и аватар отправителя берём из резолвера (кэш участников чата),
            // с фолбэком на поля самого сообщения.
            val resolved = senderInfoProvider?.invoke(item.senderId)
            val senderName = resolved?.first ?: item.senderName
            val senderAvatarFileId = resolved?.second ?: item.senderAvatarFileId

            binding.senderNameTextView.text = senderName

            if (!senderAvatarFileId.isNullOrBlank()) {
                AvatarLoader.loadByFileId(
                    imageView = binding.senderAvatarImageView,
                    placeholderView = binding.senderAvatarPlaceholder,
                    fileId = senderAvatarFileId,
                    displayName = senderName ?: "",
                    userId = item.senderId,
                    size = 48
                ) {
                    getFileUrl(senderAvatarFileId)
                }
            } else {
                binding.senderAvatarImageView.visibility = View.GONE
                AvatarLoader.showPlaceholder(
                    binding.senderAvatarPlaceholder,
                    senderName ?: "",
                    item.senderId
                )
            }
        }

        @android.annotation.SuppressLint("ClickableViewAccessibility")
        fun bind(item: MessageItem, group: GroupPosition) {
            applyGroupSpacing(binding.root, group)
            binding.root.setOnTouchListener { _, event ->
                if (event.action == android.view.MotionEvent.ACTION_DOWN) {
                    lastTouchRawX = event.rawX
                    lastTouchRawY = event.rawY
                }
                false
            }
            binding.root.setOnClickListener { v ->
                onMessageActionRequested?.invoke(v, item, lastTouchRawX, lastTouchRawY)
            }

            // Цитата reply (выше текста) и forward (ниже текста)
            bindQuoteSplit(binding.replyQuote, binding.forwardQuotesContainer, item.attachments, item.replyTo)

            // Вложения для основного отображения (без FORWARDED_MESSAGE — рендерится через quote)
            val displayedAttachments = item.attachments.filter {
                it.type != Shared.MessageAttachmentType.FORWARDED_MESSAGE
            }

            bindSenderInfo(item)

            // Определяем, является ли сообщение «чистым стикером»
            val isPureSticker = item.text.isBlank() &&
                displayedAttachments.size == 1 &&
                displayedAttachments[0].type == Shared.MessageAttachmentType.STICKER

            if (isPureSticker) {
                // Показать стикер без облачка
                binding.messageCard.visibility = View.GONE
                binding.stickerContainer.visibility = View.VISIBLE
                binding.stickerTimeStatusLayout.visibility = View.VISIBLE

                val attachment = displayedAttachments[0]
                applyStickerSize(binding.stickerImageView)
                loadStickerImage(binding.stickerImageView, attachment)

                binding.stickerTimeTextView.text = formatTime(item.timestamp)
            } else {
                // Обычное сообщение с облачком
                binding.messageCard.visibility = View.VISIBLE
                binding.stickerContainer.visibility = View.GONE
                binding.stickerTimeStatusLayout.visibility = View.GONE

                // Форма пузыря: базовый радиус — из настроек персонализации
                applyBubbleShape(binding.messageCard, group, isSentByMe = false)

                if (item.text.isNotBlank()) {
                    MarkdownRenderer.renderMessageInto(
                        binding.messageMarkdownContainer,
                        binding.messageTextView,
                        item.text
                    )
                    binding.messageTextView.visibility = View.VISIBLE
                } else {
                    MarkdownRenderer.clearMessageContent(
                        binding.messageMarkdownContainer,
                        binding.messageTextView
                    )
                }

                binding.timeTextView.text = formatTime(item.timestamp)
                binding.editedLabelTextView.visibility = if (item.isEdited) View.VISIBLE else View.GONE

                val showMediaTimeOverlay = item.text.isBlank() && displayedAttachments.isPureMedia()
                binding.timeStatusLayout.visibility = if (showMediaTimeOverlay) View.GONE else View.VISIBLE

                if (displayedAttachments.isNotEmpty()) {
                    val hasMedia = displayedAttachments.any {
                        it.type == Shared.MessageAttachmentType.IMAGE ||
                        it.type == Shared.MessageAttachmentType.GIF  ||
                        it.type == Shared.MessageAttachmentType.VIDEO
                    }
                    val mediaWidthPx = if (hasMedia) calcMediaWidthPx(binding.root.context) else 0
                    binding.attachmentsContainer.layoutParams = binding.attachmentsContainer.layoutParams.also {
                        it.width = if (mediaWidthPx > 0) mediaWidthPx else ViewGroup.LayoutParams.WRAP_CONTENT
                    }
                    setupAttachmentsContainer(
                        binding.attachmentsContainer,
                        displayedAttachments,
                        mediaWidthPx,
                        sourceMessageId = item.messageId
                    )
                    if (showMediaTimeOverlay) {
                        bindMediaTimeOverlay(binding.attachmentsContainer, item)
                    }
                    binding.attachmentsContainer.visibility = View.VISIBLE
                } else {
                    binding.attachmentsContainer.layoutParams = binding.attachmentsContainer.layoutParams.also {
                        it.width = ViewGroup.LayoutParams.WRAP_CONTENT
                    }
                    binding.attachmentsContainer.visibility = View.GONE
                    binding.attachmentsContainer.removeAllViews()
                }
            }
        }
    }

    // ─── Separator ViewHolders ─────────────────────────────────────────────────

    inner class DateSeparatorViewHolder(
        private val binding: ItemMessageDateSeparatorBinding
    ) : RecyclerView.ViewHolder(binding.root) {
        fun bind(item: MessageItem) { binding.dateTextView.text = item.dateText }
    }

    inner class UnreadSeparatorViewHolder(
        private val binding: ItemMessageDateSeparatorBinding
    ) : RecyclerView.ViewHolder(binding.root) {
        fun bind(item: MessageItem) { binding.dateTextView.text = item.dateText }
    }

    // ─── Forward / Reply Quote ────────────────────────────────────────────────

    /**
     * Биндит цитату по явным данным сервера, а не по догадке:
     *   - reply: заполнено [replyTo] → компактный блок в [replyBinding] выше основного текста
     *   - forward: есть вложения FORWARDED_MESSAGE → блок на каждое пересланное в [forwardContainer] ниже текста
     *
     * Reply и forward больше не исключают друг друга: можно переслать сообщение, отвечая на
     * другое. Когда нет ни того, ни другого — оба контейнера скрыты.
     */
    private fun bindQuoteSplit(
        replyBinding: ViewMessageQuoteBinding,
        forwardContainer: LinearLayout,
        attachments: List<Shared.MessageAttachment>,
        replyTo: Shared.ReplyInfo?
    ) {
        if (replyTo != null) {
            renderReply(replyBinding, replyTo)
        } else {
            hideQuote(replyBinding)
        }

        val forwarded = attachments
            .filter { it.type == Shared.MessageAttachmentType.FORWARDED_MESSAGE && it.hasForwardedMessage() }
            .map { it.forwardedMessage }
            .sortedBy { it.order }

        forwardContainer.removeAllViews()

        if (forwarded.isEmpty()) {
            forwardContainer.visibility = View.GONE
            return
        }

        // Каждое пересланное сообщение — свой блок: пачку нельзя схлопнуть в один,
        // иначе часть пересланного просто не будет показана.
        val inflater = LayoutInflater.from(forwardContainer.context)
        for (data in forwarded) {
            val quote = ViewMessageQuoteBinding.inflate(inflater, forwardContainer, false)
            renderForward(quote, data)
            forwardContainer.addView(quote.root)
        }
        forwardContainer.visibility = View.VISIBLE
    }

    private fun hideQuote(quote: ViewMessageQuoteBinding) {
        quote.quoteContainer.visibility = View.GONE
        quote.replyView.visibility = View.GONE
        quote.forwardView.visibility = View.GONE
    }

    private fun renderReply(quote: ViewMessageQuoteBinding, data: Shared.ReplyInfo) {
        quote.quoteContainer.visibility = View.VISIBLE
        quote.replyView.visibility = View.VISIBLE
        quote.forwardView.visibility = View.GONE

        if (data.isDeleted) {
            // Сервер не отдаёт ни текст, ни автора удалённого оригинала — цитата не должна
            // оставаться способом прочитать удалённое сообщение.
            quote.replyAuthorTextView.text = quote.replyView.context.getString(R.string.message_deleted)
            quote.replyPreviewTextView.text = ""
            quote.replyView.setOnClickListener(null)
            quote.replyView.isClickable = false
            return
        }

        quote.replyAuthorTextView.text = data.senderName.ifBlank {
            quote.replyView.context.getString(R.string.message_placeholder)
        }
        quote.replyPreviewTextView.text = buildReplyPreviewLine(data, quote.replyView.context)

        // Click по reply-блоку — переход к оригиналу
        val origId = data.messageId
        quote.replyView.isClickable = true
        quote.replyView.setOnClickListener {
            onReplyQuoteClick?.invoke(origId)
        }
    }

    /** Превью для reply: текст оригинала, а если его нет — тип первого вложения. */
    private fun buildReplyPreviewLine(data: Shared.ReplyInfo, context: Context): String {
        if (data.textPreview.isNotBlank()) return MarkdownRenderer.strip(data.textPreview)

        return when (data.firstAttachmentType) {
            Shared.MessageAttachmentType.IMAGE, Shared.MessageAttachmentType.GIF -> context.getString(R.string.reply_photo)
            Shared.MessageAttachmentType.VIDEO -> context.getString(R.string.reply_video)
            Shared.MessageAttachmentType.VOICE -> context.getString(R.string.reply_voice)
            Shared.MessageAttachmentType.AUDIO -> context.getString(R.string.reply_audio)
            Shared.MessageAttachmentType.STICKER -> context.getString(R.string.reply_sticker)
            Shared.MessageAttachmentType.DOCUMENT -> context.getString(R.string.reply_file)
            else -> ""
        }
    }

    private fun renderForward(quote: ViewMessageQuoteBinding, data: Shared.ForwardedMessageAttachment) {
        quote.quoteContainer.visibility = View.VISIBLE
        quote.replyView.visibility = View.GONE
        quote.forwardView.visibility = View.VISIBLE
        quote.forwardAuthorTextView.text = data.authorName.ifBlank {
            quote.forwardView.context.getString(R.string.forwarded_message)
        }

        // Медиа-вложения внутри пересланного сообщения (картинки/видео)
        val nestedAtts = data.attachmentsList
        if (nestedAtts.isNotEmpty()) {
            quote.forwardAttachmentsContainer.removeAllViews()
            val ctx = quote.forwardAttachmentsContainer.context
            val maxWidthPx = (calcMediaWidthPx(ctx) * 0.85f).toInt()
            setupAttachmentsContainer(
                quote.forwardAttachmentsContainer,
                nestedAtts,
                maxWidthPx,
                isSentByMe = false,
                sourceMessageId = data.originalMessageId
            )
            quote.forwardAttachmentsContainer.visibility = View.VISIBLE
        } else {
            quote.forwardAttachmentsContainer.visibility = View.GONE
            quote.forwardAttachmentsContainer.removeAllViews()
        }

        if (data.text.isNotBlank()) {
            quote.forwardTextTextView.text = MarkdownRenderer.strip(data.text)
            quote.forwardTextTextView.visibility = View.VISIBLE
        } else {
            quote.forwardTextTextView.visibility = View.GONE
        }
    }

    // ─── Sticker Helper ───────────────────────────────────────────────────────

    private fun loadStickerImage(imageView: ImageView, attachment: Shared.MessageAttachment) {
        val fileId = if (attachment.previewFileId.isNotBlank()) attachment.previewFileId else attachment.fileId
        val previewUrl = FileMediaUrl.rewrite(imageView.context, attachment.previewUrl)

        val getUrl: suspend () -> String? = if (previewUrl.isNotBlank()) {
            { previewUrl }
        } else {
            { getFileUrl(fileId) }
        }

        ImageLoadHelper.loadByFileId(
            imageView = imageView,
            fileId = fileId,
            getUrlCallback = getUrl
        )
    }

    private fun applyStickerSize(imageView: ImageView) {
        val sizePx = (stickerSizeDp * imageView.context.resources.displayMetrics.density + 0.5f).toInt()
        val params = imageView.layoutParams
        if (params.width != sizePx || params.height != sizePx) {
            params.width = sizePx
            params.height = sizePx
            imageView.layoutParams = params
        }
    }

    private fun List<Shared.MessageAttachment>.isPureMedia(): Boolean =
        isNotEmpty() && all {
            it.type == Shared.MessageAttachmentType.IMAGE ||
                it.type == Shared.MessageAttachmentType.GIF ||
                it.type == Shared.MessageAttachmentType.VIDEO
        }

    private fun bindMediaTimeOverlay(
        container: android.widget.FrameLayout,
        item: MessageItem
    ) {
        val overlay = LayoutInflater.from(container.context)
            .inflate(R.layout.view_media_time_status, container, false)
        overlay.findViewById<android.widget.TextView>(R.id.mediaTimeTextView).text = formatTime(item.timestamp)
        applyDeliveryStatusIcon(
            overlay.findViewById(R.id.mediaReadStatusImageView),
            item.readStatus,
            useLightTint = item.readStatus != ReadStatus.NONE
        )
        container.addView(overlay)
    }

    // ─── Color Helpers ─────────────────────────────────────────────────────────

    private fun resolveColor(context: Context, attr: Int): Int {
        val tv = android.util.TypedValue()
        context.theme.resolveAttribute(attr, tv, true)
        return tv.data
    }

    private fun resolveOnPrimaryContainerColor(context: Context): Int =
        resolveColor(context, com.google.android.material.R.attr.colorOnPrimaryContainer)

    private fun resolveOnPrimaryContainerVariantColor(context: Context): Int =
        androidx.core.graphics.ColorUtils.setAlphaComponent(resolveOnPrimaryContainerColor(context), 180)

    private fun resolvePrimaryContainerColor(context: Context): Int =
        resolveColor(context, com.google.android.material.R.attr.colorPrimaryContainer)

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /**
     * Определяет раскладку строк для медиа-сетки (как в WPF MultiImageGrid).
     * Возвращает список, где каждый элемент — количество ячеек в строке.
     */
    private fun determineLayout(count: Int): List<Int> = when (count) {
        0 -> emptyList()
        1 -> listOf(1)
        2 -> listOf(2)
        3 -> listOf(2, 1)
        4 -> listOf(2, 2)
        5 -> listOf(3, 2)
        6 -> listOf(3, 3)
        7 -> listOf(3, 2, 2)
        8 -> listOf(3, 3, 2)
        9 -> listOf(3, 3, 3)
        else -> listOf(3, 2, 2, 3)
    }

    /**
     * Считает ширину медиа-области (px).
     * ~70 % ширины экрана, максимум 320 dp.
     */
    private fun calcMediaWidthPx(context: android.content.Context): Int {
        val dm = context.resources.displayMetrics
        return minOf(
            (dm.widthPixels * 0.70f).toInt(),
            (320 * dm.density + 0.5f).toInt()
        )
    }

    // ─── Attachment Container Setup ───────────────────────────────────────────

    private fun setupAttachmentsContainer(
        container: ViewGroup,
        attachments: List<Shared.MessageAttachment>,
        mediaWidthPx: Int = 0,
        isSentByMe: Boolean = false,
        sourceMessageId: Long? = null
    ) {
        container.removeAllViews()

        // IMAGE + GIF + VIDEO → единая медиа-сетка
        val mediaItems = attachments.filter {
            it.type == Shared.MessageAttachmentType.IMAGE ||
            it.type == Shared.MessageAttachmentType.GIF  ||
            it.type == Shared.MessageAttachmentType.VIDEO
        }
        val stickers = attachments.filter { it.type == Shared.MessageAttachmentType.STICKER }
        val audios = attachments.filter {
            it.type == Shared.MessageAttachmentType.AUDIO ||
            it.type == Shared.MessageAttachmentType.VOICE
        }
        val docs   = attachments.filter {
            it.type == Shared.MessageAttachmentType.DOCUMENT ||
            it.type == Shared.MessageAttachmentType.MESSAGE_ATTACHMENT_TYPE_UNKNOWN
        }

        val context = container.context
        val wrapper = android.widget.LinearLayout(context).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            layoutParams = android.widget.FrameLayout.LayoutParams(
                android.widget.FrameLayout.LayoutParams.MATCH_PARENT,
                android.widget.FrameLayout.LayoutParams.WRAP_CONTENT
            )
        }

        // Медиа-сетка (IMAGE / GIF / VIDEO) — ряды по алгоритму WPF MultiImageGrid
        if (mediaItems.isNotEmpty() && mediaWidthPx > 0) {
            val mediaGrid = buildMediaGrid(context, mediaItems, mediaWidthPx, sourceMessageId)
            wrapper.addView(mediaGrid)
        }

        // Стикеры (внутри облачка, когда есть текст или другие вложения)
        for (sticker in stickers) {
            val dm = context.resources.displayMetrics
            val stickerSizePx = (stickerSizeDp * dm.density + 0.5f).toInt()
            val cornerRadiusPx = 15 * dm.density
            val stickerView = ShapeableImageView(context).apply {
                layoutParams = android.widget.LinearLayout.LayoutParams(stickerSizePx, stickerSizePx).apply {
                    topMargin = (4 * dm.density + 0.5f).toInt()
                    bottomMargin = (4 * dm.density + 0.5f).toInt()
                }
                scaleType = ImageView.ScaleType.FIT_CENTER
                shapeAppearanceModel = ShapeAppearanceModel.builder()
                    .setAllCorners(CornerFamily.ROUNDED, cornerRadiusPx)
                    .build()
            }
            loadStickerImage(stickerView, sticker)
            wrapper.addView(stickerView)
        }

        // Audio rows
        for (audio in audios) {
            val audioView = inflateAudioRow(container, audio, isSentByMe)
            wrapper.addView(audioView)
        }

        // Document rows
        for (doc in docs) {
            val docView = inflateDocRow(container, doc, isSentByMe)
            wrapper.addView(docView)
        }

        container.addView(wrapper)
    }

    // ─── Media Grid (row-based, matching WPF MultiImageGrid) ────────────────

    /**
     * Строит медиа-сетку из рядов с разным числом ячеек.
     * Ячейки квадратные, между ними отступ 2 dp.
     */
    private fun buildMediaGrid(
        context: android.content.Context,
        mediaItems: List<Shared.MessageAttachment>,
        maxWidth: Int,
        sourceMessageId: Long?
    ): View {
        val dm = context.resources.displayMetrics
        val spacingPx = (2 * dm.density + 0.5f).toInt()
        val capped = mediaItems.take(10)
        val layout = determineLayout(capped.size)

        val column = android.widget.LinearLayout(context).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            layoutParams = android.widget.LinearLayout.LayoutParams(
                maxWidth,
                android.widget.LinearLayout.LayoutParams.WRAP_CONTENT
            )
        }

        var itemIndex = 0
        for ((rowIdx, itemsInRow) in layout.withIndex()) {
            val totalSpacing = spacingPx * (itemsInRow - 1)
            val cellWidth = (maxWidth - totalSpacing) / itemsInRow

            // Высота ряда — по усреднённому соотношению сторон картинок этого ряда,
            // считается от ширины одной ячейки (cellWidth), а не всего ряда.
            val rowRatios = capped.subList(itemIndex, (itemIndex + itemsInRow).coerceAtMost(capped.size))
                .mapNotNull { attachment ->
                    val imgW = attachment.imageWidth
                    val imgH = attachment.imageHeight
                    if (imgW > 0 && imgH > 0) imgW.toFloat() / imgH.toFloat() else null
                }
            val avgRatio = if (rowRatios.isNotEmpty()) rowRatios.average().toFloat() else 1f
            val cellHeight = (cellWidth / avgRatio).toInt().coerceIn(cellWidth / 3, cellWidth * 2)

            val row = android.widget.LinearLayout(context).apply {
                orientation = android.widget.LinearLayout.HORIZONTAL
                layoutParams = android.widget.LinearLayout.LayoutParams(maxWidth, cellHeight).apply {
                    if (rowIdx > 0) topMargin = spacingPx
                }
            }

            for (col in 0 until itemsInRow) {
                if (itemIndex >= capped.size) break
                val attachment = capped[itemIndex]

                val cellView = LayoutInflater.from(context)
                    .inflate(R.layout.item_attachment_media_cell, row, false)
                cellView.layoutParams = android.widget.LinearLayout.LayoutParams(cellWidth, cellHeight).apply {
                    if (col > 0) marginStart = spacingPx
                }

                bindMediaCell(cellView, attachment, capped, itemIndex, sourceMessageId)
                row.addView(cellView)
                itemIndex++
            }

            column.addView(row)
        }

        return column
    }

    /**
     * Строит медиа-сетку из локальных URI (оптимистичное сообщение, до загрузки на сервер).
     * Использует тот же ряд-алгоритм и квадратные ячейки, что и [buildMediaGrid].
     */
    private fun buildLocalMediaGrid(
        context: android.content.Context,
        uris: List<Uri>,
        maxWidth: Int
    ): View {
        val dm = context.resources.displayMetrics
        val spacingPx = (2 * dm.density + 0.5f).toInt()
        val capped = uris.take(10)
        val layout = determineLayout(capped.size)
        val isSingle = capped.size == 1

        val column = android.widget.LinearLayout(context).apply {
            orientation = android.widget.LinearLayout.VERTICAL
            layoutParams = android.widget.LinearLayout.LayoutParams(
                maxWidth,
                android.widget.LinearLayout.LayoutParams.WRAP_CONTENT
            )
        }

        var itemIndex = 0
        for ((rowIdx, itemsInRow) in layout.withIndex()) {
            val totalSpacing = spacingPx * (itemsInRow - 1)
            val cellWidth = (maxWidth - totalSpacing) / itemsInRow
            val cellHeight = if (isSingle) (cellWidth * 0.75f).toInt() else cellWidth

            val row = android.widget.LinearLayout(context).apply {
                orientation = android.widget.LinearLayout.HORIZONTAL
                layoutParams = android.widget.LinearLayout.LayoutParams(maxWidth, cellHeight).apply {
                    if (rowIdx > 0) topMargin = spacingPx
                }
            }

            for (col in 0 until itemsInRow) {
                if (itemIndex >= capped.size) break
                val uri = capped[itemIndex]
                val cellView = LayoutInflater.from(context)
                    .inflate(R.layout.item_attachment_media_cell, row, false)
                cellView.layoutParams = android.widget.LinearLayout.LayoutParams(cellWidth, cellHeight).apply {
                    if (col > 0) marginStart = spacingPx
                }
                val thumbnail = cellView.findViewById<ImageView>(R.id.thumbnailImage)
                val videoOverlay = cellView.findViewById<View>(R.id.videoOverlay)
                val playIcon = cellView.findViewById<ImageView>(R.id.playIcon)
                val isVideo = context.contentResolver.getType(uri)?.startsWith("video/") == true
                videoOverlay.visibility = if (isVideo) View.VISIBLE else View.GONE
                playIcon.visibility = if (isVideo) View.VISIBLE else View.GONE
                thumbnail.load(uri) {
                    crossfade(150)
                    error(R.drawable.ic_image_placeholder)
                }
                row.addView(cellView)
                itemIndex++
            }
            column.addView(row)
        }
        return column
    }

    /**
     * Привязывает данные к ячейке медиа-сетки: превью, оверлей видео, клик.
     */
    private fun bindMediaCell(
        cellView: View,
        attachment: Shared.MessageAttachment,
        allMedia: List<Shared.MessageAttachment>,
        position: Int,
        sourceMessageId: Long?
    ) {
        val thumbnail = cellView.findViewById<ImageView>(R.id.thumbnailImage)
        val videoOverlay = cellView.findViewById<View>(R.id.videoOverlay)
        val playIcon = cellView.findViewById<ImageView>(R.id.playIcon)

        thumbnail.setImageDrawable(null)

        val isVideo = attachment.type == Shared.MessageAttachmentType.VIDEO
        videoOverlay.visibility = if (isVideo) View.VISIBLE else View.GONE
        playIcon.visibility     = if (isVideo) View.VISIBLE else View.GONE

        // Загружаем превью (previewFileId → fileId как fallback)
        val previewFileId = attachment.previewFileId.ifBlank { attachment.fileId }
        val previewUrl    = FileMediaUrl.rewrite(thumbnail.context, attachment.previewUrl)

        val getUrl: suspend () -> String? = if (previewUrl.isNotBlank()) {
            { previewUrl }
        } else {
            { getFileUrl(previewFileId) }
        }

        ImageLoadHelper.loadByFileId(
            imageView = thumbnail,
            fileId = previewFileId,
            getUrlCallback = getUrl,
            onError = { thumbnail.setImageResource(R.drawable.ic_image_placeholder) }
        )

        // Клик: видео → MediaViewerActivity, картинка/gif → ImageViewerActivity
        if (isVideo) {
            cellView.setOnClickListener {
                val ctx = cellView.context
                val cachedPath = FileCache.getFile(attachment.fileId)?.absolutePath
                ctx.startActivity(
                    MediaViewerActivity.createIntent(
                        ctx,
                        attachment.fileId,
                        attachment.fileName.ifBlank { "video" },
                        cachedPath
                    )
                )
            }
        } else {
            cellView.setOnClickListener {
                val ctx = cellView.context
                val imageItems = allMedia.filter {
                    it.type == Shared.MessageAttachmentType.IMAGE ||
                    it.type == Shared.MessageAttachmentType.GIF
                }
                val clickedIndex = imageItems.indexOf(attachment).coerceAtLeast(0)
                val allFileIds    = imageItems.map { it.fileId }
                val allPreviewUrls = imageItems.map { FileMediaUrl.rewrite(ctx, it.previewUrl) }
                ctx.startActivity(
                    ImageViewerActivity.createIntent(
                        ctx,
                        allFileIds,
                        allPreviewUrls,
                        clickedIndex,
                        fileNames = imageItems.map { it.fileName },
                        sourceMessageIds = List(imageItems.size) { sourceMessageId ?: 0L }
                    )
                )
            }

        }
    }

    // ─── Audio Row ────────────────────────────────────────────────────────────

    private fun inflateAudioRow(container: ViewGroup, attachment: Shared.MessageAttachment, isSentByMe: Boolean = false): View {
        val binding = ItemAttachmentAudioBinding.inflate(
            LayoutInflater.from(container.context), container, false
        )
        val context = container.context
        val fileId = attachment.fileId
        val isVoice = attachment.type == Shared.MessageAttachmentType.VOICE
        val fileName = attachment.fileName.ifBlank { if (isVoice) "voice.ogg" else "audio" }

        binding.root.tag = fileId
        binding.downloadButton.tag = fileId
        binding.fileNameText.text = fileName
        binding.fileNameText.visibility = if (isVoice) View.GONE else View.VISIBLE
        binding.voiceWaveform.visibility = if (isVoice) View.VISIBLE else View.GONE
        binding.voiceWaveform.isEnabled = false
        binding.voiceWaveform.resetAmplitudes()
        binding.durationText.text = "0:00"

        if (isSentByMe) {
            val onContainer = resolveOnPrimaryContainerColor(context)
            val onContainerVariant = resolveOnPrimaryContainerVariantColor(context)
            val containerColor = resolvePrimaryContainerColor(context)
            val onContainerCsl = ColorStateList.valueOf(onContainer)
            val onContainerVariantCsl = ColorStateList.valueOf(onContainerVariant)

            binding.fileNameText.setTextColor(onContainer)
            binding.durationText.setTextColor(onContainerVariant)
            binding.voiceWaveform.setColors(onContainer, onContainerVariant)

            val playBg = GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(onContainer)
            }
            binding.playPauseButton.background = playBg
            binding.playPauseButton.imageTintList = ColorStateList.valueOf(containerColor)

            val dlBg = GradientDrawable().apply {
                shape = GradientDrawable.OVAL
                setColor(onContainer)
            }
            binding.downloadButton.background = dlBg
            binding.downloadButton.imageTintList = ColorStateList.valueOf(containerColor)

            binding.audioSeekBar.thumbTintList = onContainerCsl
            binding.audioSeekBar.progressTintList = onContainerCsl
            binding.audioSeekBar.progressBackgroundTintList = onContainerVariantCsl
            binding.downloadProgressBar.setIndicatorColor(onContainer)
        } else {
            binding.voiceWaveform.setColors(
                resolveThemeColor(context, androidx.appcompat.R.attr.colorPrimary),
                resolveThemeColor(context, com.google.android.material.R.attr.colorOutlineVariant)
            )
        }

        fun updateUiForCached(durationMs: Int = 0) {
            binding.downloadButton.visibility = View.GONE
            binding.downloadProgressBar.visibility = View.GONE
            binding.playPauseButton.isEnabled = true
            binding.playPauseButton.alpha = 1f

            if (isVoice) {
                binding.fileNameText.visibility = View.GONE
                binding.voiceWaveform.visibility = View.VISIBLE
                binding.voiceWaveform.isEnabled = true
                binding.audioSeekBar.visibility = View.GONE
            } else {
                binding.fileNameText.visibility = View.VISIBLE
                binding.voiceWaveform.visibility = View.GONE
                binding.audioSeekBar.visibility = View.VISIBLE
                binding.audioSeekBar.isEnabled = true
            }

            if (durationMs > 0) {
                binding.durationText.text = formatAudioTime(durationMs.toLong())
            }
        }

        fun updateUiForNotCached() {
            binding.downloadButton.visibility = View.VISIBLE
            binding.downloadButton.isEnabled = true
            binding.downloadButton.alpha = 1f
            binding.downloadProgressBar.visibility = View.GONE
            binding.playPauseButton.isEnabled = false
            binding.playPauseButton.alpha = 0.4f

            if (isVoice) {
                binding.fileNameText.visibility = View.GONE
                binding.voiceWaveform.visibility = View.VISIBLE
                binding.voiceWaveform.isEnabled = false
                binding.audioSeekBar.visibility = View.GONE
            } else {
                binding.fileNameText.visibility = View.VISIBLE
                binding.voiceWaveform.visibility = View.GONE
                binding.audioSeekBar.visibility = View.GONE
            }
        }

        fun updateUiForDownloading() {
            binding.downloadButton.visibility = View.VISIBLE
            binding.downloadButton.isEnabled = false
            binding.downloadButton.alpha = 0.4f
            binding.downloadProgressBar.visibility = View.VISIBLE
            binding.playPauseButton.isEnabled = false
            binding.playPauseButton.alpha = 0.4f
            binding.audioSeekBar.visibility = View.GONE
            if (isVoice) {
                binding.fileNameText.visibility = View.GONE
                binding.voiceWaveform.visibility = View.VISIBLE
                binding.voiceWaveform.isEnabled = false
            }
        }

        fun startDownload(auto: Boolean) {
            if (auto && !voiceAutoDownloads.add(fileId)) {
                updateUiForDownloading()
                return
            }

            updateUiForDownloading()
            binding.downloadProgressBar.progress = 0
            binding.root.tag = fileId
            binding.downloadButton.tag = fileId

            scope.launch {
                val file = downloadToCache(fileId) { progress ->
                    scope.launch(Dispatchers.Main) {
                        if (binding.root.tag == fileId) {
                            binding.downloadProgressBar.progress = progress
                        }
                    }
                }
                withContext(Dispatchers.Main) {
                    if (auto) voiceAutoDownloads.remove(fileId)
                    if (binding.root.tag != fileId) return@withContext

                    if (file != null) {
                        val durationMs = getAudioDuration(file)
                        updateUiForCached(durationMs)
                        loadVoiceWaveform(fileId, file, binding)
                    } else {
                        updateUiForNotCached()
                    }
                }
            }
        }

        val cachedFile = FileCache.getFile(fileId)
        if (cachedFile != null) {
            val durationMs = getAudioDuration(cachedFile)
            updateUiForCached(durationMs)
            loadVoiceWaveform(fileId, cachedFile, binding)
            if (AudioPlayerHelper.isActiveFile(fileId)) {
                updateAudioPlaybackUI(binding, AudioPlayerHelper.isPlaying())
                val duration = AudioPlayerHelper.getDuration()
                if (duration > 0) {
                    val progress = AudioPlayerHelper.getCurrentPosition().toFloat() / duration
                    if (isVoice) binding.voiceWaveform.setProgress(progress) else binding.audioSeekBar.progress = (progress * 1000).toInt()
                }
                if (AudioPlayerHelper.isPlaying()) startAudioProgressPolling(fileId, binding)
            }
        } else {
            updateUiForNotCached()
            if (isVoice && attachment.attachmentSize in 1L..VOICE_AUTO_DOWNLOAD_LIMIT_BYTES) {
                startDownload(auto = true)
            }
        }

        binding.downloadButton.setOnClickListener {
            startDownload(auto = false)
        }

        binding.playPauseButton.setOnClickListener {
            val file = FileCache.getFile(fileId) ?: return@setOnClickListener
            if (AudioPlayerHelper.isActiveFile(fileId)) {
                if (AudioPlayerHelper.isPlaying()) {
                    AudioPlayerHelper.pause()
                    updateAudioPlaybackUI(binding, false)
                } else {
                    AudioPlayerHelper.resume()
                    updateAudioPlaybackUI(binding, true)
                    startAudioProgressPolling(fileId, binding)
                }
            } else {
                AudioPlayerHelper.play(fileId, file, object : AudioCallbacks {
                    override fun onStateChanged(isPlaying: Boolean) {
                        updateAudioPlaybackUI(binding, isPlaying)
                        if (isPlaying) startAudioProgressPolling(fileId, binding)
                    }
                    override fun onProgress(positionMs: Int, durationMs: Int) {}
                    override fun onComplete() {
                        updateAudioPlaybackUI(binding, false)
                        binding.audioSeekBar.progress = 0
                        binding.voiceWaveform.setProgress(0f)
                        binding.durationText.text = formatAudioTime(
                            AudioPlayerHelper.getDuration().toLong()
                        )
                    }
                    override fun onError() {
                        updateAudioPlaybackUI(binding, false)
                    }
                })
            }
        }

        binding.audioSeekBar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar, progress: Int, fromUser: Boolean) {
                if (fromUser && AudioPlayerHelper.isActiveFile(fileId)) {
                    val duration = AudioPlayerHelper.getDuration()
                    if (duration > 0) {
                        AudioPlayerHelper.seekTo((progress.toLong() * duration / 1000L).toInt())
                    }
                }
            }
            override fun onStartTrackingTouch(seekBar: SeekBar) {}
            override fun onStopTrackingTouch(seekBar: SeekBar) {}
        })

        binding.voiceWaveform.onSeekRequested = { progress ->
            if (AudioPlayerHelper.isActiveFile(fileId)) {
                val duration = AudioPlayerHelper.getDuration()
                if (duration > 0) {
                    AudioPlayerHelper.seekTo((progress * duration).toInt())
                }
            }
        }

        binding.root.setOnLongClickListener { view ->
            showAudioContextMenu(view, context, fileId, fileName, binding, isVoice)
            true
        }

        return binding.root
    }
    private fun showAudioContextMenu(
        anchor: View,
        context: Context,
        fileId: String,
        fileName: String,
        binding: ItemAttachmentAudioBinding,
        isVoice: Boolean
    ) {
        val popup = PopupMenu(context, anchor)
        val menuInflater = popup.menuInflater
        menuInflater.inflate(R.menu.menu_audio_attachment, popup.menu)

        val isCached = FileCache.hasFile(fileId)
        popup.menu.findItem(R.id.action_delete_from_cache).isVisible = isCached

        popup.setOnMenuItemClickListener { menuItem ->
            when (menuItem.itemId) {
                R.id.action_save_audio -> {
                    if (FileCache.hasFile(fileId)) {
                        val cachedFile = FileCache.getFile(fileId)
                        if (cachedFile != null) {
                            saveFileToDownloads(context, cachedFile, fileName)
                        }
                    } else {
                        Toast.makeText(context, R.string.audio_download_required, Toast.LENGTH_SHORT).show()
                    }
                    true
                }
                R.id.action_delete_from_cache -> {
                    if (AudioPlayerHelper.isActiveFile(fileId)) {
                        AudioPlayerHelper.stop()
                    }
                    FileCache.deleteFile(fileId)
                    voiceWaveformCache.remove(fileId)
                    binding.downloadButton.visibility = View.VISIBLE
                    binding.downloadButton.isEnabled = true
                    binding.downloadButton.alpha = 1f
                    binding.downloadProgressBar.visibility = View.GONE
                    binding.audioSeekBar.visibility = View.GONE
                    binding.voiceWaveform.resetAmplitudes()
                    binding.voiceWaveform.visibility = if (isVoice) View.VISIBLE else View.GONE
                    binding.voiceWaveform.isEnabled = false
                    binding.fileNameText.visibility = if (isVoice) View.GONE else View.VISIBLE
                    binding.playPauseButton.isEnabled = false
                    binding.playPauseButton.alpha = 0.4f
                    binding.durationText.text = "0:00"
                    Toast.makeText(context, R.string.audio_removed_from_cache, Toast.LENGTH_SHORT).show()
                    true
                }
                else -> false
            }
        }
        popup.show()
    }
    private fun getAudioDuration(file: File): Int {
        return try {
            val retriever = MediaMetadataRetriever()
            retriever.setDataSource(file.absolutePath)
            val duration = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION)?.toIntOrNull() ?: 0
            retriever.release()
            duration
        } catch (e: Exception) {
            0
        }
    }

    private fun loadVoiceWaveform(
        fileId: String,
        file: File,
        binding: ItemAttachmentAudioBinding
    ) {
        if (binding.voiceWaveform.visibility != View.VISIBLE) return

        val cached = voiceWaveformCache[fileId]
        if (cached != null) {
            binding.voiceWaveform.setAmplitudes(cached)
            return
        }

        binding.voiceWaveform.resetAmplitudes()
        scope.launch {
            val waveform = withContext(Dispatchers.IO) {
                AudioWaveformExtractor.extract(file)
            }
            withContext(Dispatchers.Main) {
                if (binding.root.tag == fileId) {
                    voiceWaveformCache[fileId] = waveform
                    binding.voiceWaveform.setAmplitudes(waveform)
                }
            }
        }
    }
    private fun updateAudioPlaybackUI(
        binding: ItemAttachmentAudioBinding,
        isPlaying: Boolean
    ) {
        binding.playPauseButton.setImageResource(
            if (isPlaying) R.drawable.ic_pause else R.drawable.ic_play_arrow
        )
    }

    private fun startAudioProgressPolling(fileId: String, binding: ItemAttachmentAudioBinding) {
        val handler = Handler(Looper.getMainLooper())
        val runnable = object : Runnable {
            override fun run() {
                if (binding.root.tag != fileId) return
                if (!AudioPlayerHelper.isActiveFile(fileId)) return
                if (!AudioPlayerHelper.isPlaying()) return
                val pos = AudioPlayerHelper.getCurrentPosition()
                val dur = AudioPlayerHelper.getDuration()
                if (dur > 0) {
                    val progress = (pos.toFloat() / dur).coerceIn(0f, 1f)
                    if (binding.voiceWaveform.visibility == View.VISIBLE) {
                        binding.voiceWaveform.setProgress(progress)
                    } else {
                        binding.audioSeekBar.progress = (progress * 1000).toInt()
                    }
                    binding.durationText.text = binding.root.context.getString(
                        R.string.audio_position,
                        formatAudioTime(pos.toLong()),
                        formatAudioTime(dur.toLong())
                    )
                }
                handler.postDelayed(this, 250)
            }
        }
        handler.post(runnable)
    }
    // ─── Video Row ────────────────────────────────────────────────────────────

    private fun inflateVideoRow(container: ViewGroup, attachment: Shared.MessageAttachment): View {
        val binding = ItemAttachmentVideoBinding.inflate(
            LayoutInflater.from(container.context), container, false
        )
        val fileId = attachment.fileId
        val fileName = attachment.fileName.ifBlank { "video" }

        // Load thumbnail
        val thumbnailFileId = attachment.previewFileId.ifBlank { "" }
        val thumbnailUrl = FileMediaUrl.rewrite(binding.root.context, attachment.previewUrl)

        if (thumbnailUrl.isNotBlank()) {
            ImageLoadHelper.loadByFileId(
                imageView = binding.videoThumbnail,
                fileId = thumbnailFileId.ifBlank { fileId },
                getUrlCallback = { thumbnailUrl },
                onError = { binding.videoThumbnail.setImageResource(R.drawable.ic_image_placeholder) }
            )
        } else if (thumbnailFileId.isNotBlank()) {
            ImageLoadHelper.loadByFileId(
                imageView = binding.videoThumbnail,
                fileId = thumbnailFileId,
                getUrlCallback = { getFileUrl(thumbnailFileId) },
                onError = { binding.videoThumbnail.setImageResource(R.drawable.ic_image_placeholder) }
            )
        }

        if (FileCache.hasFile(fileId)) {
            binding.videoDownloadButton.visibility = View.GONE
            binding.videoPlayButton.alpha = 1f
            binding.videoPlayButton.isEnabled = true
        } else {
            binding.videoDownloadButton.visibility = View.VISIBLE
            binding.videoPlayButton.alpha = 0.4f
            binding.videoPlayButton.isEnabled = false
        }

        // Download button
        binding.videoDownloadButton.setOnClickListener {
            binding.videoDownloadButton.visibility = View.GONE
            binding.videoDownloadProgress.visibility = View.VISIBLE

            scope.launch {
                val file = downloadToCache(fileId) { _ -> }
                withContext(Dispatchers.Main) {
                    binding.videoDownloadProgress.visibility = View.GONE
                    if (file != null) {
                        binding.videoPlayButton.alpha = 1f
                        binding.videoPlayButton.isEnabled = true
                    } else {
                        binding.videoDownloadButton.visibility = View.VISIBLE
                    }
                }
            }
        }

        // Play button
        binding.videoPlayButton.setOnClickListener {
            val cachedPath = FileCache.getFile(fileId)?.absolutePath
            val intent = MediaViewerActivity.createIntent(
                binding.root.context, fileId, fileName, cachedPath
            )
            binding.root.context.startActivity(intent)
        }

        return binding.root
    }

    // ─── Document Row ─────────────────────────────────────────────────────────

    private fun inflateDocRow(container: ViewGroup, attachment: Shared.MessageAttachment, isSentByMe: Boolean = false): View {
        val binding = ItemAttachmentDocumentBinding.inflate(
            LayoutInflater.from(container.context), container, false
        )
        val context = container.context
        val fileId = attachment.fileId
        val fileName = attachment.fileName.ifBlank { context.getString(R.string.attachment_file) }
        val previewUrl = FileMediaUrl.rewrite(context, attachment.previewUrl)

        binding.docFileName.text = fileName
        binding.docFileSize.text = formatFileSize(context, attachment.attachmentSize)
        binding.docDownloadProgress.visibility = View.GONE

        // Перекраска для отправленных сообщений (контраст на primaryContainer)
        if (isSentByMe) {
            val onContainer = resolveOnPrimaryContainerColor(context)
            val onContainerVariant = resolveOnPrimaryContainerVariantColor(context)
            val onContainerCsl = ColorStateList.valueOf(onContainer)

            binding.docFileIcon.imageTintList = onContainerCsl
            binding.docFileName.setTextColor(onContainer)
            binding.docFileSize.setTextColor(onContainerVariant)
            binding.docDownloadButton.imageTintList = onContainerCsl
            binding.docOpenButton.imageTintList = onContainerCsl
            binding.docDownloadProgress.setIndicatorColor(onContainer)
        }

        fun updateUiForCached() {
            binding.docDownloadButton.visibility = View.GONE
            binding.docOpenButton.visibility = View.VISIBLE
            binding.docDownloadProgress.visibility = View.GONE
        }

        fun updateUiForNotCached() {
            binding.docDownloadButton.visibility = View.VISIBLE
            binding.docOpenButton.visibility = View.GONE
            binding.docDownloadProgress.visibility = View.GONE
            binding.docDownloadButton.isEnabled = true
        }

        fun updateUiForDownloading() {
            binding.docDownloadButton.visibility = View.VISIBLE
            binding.docDownloadButton.isEnabled = false
            binding.docOpenButton.visibility = View.GONE
            binding.docDownloadProgress.visibility = View.VISIBLE
            binding.docDownloadProgress.progress = 0
        }

        if (FileCache.hasFile(fileId)) {
            updateUiForCached()
        } else {
            updateUiForNotCached()
        }

        // Download button
        binding.docDownloadButton.setOnClickListener {
            updateUiForDownloading()

            scope.launch {
                val file = downloadToCache(fileId) { progress ->
                    scope.launch(Dispatchers.Main) {
                        binding.docDownloadProgress.progress = progress
                    }
                }
                withContext(Dispatchers.Main) {
                    if (file != null) {
                        updateUiForCached()
                    } else {
                        updateUiForNotCached()
                    }
                }
            }
        }

        // Open button
        binding.docOpenButton.setOnClickListener {
            val cachedFile = FileCache.getFile(fileId) ?: return@setOnClickListener
            openFile(context, cachedFile, fileName, fileId, previewUrl)
        }

        // Long press → context menu
        binding.root.setOnLongClickListener { view ->
            showDocumentContextMenu(view, context, fileId, fileName, binding)
            true
        }

        return binding.root
    }

    private fun showDocumentContextMenu(
        anchor: View,
        context: Context,
        fileId: String,
        fileName: String,
        binding: ItemAttachmentDocumentBinding
    ) {
        val isCached = FileCache.hasFile(fileId)
        if (!isCached) {
            // Меню сводилось к одному пункту "Удалить из кеша" — для нескачанного файла нет смысла показывать
            return
        }
        val popup = PopupMenu(context, anchor)
        val menuInflater = popup.menuInflater
        menuInflater.inflate(R.menu.menu_document_attachment, popup.menu)

        popup.setOnMenuItemClickListener { menuItem ->
            when (menuItem.itemId) {
                R.id.action_delete_doc_from_cache -> {
                    FileCache.deleteFile(fileId)
                    binding.docDownloadButton.visibility = View.VISIBLE
                    binding.docDownloadButton.isEnabled = true
                    binding.docOpenButton.visibility = View.GONE
                    binding.docDownloadProgress.visibility = View.GONE
                    Toast.makeText(context, R.string.file_removed_from_cache, Toast.LENGTH_SHORT).show()
                    true
                }
                else -> false
            }
        }
        popup.show()
    }

    private fun openFile(context: Context, file: File, fileName: String, fileId: String, previewUrl: String) {
        val mimeType = getMimeType(fileName)

        when {
            isImageFile(fileName) -> {
                // Open in ImageViewerActivity
                val intent = ImageViewerActivity.createIntent(
                    context,
                    listOf(fileId),
                    listOf(previewUrl),
                    0,
                    fileNames = listOf(fileName)
                )
                context.startActivity(intent)
            }
            isVideoFile(fileName) -> {
                // Open in MediaViewerActivity
                val intent = MediaViewerActivity.createIntent(
                    context,
                    fileId,
                    fileName,
                    file.absolutePath
                )
                context.startActivity(intent)
            }
            else -> {
                // Open with system chooser
                try {
                    val uri = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                        FileProvider.getUriForFile(
                            context,
                            "${context.packageName}.fileprovider",
                            file
                        )
                    } else {
                        Uri.fromFile(file)
                    }

                    val intent = Intent(Intent.ACTION_VIEW).apply {
                        setDataAndType(uri, mimeType)
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                        addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
                    }

                    val chooser = Intent.createChooser(intent, context.getString(R.string.open_with))
                    context.startActivity(chooser)
                } catch (e: Exception) {
                    Toast.makeText(context, R.string.file_open_failed, Toast.LENGTH_SHORT).show()
                }
            }
        }
    }

    private fun isImageFile(fileName: String): Boolean {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        return ext in setOf("jpg", "jpeg", "png", "gif", "webp", "bmp", "heic", "heif")
    }

    private fun isVideoFile(fileName: String): Boolean {
        val ext = fileName.substringAfterLast('.', "").lowercase()
        return ext in setOf("mp4", "mkv", "webm", "avi", "mov", "3gp", "flv", "wmv")
    }

    private fun getMimeType(fileName: String): String {
        val ext = fileName.substringAfterLast('.', "")
        return MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) ?: "application/octet-stream"
    }

    private fun saveFileToDownloads(context: Context, sourceFile: File, fileName: String) {
        try {
            val resolver = context.contentResolver
            val contentValues = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, fileName)
                put(MediaStore.Downloads.MIME_TYPE, getMimeType(fileName))
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    put(MediaStore.Downloads.RELATIVE_PATH, "${Environment.DIRECTORY_DOWNLOADS}/BarkFluff")
                    put(MediaStore.Downloads.IS_PENDING, 1)
                }
            }

            val uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, contentValues)

            if (uri != null) {
                resolver.openOutputStream(uri).use { outputStream ->
                    FileInputStream(sourceFile).use { inputStream ->
                        inputStream.copyTo(outputStream!!)
                    }
                }

                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    contentValues.clear()
                    contentValues.put(MediaStore.Downloads.IS_PENDING, 0)
                    resolver.update(uri, contentValues, null, null)
                }

                Toast.makeText(context, R.string.file_saved_to_downloads, Toast.LENGTH_SHORT).show()
            } else {
                Toast.makeText(context, R.string.file_save_failed, Toast.LENGTH_SHORT).show()
            }
        } catch (e: Exception) {
            Toast.makeText(
                context,
                context.getString(R.string.file_save_error, e.message.orEmpty()),
                Toast.LENGTH_SHORT
            ).show()
        }
    }

    // ─── Formatting Helpers ───────────────────────────────────────────────────

    private fun formatTime(timestampMillis: Long): String {
        if (timestampMillis <= 0) return ""
        return SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date(timestampMillis))
    }

    private fun formatAudioTime(ms: Long): String {
        if (ms <= 0) return "0:00"
        val totalSec = ms / 1000
        val min = totalSec / 60
        val sec = totalSec % 60
        return "%d:%02d".format(min, sec)
    }

    private fun formatFileSize(context: android.content.Context, bytes: Long): String {
        return when {
            bytes <= 0 -> ""
            bytes < 1024 -> context.getString(R.string.file_size_bytes, bytes)
            bytes < 1024 * 1024 -> context.getString(R.string.file_size_kilobytes, bytes / 1024.0)
            bytes < 1024 * 1024 * 1024 -> context.getString(
                R.string.file_size_megabytes,
                bytes / (1024.0 * 1024.0)
            )
            else -> context.getString(
                R.string.file_size_gigabytes,
                bytes / (1024.0 * 1024.0 * 1024.0)
            )
        }
    }

    // ─── DiffCallback ─────────────────────────────────────────────────────────

    class MessageDiffCallback : DiffUtil.ItemCallback<MessageItem>() {
        override fun areItemsTheSame(oldItem: MessageItem, newItem: MessageItem): Boolean {
            if (oldItem.type == MessageType.FOOTER && newItem.type == MessageType.FOOTER) return true
            if (oldItem.type == MessageType.FOOTER || newItem.type == MessageType.FOOTER) return false
            if (oldItem.type != newItem.type) return false
            if (oldItem.type == MessageType.UNREAD_SEPARATOR) return true
            return oldItem.messageId == newItem.messageId
        }
        override fun areContentsTheSame(oldItem: MessageItem, newItem: MessageItem) =
            if (oldItem.type == MessageType.FOOTER && newItem.type == MessageType.FOOTER) true
            else oldItem == newItem
    }
}

// ─── Data Classes & Enums ─────────────────────────────────────────────────────

enum class MessageType { MESSAGE, DATE_SEPARATOR, UNREAD_SEPARATOR, FOOTER, SYSTEM }

data class MessageItem(
    val messageId: Long,
    val senderId: Long,
    val senderName: String? = null,
    val senderAvatarFileId: String? = null,
    val text: String,
    val timestamp: Long,
    val attachments: List<Shared.MessageAttachment>,
    /**
     * Цитируемое сообщение, если это ответ. Приходит с сервера явным полем — раньше reply и
     * forward различались догадкой «есть ли оригинал в загруженной истории», из-за чего ответ
     * превращался в пересылку, стоило прокрутить чат.
     */
    val replyTo: Shared.ReplyInfo? = null,
    val readStatus: ReadStatus = ReadStatus.NONE,
    val type: MessageType = MessageType.MESSAGE,
    val dateText: String = "",
    val isEdited: Boolean = false,
    /** Локальный clientMessageId оптимистичных сообщений (для трекинга SENDING→SENT перехода). null для серверных. */
    val localId: String? = null,
    /** Прогресс загрузки медиа 0..100. null если не идёт upload. */
    val uploadProgress: Int? = null,
    /** Локальные URI медиа для превью оптимистичного сообщения (пока вложения ещё не загружены на сервер). */
    val localPreviewUris: List<android.net.Uri> = emptyList()
) {
    companion object {
        fun createDateSeparator(dateText: String) = MessageItem(
            messageId = 0, senderId = 0, text = "", timestamp = 0,
            attachments = emptyList(), type = MessageType.DATE_SEPARATOR, dateText = dateText
        )

        fun createUnreadSeparator(label: String) = MessageItem(
            messageId = -2, senderId = 0, text = "", timestamp = 0,
            attachments = emptyList(), type = MessageType.UNREAD_SEPARATOR,
            dateText = label
        )
    }
}

/**
 * Статус доставки/прочтения исходящего сообщения. Расширен под M3 Expressive feedback:
 * - NONE — для входящих и системных
 * - SENDING — оптимистичный item, отправка в процессе (часы)
 * - SENT — отправлено на сервер, ACK получен (одна галочка)
 * - DELIVERED — доставлено получателю (двойная outline)
 * - READ — прочитано получателем (двойная filled, primary tint)
 * - FAILED — ошибка при отправке (восклицательный знак, tap to retry)
 */
enum class ReadStatus { NONE, SENDING, SENT, DELIVERED, READ, FAILED }
