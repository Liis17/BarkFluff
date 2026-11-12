package com.barkfluff.client.adapter

import android.animation.Animator
import android.animation.AnimatorListenerAdapter
import android.animation.ValueAnimator
import android.content.Context
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.view.animation.PathInterpolator
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.SeekBar
import androidx.core.view.updateLayoutParams
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import coil.imageLoader
import coil.load
import coil.dispose
import com.barkfluff.client.R
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ItemAttachmentAudioBinding
import com.google.android.material.card.MaterialCardView
import com.google.android.material.imageview.ShapeableImageView
import com.google.android.material.color.MaterialColors
import com.google.android.material.shape.CornerFamily
import com.google.android.material.shape.ShapeAppearanceModel
import com.barkfluff.client.databinding.ItemAttachmentDocumentBinding
import com.barkfluff.client.databinding.ItemAttachmentVideoBinding
import com.barkfluff.client.databinding.ItemMessageDateSeparatorBinding
import com.barkfluff.client.databinding.ItemMessageReceivedBinding
import com.barkfluff.client.databinding.ItemMessageSentBinding
import com.barkfluff.client.databinding.ViewMessageQuoteBinding
import com.barkfluff.client.audio.AudioPlayback
import com.barkfluff.client.audio.AudioTrack
import com.barkfluff.client.utils.AudioWaveformExtractor
import com.barkfluff.client.utils.ImageCompressor
import com.barkfluff.client.cache.OutgoingMessageState
import com.barkfluff.client.cache.OutgoingAttachmentKind
import com.barkfluff.client.send.OutgoingAttachmentUi
import com.barkfluff.client.send.OutgoingAttachmentUploadState
import com.barkfluff.client.utils.ImageLoadHelper
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.view.M3ContextMenu
import barkfluff.shared.Shared
import android.content.res.ColorStateList
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Адаптер для отображения сообщений в чате с разделителями дат.
 * Поддерживает четыре типа view: отправленные сообщения, полученные сообщения,
 * разделители дат и разделитель непрочитанных сообщений.
 */
class MessageAdapter(
    private val playback: AudioPlayback,
    private val playbackChatId: String = "",
    private val playbackChatTitle: String = "",
    private val playbackOtherUserId: Long = 0L,
    private val currentUserId: Long,
    private val isGroupChat: Boolean,
    private val attachmentLoader: AttachmentLoader = EmptyAttachmentLoader,
    /** Закругление облачков сообщений в dp (0..30). */
    var messageCornerRadiusDp: Int = 28,
    /** Размер стикеров в чате в dp. */
    var stickerSizeDp: Int = GlobalParam.DEFAULT_STICKER_SIZE_DP,
    /** Единственная граница событий строки; adapter не знает о навигации и domain side effects. */
    private val eventSink: MessageRowEventSink? = null,
    private val autoDownloadViews: AttachmentAutoDownloadViews? = null,
) : ListAdapter<MessageItem, RecyclerView.ViewHolder>(MessageDiffCallback()) {

    private val audioPlaybackController = AudioPlaybackController(playback)
    private val viewOperations = ViewBoundOperationController()
    private val contentRenderer = MessageContentRenderer()
    private val attachmentRenderer = MessageAttachmentRenderer(attachmentLoader)
    private val audioRenderer = MessageAudioRenderer(audioPlaybackController)

    /** Возвращает MessageItem по позиции для обработчика свайпа (ItemTouchHelper). null если позиция вне диапазона или не сообщение. */
    fun isAttachmentCached(fileId: String): Boolean = attachmentLoader.hasCached(fileId)

    /** Удаляет аудио из кеша; [row] — видимая строка вложения, её сразу переводим в состояние «не скачано». */
    fun removeCachedAudio(fileId: String, isVoice: Boolean, row: View?) {
        if (audioPlaybackController.isActiveFile(fileId)) audioPlaybackController.stop()
        attachmentLoader.deleteCached(fileId)
        audioPlaybackController.remove(fileId)
        val binding = row?.let(ItemAttachmentAudioBinding::bind) ?: return
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
        binding.durationText.text = formatAudioTime(0L)
    }

    fun getMessageAt(position: Int): MessageItem? {
        if (position < 0 || position >= itemCount) return null
        val item = getItem(position)
        return if (item.type == MessageType.MESSAGE) item else null
    }

    /**
     * Показывает/скрывает индикатор выделения и переключает его иконку по состоянию выбора.
     * Для [contentColumn] (только у входящих — у исходящих под индикатор уже есть готовый
     * отступ) сдвигает содержимое вправо через translationX, освобождая место слева.
     */
    private fun bindSelectionIndicator(root: View, indicator: ImageView, contentColumn: View?, item: MessageItem) {
        val isSelectable = item.selectionEnabled && item.type == MessageType.MESSAGE
        val density = root.resources.displayMetrics.density
        val hiddenOffset = -12f * density
        indicator.animate().cancel()
        if (isSelectable) {
            val wasVisible = indicator.visibility == View.VISIBLE
            if (!wasVisible) {
                indicator.visibility = View.VISIBLE
                indicator.alpha = 0f
                indicator.translationX = hiddenOffset
            }
            val previousSelection = indicator.getTag(R.id.tag_message_selection_checked) as? Boolean
            indicator.setImageResource(
                if (item.isSelected) R.drawable.ic_action_select else R.drawable.ic_circle_outline
            )
            indicator.setTag(R.id.tag_message_selection_checked, item.isSelected)
            if (!ValueAnimator.areAnimatorsEnabled()) {
                indicator.alpha = 1f
                indicator.translationX = 0f
                indicator.scaleX = 1f
                indicator.scaleY = 1f
            } else {
                indicator.animate()
                    .alpha(1f)
                    .translationX(0f)
                    .setDuration(SELECTION_ANIMATION_MS)
                    .setInterpolator(SELECTION_INTERPOLATOR)
                    .start()
                if (wasVisible && previousSelection != null && previousSelection != item.isSelected) {
                    indicator.scaleX = 0.72f
                    indicator.scaleY = 0.72f
                    indicator.animate()
                        .scaleX(1f)
                        .scaleY(1f)
                        .setDuration(CHECK_ANIMATION_MS)
                        .setInterpolator(SELECTION_INTERPOLATOR)
                        .start()
                }
            }
            indicator.setOnClickListener {
                eventSink?.onSelectionToggle(item.messageId)
            }
        } else {
            indicator.setOnClickListener(null)
            if (indicator.visibility == View.VISIBLE && ValueAnimator.areAnimatorsEnabled()) {
                indicator.animate()
                    .alpha(0f)
                    .translationX(hiddenOffset)
                    .setDuration(SELECTION_ANIMATION_MS)
                    .setInterpolator(SELECTION_INTERPOLATOR)
                    .withEndAction {
                        indicator.visibility = View.GONE
                        indicator.alpha = 1f
                        indicator.translationX = 0f
                    }
                    .start()
            } else {
                indicator.visibility = View.GONE
                indicator.alpha = 1f
                indicator.translationX = 0f
            }
        }
        if (contentColumn != null) {
            val targetTranslation = if (isSelectable) SELECTION_SHIFT_DP * density else 0f
            contentColumn.animate().cancel()
            if (ValueAnimator.areAnimatorsEnabled()) {
                contentColumn.animate()
                    .translationX(targetTranslation)
                    .setDuration(SELECTION_ANIMATION_MS)
                    .setInterpolator(SELECTION_INTERPOLATOR)
                    .start()
            } else {
                contentColumn.translationX = targetTranslation
            }
        }
        bindSelectionBackground(root, item.isSelected && isSelectable)
    }

    private fun bindSelectionBackground(root: View, selected: Boolean) {
        val existing = root.getTag(R.id.tag_message_selection_background) as? ColorDrawable
        val oldAnimator = root.getTag(R.id.tag_message_selection_animator) as? ValueAnimator
        root.setTag(R.id.tag_message_selection_animator, null)
        oldAnimator?.cancel()
        if (!selected && existing == null) return

        val drawable = existing ?: ColorDrawable().also {
            it.alpha = 0
            root.setTag(R.id.tag_message_selection_background, it)
            root.foreground = it
        }
        drawable.color = MaterialColors.getColor(root, androidx.appcompat.R.attr.colorPrimary)
        if (root.foreground !== drawable) root.foreground = drawable
        val targetAlpha = if (selected) SELECTION_BACKGROUND_ALPHA else 0
        if (!ValueAnimator.areAnimatorsEnabled()) {
            drawable.alpha = targetAlpha
            if (!selected) {
                root.foreground = null
                root.setTag(R.id.tag_message_selection_background, null)
            }
            return
        }
        val animator = ValueAnimator.ofInt(drawable.alpha, targetAlpha).apply {
            duration = SELECTION_ANIMATION_MS
            interpolator = SELECTION_INTERPOLATOR
            addUpdateListener { drawable.alpha = it.animatedValue as Int }
            addListener(object : AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: Animator) {
                    if (root.getTag(R.id.tag_message_selection_animator) !== animation) return
                    root.setTag(R.id.tag_message_selection_animator, null)
                    if (!selected) {
                        root.foreground = null
                        root.setTag(R.id.tag_message_selection_background, null)
                    }
                }
            })
        }
        root.setTag(R.id.tag_message_selection_animator, animator)
        animator.start()
    }

    companion object {
        /** Совпадает с layout_marginTop у timeStatusLayout в item_message_sent/received. */
        private const val TIME_STATUS_TOP_MARGIN_DP = 2f
        /** Поднимает строку времени на уровень длительности под волной голосового. */
        private const val VOICE_INLINE_TIME_OVERLAP_DP = 27f
        private const val VIEW_TYPE_SENT = 1
        private const val VIEW_TYPE_RECEIVED = 2
        private const val VIEW_TYPE_DATE_SEPARATOR = 3
        private const val VIEW_TYPE_UNREAD_SEPARATOR = 4
        private const val VIEW_TYPE_FOOTER = 5
        private const val VIEW_TYPE_SYSTEM = 6

        /**
         * Payload для случая, когда подгрузился кэш участников группы: меняются только
         * имя и мини-аватар отправителя, полный ребинд (с перезапуском загрузки вложений)
         * не нужен.
         */
        const val PAYLOAD_SENDER_INFO = "sender_info"

        /** Only outgoing upload indicators/status changed; media views stay bound and keep previews. */
        const val PAYLOAD_OUTGOING_PROGRESS = 1 shl 0
        /** Selection state changed; only the indicator, row tint and inbound content offset are rebound. */
        const val PAYLOAD_SELECTION = 1 shl 1

        internal data class MergedPayload(val flags: Int, val senderInfoChanged: Boolean)

        internal fun mergePayloads(payloads: List<Any>): MergedPayload? {
            if (payloads.isEmpty()) return null
            var flags = 0
            var senderInfoChanged = false
            val knownFlags = PAYLOAD_OUTGOING_PROGRESS or PAYLOAD_SELECTION
            payloads.forEach { payload ->
                when (payload) {
                    PAYLOAD_SENDER_INFO -> senderInfoChanged = true
                    is Int -> {
                        if (payload and knownFlags.inv() != 0) return null
                        flags = flags or payload
                    }
                    else -> return null
                }
            }
            return if (flags == 0 && !senderInfoChanged) null
            else MergedPayload(flags = flags, senderInfoChanged = senderInfoChanged)
        }

        /** «Хвостик» последнего пузыря в серии. */
        private const val BUBBLE_TAIL_CORNER_DP = 8f
        /** Отступ между сериями сообщений. */
        private const val BUBBLE_GROUP_GAP_DP = 10
        /** Отступ между сообщениями внутри одной серии. */
        private const val BUBBLE_INNER_GAP_DP = 3
        /** Без скругления (для мест, где форма пузыря не пробрасывается, напр. вложения в forward-цитате). */
        private val ZERO_CORNERS = floatArrayOf(0f, 0f, 0f, 0f)
        /** Сдвиг contentColumn у входящих в режиме выделения: 24dp индикатор + 8dp зазор. */
        private const val SELECTION_SHIFT_DP = 32f
        private const val SELECTION_ANIMATION_MS = 180L
        private const val CHECK_ANIMATION_MS = 150L
        private const val SELECTION_BACKGROUND_ALPHA = 36
        private val SELECTION_INTERPOLATOR = PathInterpolator(0.2f, 0f, 0f, 1f)

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
        autoDownloadViews?.recycleTree(holder.itemView)
        clearMediaImages(holder.itemView)
        viewOperations.cancelTree(holder.itemView)
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
        if (payloads.isEmpty()) {
            super.onBindViewHolder(holder, position, payloads)
            return
        }

        val mergedPayload = mergePayloads(payloads)
        if (mergedPayload == null) {
            super.onBindViewHolder(holder, position, payloads)
            return
        }

        val item = getItem(position)
        if (mergedPayload.senderInfoChanged && holder is ReceivedMessageViewHolder) {
            holder.bindSenderInfo(item)
        }
        if (mergedPayload.flags and PAYLOAD_OUTGOING_PROGRESS != 0 && holder is SentMessageViewHolder) {
            holder.bindOutgoingProgress(item)
        }
        if (mergedPayload.flags and PAYLOAD_SELECTION != 0) {
            when (holder) {
                is SentMessageViewHolder -> holder.bindSelection(item)
                is ReceivedMessageViewHolder -> holder.bindSelection(item)
            }
        }
    }

    override fun onViewRecycled(holder: RecyclerView.ViewHolder) {
        autoDownloadViews?.recycleTree(holder.itemView)
        clearMediaImages(holder.itemView)
        viewOperations.cancelTree(holder.itemView)
        super.onViewRecycled(holder)
    }

    override fun onDetachedFromRecyclerView(recyclerView: RecyclerView) {
        autoDownloadViews?.detach(recyclerView)
        viewOperations.cancelAll()
        super.onDetachedFromRecyclerView(recyclerView)
    }

    override fun onAttachedToRecyclerView(recyclerView: RecyclerView) {
        super.onAttachedToRecyclerView(recyclerView)
        autoDownloadViews?.attach(recyclerView)
    }

    override fun onViewAttachedToWindow(holder: RecyclerView.ViewHolder) {
        super.onViewAttachedToWindow(holder)
        autoDownloadViews?.refresh()
    }

    private fun clearMediaImages(root: View) {
        if (root is ImageView && root.id == R.id.thumbnailImage) {
            root.dispose()
            root.tag = null
        }
        if (root is ViewGroup) for (index in 0 until root.childCount) clearMediaImages(root.getChildAt(index))
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
    /** Радиусы углов пузыря [topLeft, topRight, bottomRight, bottomLeft] в px, по той же логике, что и облачко. */
    private fun computeBubbleCorners(density: Float, group: GroupPosition, isSentByMe: Boolean): FloatArray {
        val big = messageCornerRadiusDp * density
        val mid = big / 2f
        val small = minOf(BUBBLE_TAIL_CORNER_DP * density, mid)
        return if (isSentByMe) {
            floatArrayOf(
                big,
                if (group.isFirst) big else mid,
                if (group.isLast) small else mid,
                big
            )
        } else {
            floatArrayOf(
                if (group.isFirst) big else mid,
                big,
                big,
                if (group.isLast) small else mid
            )
        }
    }

    /**
     * Радиусы углов одной ячейки медиа-сетки: угол пузыря наследуется только той ячейкой,
     * что физически стоит в соответствующем углу сетки, внутренние стыки ячеек — прямые.
     */
    private fun mediaCellCorners(
        bubbleCorners: FloatArray,
        roundTop: Boolean,
        roundBottom: Boolean,
        isTopRow: Boolean,
        isBottomRow: Boolean,
        isLeftCol: Boolean,
        isRightCol: Boolean
    ): FloatArray = floatArrayOf(
        if (isTopRow && isLeftCol && roundTop) bubbleCorners[0] else 0f,
        if (isTopRow && isRightCol && roundTop) bubbleCorners[1] else 0f,
        if (isBottomRow && isRightCol && roundBottom) bubbleCorners[2] else 0f,
        if (isBottomRow && isLeftCol && roundBottom) bubbleCorners[3] else 0f
    )

    /** Применяет per-corner скругление к превью и видео-оверлею ячейки медиа-сетки. */
    private fun applyMediaCellCorners(cellView: View, corners: FloatArray) {
        val thumbnail = cellView.findViewById<ShapeableImageView>(R.id.thumbnailImage)
        thumbnail.shapeAppearanceModel = ShapeAppearanceModel.builder()
            .setTopLeftCornerSize(corners[0])
            .setTopRightCornerSize(corners[1])
            .setBottomRightCornerSize(corners[2])
            .setBottomLeftCornerSize(corners[3])
            .build()

        cellView.findViewById<View>(R.id.videoOverlay).background = GradientDrawable().apply {
            setColor(0x52000000)
            cornerRadii = floatArrayOf(
                corners[0], corners[0],
                corners[1], corners[1],
                corners[2], corners[2],
                corners[3], corners[3]
            )
        }
    }

    private fun applyBubbleShape(card: MaterialCardView, group: GroupPosition, isSentByMe: Boolean) {
        val density = card.resources.displayMetrics.density
        val c = computeBubbleCorners(density, group, isSentByMe)
        card.shapeAppearanceModel = ShapeAppearanceModel.builder()
            .setTopLeftCornerSize(c[0])
            .setTopRightCornerSize(c[1])
            .setBottomRightCornerSize(c[2])
            .setBottomLeftCornerSize(c[3])
            .build()
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

        fun bind(item: MessageItem, group: GroupPosition) {
            applyGroupSpacing(binding.root, group)
            // Индикатор выделения уже попадает в существующий отступ слева — сдвигать нечего.
            bindSelectionIndicator(binding.root, binding.selectionIndicator, contentColumn = null, item)

            // Клик открывает меню действий только по самому пузырю — тап в пустое
            // место строки (широкий paddingStart для отступа от края экрана) меню не открывает.
            // В режиме выделения клик по пузырю переключает выбор вместо открытия меню.
            val bubbleClickListener = View.OnClickListener { v ->
                if (eventSink?.isSelectionActive() ?: item.selectionEnabled) {
                    eventSink?.onSelectionToggle(item.messageId)
                } else {
                    eventSink?.onMessageActionRequested(v, item)
                }
            }
            binding.messageCard.setOnClickListener(bubbleClickListener)
            binding.stickerContainer.setOnClickListener(bubbleClickListener)

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

                binding.stickerTimeTextView.text = contentRenderer.formatTime(item.timestamp)
                applyDeliveryStatusIcon(binding.stickerReadStatusImageView, item.readStatus, useLightTint = true)
            } else {
                // Обычное сообщение с облачком
                binding.messageCard.visibility = View.VISIBLE
                binding.stickerContainer.visibility = View.GONE
                binding.stickerTimeStatusLayout.visibility = View.GONE

                // Форма пузыря: базовый радиус — из настроек персонализации
                applyBubbleShape(binding.messageCard, group, isSentByMe = true)
                val bubbleCorners = computeBubbleCorners(
                    binding.root.resources.displayMetrics.density, group, isSentByMe = true
                )
                val roundMediaTop = binding.replyQuote.quoteContainer.visibility != View.VISIBLE
                val roundMediaBottom = item.text.isBlank() &&
                    binding.forwardQuotesContainer.visibility != View.VISIBLE

                if (item.text.isNotBlank()) {
                    contentRenderer.renderText(
                        binding.messageMarkdownContainer,
                        binding.messageTextView,
                        item.text
                    )
                    binding.messageTextView.visibility = View.VISIBLE
                } else {
                    contentRenderer.clearText(
                        binding.messageMarkdownContainer,
                        binding.messageTextView
                    )
                }

                binding.timeTextView.text = contentRenderer.formatTime(item.timestamp)
                binding.editedLabelTextView.visibility = if (item.isEdited) View.VISIBLE else View.GONE

                applyDeliveryStatusIcon(binding.readStatusImageView, item.readStatus)

                val hasOutgoingMedia = item.outgoingAttachments.any { it.isOutgoingMedia() }
                val showMediaTimeOverlay = item.text.isBlank() &&
                    (hasOutgoingMedia || item.localPreviewUris.isNotEmpty() || displayedAttachments.isPureMedia())
                binding.timeStatusLayout.visibility = if (showMediaTimeOverlay) View.GONE else View.VISIBLE
                applyVoiceInlineTime(binding.timeStatusLayout, item, displayedAttachments, binding.forwardQuotesContainer)

                if (item.outgoingAttachments.isNotEmpty()) {
                    val mediaWidthPx = if (hasOutgoingMedia) calcMediaWidthPx(binding.root.context) else 0
                    binding.attachmentsContainer.layoutParams = binding.attachmentsContainer.layoutParams.also {
                        it.width = if (mediaWidthPx > 0) mediaWidthPx else ViewGroup.LayoutParams.WRAP_CONTENT
                    }
                    binding.attachmentsContainer.removeAllViews()
                    val (outgoingView, mediaGrid) = buildOutgoingAttachments(
                        binding.attachmentsContainer,
                        item.outgoingAttachments,
                        item,
                        mediaWidthPx,
                        bubbleCorners,
                        roundMediaTop,
                        roundMediaBottom,
                    )
                    binding.attachmentsContainer.addView(outgoingView)
                    if (showMediaTimeOverlay && mediaGrid != null) bindMediaTimeOverlay(mediaGrid, item)
                    binding.attachmentsContainer.visibility = View.VISIBLE
                } else if (item.localPreviewUris.isNotEmpty()) {
                    // Оптимистичное сообщение: вложения ещё не на сервере — рендерим локальные превью.
                    val mediaWidthPx = calcMediaWidthPx(binding.root.context)
                    binding.attachmentsContainer.layoutParams = binding.attachmentsContainer.layoutParams.also {
                        it.width = mediaWidthPx
                    }
                    binding.attachmentsContainer.removeAllViews()
                    binding.attachmentsContainer.addView(
                        buildLocalMediaGrid(
                            binding.root.context, item.localPreviewUris, mediaWidthPx,
                            bubbleCorners, roundMediaTop, roundMediaBottom
                        )
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
                        sourceMessageId = item.messageId,
                        mediaCorners = bubbleCorners,
                        roundMediaTop = roundMediaTop,
                        roundMediaBottom = roundMediaBottom
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

        fun bindOutgoingProgress(item: MessageItem) {
            applyDeliveryStatusIcon(binding.readStatusImageView, item.readStatus)
            binding.attachmentsContainer.findViewById<ImageView>(R.id.mediaReadStatusImageView)?.let {
                applyDeliveryStatusIcon(it, item.readStatus, useLightTint = true)
            }
            updateOutgoingUploadViews(binding.attachmentsContainer, item)
        }

        fun bindSelection(item: MessageItem) {
            bindSelectionIndicator(binding.root, binding.selectionIndicator, contentColumn = null, item)
        }
    }

    // ─── Received Message ViewHolder ──────────────────────────────────────────

    inner class ReceivedMessageViewHolder(
        private val binding: ItemMessageReceivedBinding
    ) : RecyclerView.ViewHolder(binding.root) {

        /** Вызывается и из полного bind, и из частичного обновления по PAYLOAD_SENDER_INFO. */
        fun bindSenderInfo(item: MessageItem) {
            if (!isGroupChat) {
                binding.senderInfoLayout.visibility = View.GONE
                return
            }
            binding.senderInfoLayout.visibility = View.VISIBLE

            // Имя и аватар отправителя берём из резолвера (кэш участников чата),
            // с фолбэком на поля самого сообщения.
            val resolved = eventSink?.senderInfo(item.senderId)
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
                    attachmentLoader.url(senderAvatarFileId)
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

        fun bindSelection(item: MessageItem) {
            bindSelectionIndicator(binding.root, binding.selectionIndicator, binding.contentColumn, item)
        }

        fun bind(item: MessageItem, group: GroupPosition) {
            applyGroupSpacing(binding.root, group)
            // У входящих своего отступа под индикатор нет — в режиме выделения сдвигаем
            // весь контент вправо, освобождая место индикатору у левого края.
            bindSelectionIndicator(binding.root, binding.selectionIndicator, binding.contentColumn, item)

            // Клик открывает меню действий только по самому пузырю — тап в пустое
            // место строки (широкий paddingEnd для отступа от края экрана) меню не открывает.
            // В режиме выделения клик по пузырю переключает выбор вместо открытия меню.
            val bubbleClickListener = View.OnClickListener { v ->
                if (eventSink?.isSelectionActive() ?: item.selectionEnabled) {
                    eventSink?.onSelectionToggle(item.messageId)
                } else {
                    eventSink?.onMessageActionRequested(v, item)
                }
            }
            binding.messageCard.setOnClickListener(bubbleClickListener)
            binding.stickerContainer.setOnClickListener(bubbleClickListener)

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

                binding.stickerTimeTextView.text = contentRenderer.formatTime(item.timestamp)
            } else {
                // Обычное сообщение с облачком
                binding.messageCard.visibility = View.VISIBLE
                binding.stickerContainer.visibility = View.GONE
                binding.stickerTimeStatusLayout.visibility = View.GONE

                // Форма пузыря: базовый радиус — из настроек персонализации
                applyBubbleShape(binding.messageCard, group, isSentByMe = false)
                val bubbleCorners = computeBubbleCorners(
                    binding.root.resources.displayMetrics.density, group, isSentByMe = false
                )
                val roundMediaTop = binding.replyQuote.quoteContainer.visibility != View.VISIBLE
                val roundMediaBottom = item.text.isBlank() &&
                    binding.forwardQuotesContainer.visibility != View.VISIBLE

                if (item.text.isNotBlank()) {
                    contentRenderer.renderText(
                        binding.messageMarkdownContainer,
                        binding.messageTextView,
                        item.text
                    )
                    binding.messageTextView.visibility = View.VISIBLE
                } else {
                    contentRenderer.clearText(
                        binding.messageMarkdownContainer,
                        binding.messageTextView
                    )
                }

                binding.timeTextView.text = contentRenderer.formatTime(item.timestamp)
                binding.editedLabelTextView.visibility = if (item.isEdited) View.VISIBLE else View.GONE

                val showMediaTimeOverlay = item.text.isBlank() && displayedAttachments.isPureMedia()
                binding.timeStatusLayout.visibility = if (showMediaTimeOverlay) View.GONE else View.VISIBLE
                applyVoiceInlineTime(binding.timeStatusLayout, item, displayedAttachments, binding.forwardQuotesContainer)

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
                        sourceMessageId = item.messageId,
                        mediaCorners = bubbleCorners,
                        roundMediaTop = roundMediaTop,
                        roundMediaBottom = roundMediaBottom
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
            eventSink?.onReplyQuoteClick(origId)
        }
    }

    /** Превью для reply: текст оригинала, а если его нет — тип первого вложения. */
    private fun buildReplyPreviewLine(data: Shared.ReplyInfo, context: Context): String {
        if (data.textPreview.isNotBlank()) return contentRenderer.plainText(data.textPreview)

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
            quote.forwardTextTextView.text = contentRenderer.plainText(data.text)
            quote.forwardTextTextView.visibility = View.VISIBLE
        } else {
            quote.forwardTextTextView.visibility = View.GONE
        }
    }

    // ─── Sticker Helper ───────────────────────────────────────────────────────

    private fun loadStickerImage(imageView: ImageView, attachment: Shared.MessageAttachment) {
        val fileId = if (attachment.previewFileId.isNotBlank()) attachment.previewFileId else attachment.fileId
        val previewUrl = attachmentRenderer.previewUrl(imageView.context, attachment)

        val getUrl: suspend () -> String? = if (previewUrl.isNotBlank()) {
            { previewUrl }
        } else {
            { attachmentLoader.url(fileId) }
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
        overlay.findViewById<android.widget.TextView>(R.id.mediaTimeTextView).text =
            contentRenderer.formatTime(item.timestamp)
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
        sourceMessageId: Long? = null,
        mediaCorners: FloatArray = ZERO_CORNERS,
        roundMediaTop: Boolean = false,
        roundMediaBottom: Boolean = false
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

        // Медиа-сетка (IMAGE / GIF / VIDEO) — ряды по алгоритму WPF MultiImageGrid.
        // Низ сетки скругляется под форму пузыря, только если медиа — последнее видимое
        // содержимое (иначе за ней идут стикеры/аудио/документы и низ должен остаться прямым).
        if (mediaItems.isNotEmpty() && mediaWidthPx > 0) {
            val effectiveRoundBottom = roundMediaBottom &&
                stickers.isEmpty() && audios.isEmpty() && docs.isEmpty()
            val mediaGrid = buildMediaGrid(
                context, mediaItems, mediaWidthPx, sourceMessageId,
                mediaCorners, roundMediaTop, effectiveRoundBottom
            )
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
            val audioView = inflateAudioRow(container, audio, isSentByMe, sourceMessageId)
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
        sourceMessageId: Long?,
        bubbleCorners: FloatArray = ZERO_CORNERS,
        roundTop: Boolean = false,
        roundBottom: Boolean = false
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

                val cellCorners = mediaCellCorners(
                    bubbleCorners, roundTop, roundBottom,
                    isTopRow = rowIdx == 0,
                    isBottomRow = rowIdx == layout.lastIndex,
                    isLeftCol = col == 0,
                    isRightCol = col == itemsInRow - 1
                )
                bindMediaCell(cellView, attachment, capped, itemIndex, sourceMessageId, cellCorners)
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
        maxWidth: Int,
        bubbleCorners: FloatArray = ZERO_CORNERS,
        roundTop: Boolean = false,
        roundBottom: Boolean = false
    ): View {
        val dm = context.resources.displayMetrics
        val spacingPx = (2 * dm.density + 0.5f).toInt()
        val capped = uris.take(10)
        val layout = determineLayout(capped.size)
        val isSingle = capped.size == 1
        val singleRatio: Float? = if (isSingle) {
            val uri = capped[0]
            val isVideo = context.contentResolver.getType(uri)?.startsWith("video/") == true
            val dims = if (isVideo) getVideoDimensions(uri, context) else ImageCompressor.getImageDimensions(uri, context)
            dims?.takeIf { it.first > 0 && it.second > 0 }?.let { (w, h) -> w.toFloat() / h.toFloat() }
        } else null

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
            val cellHeight = when {
                isSingle && singleRatio != null -> (cellWidth / singleRatio).toInt().coerceIn(cellWidth / 3, cellWidth * 2)
                isSingle -> (cellWidth * 0.75f).toInt()
                else -> cellWidth
            }

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
                val cellCorners = mediaCellCorners(
                    bubbleCorners, roundTop, roundBottom,
                    isTopRow = rowIdx == 0,
                    isBottomRow = rowIdx == layout.lastIndex,
                    isLeftCol = col == 0,
                    isRightCol = col == itemsInRow - 1
                )
                applyMediaCellCorners(cellView, cellCorners)
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

    private fun OutgoingAttachmentUi.isOutgoingMedia(): Boolean = when (kind) {
        OutgoingAttachmentKind.RAW_IMAGE,
        OutgoingAttachmentKind.EDITED_IMAGE,
        OutgoingAttachmentKind.VIDEO,
        OutgoingAttachmentKind.STICKER -> true
        else -> false
    }

    private fun OutgoingAttachmentUi.isOutgoingAudio(): Boolean =
        kind == OutgoingAttachmentKind.VOICE || mimeType.startsWith("audio/", ignoreCase = true)

    private fun buildOutgoingAttachments(
        container: ViewGroup,
        attachments: List<OutgoingAttachmentUi>,
        item: MessageItem,
        mediaWidthPx: Int,
        bubbleCorners: FloatArray,
        roundMediaTop: Boolean,
        roundMediaBottom: Boolean,
    ): Pair<View, android.widget.FrameLayout?> {
        val context = container.context
        val ordered = attachments.sortedBy { it.index }
        val media = ordered.filter { it.isOutgoingMedia() }
        val other = ordered.filterNot { it.isOutgoingMedia() }
        val wrapper = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = LinearLayout.LayoutParams(
                if (mediaWidthPx > 0) ViewGroup.LayoutParams.MATCH_PARENT else ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
            )
        }
        var mediaGrid: android.widget.FrameLayout? = null
        if (media.isNotEmpty() && mediaWidthPx > 0) {
            mediaGrid = buildOutgoingMediaGrid(
                context = context,
                attachments = media,
                item = item,
                maxWidth = mediaWidthPx,
                bubbleCorners = bubbleCorners,
                roundTop = roundMediaTop,
                roundBottom = roundMediaBottom && other.isEmpty(),
            )
            wrapper.addView(mediaGrid)
        }
        other.forEach { attachment ->
            val row = if (attachment.isOutgoingAudio()) {
                buildOutgoingAudioRow(container, attachment, item)
            } else {
                buildOutgoingDocumentRow(container, attachment, item)
            }
            wrapper.addView(row)
        }
        return wrapper to mediaGrid
    }

    private fun buildOutgoingMediaGrid(
        context: Context,
        attachments: List<OutgoingAttachmentUi>,
        item: MessageItem,
        maxWidth: Int,
        bubbleCorners: FloatArray,
        roundTop: Boolean,
        roundBottom: Boolean,
    ): android.widget.FrameLayout {
        val dm = context.resources.displayMetrics
        val spacingPx = (2 * dm.density + 0.5f).toInt()
        val capped = attachments.take(10)
        val layout = determineLayout(capped.size)
        val grid = android.widget.FrameLayout(context).apply {
            layoutParams = LinearLayout.LayoutParams(maxWidth, ViewGroup.LayoutParams.WRAP_CONTENT)
        }
        val column = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            layoutParams = android.widget.FrameLayout.LayoutParams(
                maxWidth,
                ViewGroup.LayoutParams.WRAP_CONTENT,
            )
        }

        var itemIndex = 0
        for ((rowIdx, itemsInRow) in layout.withIndex()) {
            val cellWidth = (maxWidth - spacingPx * (itemsInRow - 1)) / itemsInRow
            val rowAttachments = capped.subList(itemIndex, (itemIndex + itemsInRow).coerceAtMost(capped.size))
            val rowRatios = if (capped.size == 1) {
                rowAttachments.mapNotNull { outgoingMediaRatio(context, it) }
            } else {
                emptyList()
            }
            val avgRatio = if (rowRatios.isNotEmpty()) rowRatios.average().toFloat() else 1f
            val cellHeight = (cellWidth / avgRatio).toInt().coerceIn(cellWidth / 3, cellWidth * 2)
            val row = LinearLayout(context).apply {
                orientation = LinearLayout.HORIZONTAL
                layoutParams = LinearLayout.LayoutParams(maxWidth, cellHeight).apply {
                    if (rowIdx > 0) topMargin = spacingPx
                }
            }
            for (col in 0 until itemsInRow) {
                if (itemIndex >= capped.size) break
                val attachment = capped[itemIndex]
                val cell = LayoutInflater.from(context)
                    .inflate(R.layout.item_attachment_media_cell, row, false)
                cell.layoutParams = LinearLayout.LayoutParams(cellWidth, cellHeight).apply {
                    if (col > 0) marginStart = spacingPx
                }
                val corners = mediaCellCorners(
                    bubbleCorners, roundTop, roundBottom,
                    isTopRow = rowIdx == 0,
                    isBottomRow = rowIdx == layout.lastIndex,
                    isLeftCol = col == 0,
                    isRightCol = col == itemsInRow - 1,
                )
                applyMediaCellCorners(cell, corners)
                cell.tag = attachment
                val thumbnail = cell.findViewById<ImageView>(R.id.thumbnailImage)
                val localFile = File(attachment.previewPath ?: attachment.stagedPath)
                // Перерисовка пузыря во время отправки берёт уже декодированное превью из памяти,
                // чтобы картинка не пропадала на кадр-заглушку.
                val memoryKey = "outgoing:${localFile.path}"
                val seeded = ImageLoadHelper.peekMemory(context.imageLoader, memoryKey)
                if (seeded != null) {
                    thumbnail.setImageBitmap(seeded)
                } else {
                    thumbnail.setImageResource(R.drawable.ic_image_placeholder)
                    if (localFile.isFile) {
                        thumbnail.load(localFile) {
                            memoryCacheKey(memoryKey)
                            crossfade(150)
                            error(R.drawable.ic_image_placeholder)
                        }
                    }
                }
                renderOutgoingAttachmentIndicator(cell, attachment, item)
                row.addView(cell)
                itemIndex++
            }
            column.addView(row)
        }
        grid.addView(column)
        return grid
    }

    private fun outgoingMediaRatio(context: Context, attachment: OutgoingAttachmentUi): Float? {
        val uri = Uri.fromFile(File(attachment.previewPath ?: attachment.stagedPath))
        val dimensions = if (attachment.kind == OutgoingAttachmentKind.VIDEO) {
            getVideoDimensions(uri, context)
        } else {
            ImageCompressor.getImageDimensions(uri, context)
        }
        return dimensions?.takeIf { it.first > 0 && it.second > 0 }
            ?.let { (width, height) -> width.toFloat() / height.toFloat() }
    }

    private fun buildOutgoingAudioRow(
        container: ViewGroup,
        attachment: OutgoingAttachmentUi,
        item: MessageItem,
    ): View {
        val binding = ItemAttachmentAudioBinding.inflate(LayoutInflater.from(container.context), container, false)
        binding.root.tag = attachment
        binding.fileNameText.text = attachment.fileName.ifBlank { container.context.getString(R.string.attachment_file) }
        binding.fileNameText.visibility = View.VISIBLE
        binding.voiceWaveform.visibility = View.GONE
        binding.durationText.text = formatFileSize(container.context, attachment.sizeBytes)
        binding.downloadButton.visibility = View.GONE
        binding.downloadProgressBar.visibility = View.GONE
        binding.audioSeekBar.visibility = View.GONE
        binding.playPauseButton.isEnabled = false
        binding.playPauseButton.alpha = 0.6f
        binding.outgoingUploadProgress.setIndicatorColor(resolveOnPrimaryContainerColor(container.context))
        renderOutgoingAttachmentIndicator(binding.root, attachment, item)
        return binding.root
    }

    private fun buildOutgoingDocumentRow(
        container: ViewGroup,
        attachment: OutgoingAttachmentUi,
        item: MessageItem,
    ): View {
        val binding = ItemAttachmentDocumentBinding.inflate(LayoutInflater.from(container.context), container, false)
        binding.root.tag = attachment
        binding.docFileName.text = attachment.fileName.ifBlank { container.context.getString(R.string.attachment_file) }
        binding.docFileSize.text = formatFileSize(container.context, attachment.sizeBytes)
        binding.docDownloadButton.visibility = View.GONE
        binding.docOpenButton.visibility = View.GONE
        binding.docMenuButton.visibility = View.GONE
        binding.docDownloadProgress.visibility = View.GONE
        binding.outgoingUploadProgress.setIndicatorColor(resolveOnPrimaryContainerColor(container.context))
        renderOutgoingAttachmentIndicator(binding.root, attachment, item)
        return binding.root
    }

    private fun updateOutgoingUploadViews(root: View, item: MessageItem) {
        val boundAttachment = root.tag as? OutgoingAttachmentUi
        if (boundAttachment != null) {
            val attachment = item.outgoingAttachments.firstOrNull { it.index == boundAttachment.index }
                ?: boundAttachment
            updateOutgoingAttachmentMetadata(root, boundAttachment, attachment)
            root.tag = attachment
            renderOutgoingAttachmentIndicator(root, attachment, item)
        }
        if (root is ViewGroup) {
            for (index in 0 until root.childCount) updateOutgoingUploadViews(root.getChildAt(index), item)
        }
    }

    private fun updateOutgoingAttachmentMetadata(
        root: View,
        old: OutgoingAttachmentUi,
        new: OutgoingAttachmentUi,
    ) {
        if (old.fileName == new.fileName && old.sizeBytes == new.sizeBytes) return
        val fileName = new.fileName.ifBlank { root.context.getString(R.string.attachment_file) }
        root.findViewById<android.widget.TextView>(R.id.docFileName)?.text = fileName
        root.findViewById<android.widget.TextView>(R.id.fileNameText)?.text = fileName
        val size = formatFileSize(root.context, new.sizeBytes)
        root.findViewById<android.widget.TextView>(R.id.docFileSize)?.text = size
        root.findViewById<android.widget.TextView>(R.id.durationText)?.text = size
    }

    private fun renderOutgoingAttachmentIndicator(
        root: View,
        attachment: OutgoingAttachmentUi,
        item: MessageItem,
    ) {
        val context = root.context
        val progress = root.findViewById<com.google.android.material.progressindicator.CircularProgressIndicator>(
            R.id.outgoingUploadProgress,
        )
        val mediaIndicator = root.findViewById<View>(R.id.outgoingUploadIndicator)
        val cancelButton = root.findViewById<View>(R.id.outgoingCancelButton)
        val showProgress = item.outgoingState != OutgoingMessageState.FAILED &&
            attachment.uploadState != OutgoingAttachmentUploadState.DONE
        mediaIndicator?.visibility = if (showProgress) View.VISIBLE else View.GONE
        if (mediaIndicator == null) progress?.visibility = if (showProgress) View.VISIBLE else View.GONE
        progress?.let {
            it.isIndeterminate = attachment.uploadState != OutgoingAttachmentUploadState.UPLOADING
            if (!it.isIndeterminate) it.progress = attachment.progress
            if (mediaIndicator == null) it.contentDescription = context.getString(
                R.string.message_upload_progress,
                attachment.progress,
            )
        }
        cancelButton?.let { button ->
            button.visibility = if (showProgress && !item.localId.isNullOrBlank()) View.VISIBLE else View.GONE
            button.contentDescription = context.getString(R.string.btn_cancel)
            button.setOnClickListener {
                item.localId?.takeIf(String::isNotBlank)?.let { operationId ->
                    eventSink?.onCancelOutgoing(operationId)
                }
            }
        }
        root.findViewById<View>(R.id.mediaDownloadIcon)?.visibility = View.GONE
        root.findViewById<View>(R.id.mediaDownloadProgress)?.visibility = View.GONE
        root.findViewById<View>(R.id.videoOverlay)?.visibility = if (attachment.kind == OutgoingAttachmentKind.VIDEO) View.VISIBLE else View.GONE
        root.findViewById<ImageView>(R.id.playIcon)?.visibility = if (
            attachment.kind == OutgoingAttachmentKind.VIDEO && attachment.uploadState == OutgoingAttachmentUploadState.DONE
        ) View.VISIBLE else View.GONE
    }

    /**
     * Привязывает данные к ячейке медиа-сетки: превью, оверлей видео, клик.
     */
    private fun bindMediaCell(
        cellView: View,
        attachment: Shared.MessageAttachment,
        allMedia: List<Shared.MessageAttachment>,
        position: Int,
        sourceMessageId: Long?,
        cellCorners: FloatArray = ZERO_CORNERS
    ) {
        val thumbnail = cellView.findViewById<ImageView>(R.id.thumbnailImage)
        val videoOverlay = cellView.findViewById<View>(R.id.videoOverlay)
        val playIcon = cellView.findViewById<ImageView>(R.id.playIcon)

        applyMediaCellCorners(cellView, cellCorners)
        thumbnail.setImageDrawable(null)

        val isVideo = attachment.type == Shared.MessageAttachmentType.VIDEO
        videoOverlay.visibility = if (isVideo) View.VISIBLE else View.GONE
        playIcon.visibility = View.GONE

        val downloadIcon = cellView.findViewById<View>(R.id.mediaDownloadIcon)
        val progress = cellView.findViewById<com.google.android.material.progressindicator.CircularProgressIndicator>(R.id.mediaDownloadProgress)
        // Только что отправленное вложение уже лежит в памяти — показываем его без кадра-заглушки.
        val seeded = ImageLoadHelper.peekMemory(thumbnail.context, attachment.fileId)
        if (seeded != null) thumbnail.setImageBitmap(seeded) else thumbnail.setImageResource(R.drawable.ic_image_placeholder)
        downloadIcon.visibility = if (seeded != null) View.GONE else View.VISIBLE
        cellView.contentDescription = thumbnail.context.getString(R.string.cd_download_file)
        viewOperations.launch(thumbnail) {
            if (ImageLoadHelper.loadCached(thumbnail, attachment)) {
                downloadIcon.visibility = View.GONE
                if (isVideo && progress.visibility != View.VISIBLE) playIcon.visibility = View.VISIBLE
            }
        }
        val render: (AttachmentDownloadState) -> Unit = { state ->
            when (state) {
                is AttachmentDownloadState.Cached -> {
                    viewOperations.cancel(thumbnail)
                    ImageLoadHelper.loadLocal(thumbnail, attachment, state.file)
                    downloadIcon.visibility = View.GONE
                    progress.visibility = View.GONE
                    playIcon.visibility = if (isVideo) View.VISIBLE else View.GONE
                    cellView.contentDescription = thumbnail.context.getString(if (isVideo) R.string.cd_play_video else R.string.cd_open_file)
                    cellView.stateDescription = null
                }
                is AttachmentDownloadState.Downloading -> {
                    downloadIcon.visibility = View.GONE
                    progress.visibility = View.VISIBLE
                    playIcon.visibility = View.GONE
                    progress.progress = state.progress
                    progress.isIndeterminate = state.progress == 0
                    cellView.stateDescription = thumbnail.context.getString(R.string.cd_auto_download_progress, state.progress)
                }
                AttachmentDownloadState.Waiting, AttachmentDownloadState.Failed -> {
                    downloadIcon.visibility = View.VISIBLE
                    progress.visibility = View.GONE
                    playIcon.visibility = View.GONE
                    cellView.contentDescription = thumbnail.context.getString(R.string.cd_download_file)
                    cellView.stateDescription = if (state == AttachmentDownloadState.Failed) thumbnail.context.getString(R.string.auto_download_failed) else null
                }
            }
        }
        if (autoDownloadViews != null) autoDownloadViews.bind(cellView, attachment, render)
        else attachmentLoader.cached(attachment.fileId)?.let { render(AttachmentDownloadState.Cached(it)) }

        // Navigation is owned by the Activity/controller; recycled rows only emit a typed event.
        if (isVideo) {
            cellView.setOnClickListener {
                val cachedPath = attachmentLoader.cached(attachment.fileId)?.absolutePath
                eventSink?.onAttachmentAction(
                    MessageAttachmentAction.OpenVideo(
                        fileId = attachment.fileId,
                        fileName = attachment.fileName.ifBlank { "video" },
                        cachedPath = cachedPath,
                    )
                )
            }
        } else {
            cellView.setOnClickListener {
                val imageItems = allMedia.filter {
                    it.type == Shared.MessageAttachmentType.IMAGE ||
                    it.type == Shared.MessageAttachmentType.GIF
                }
                val clickedIndex = imageItems.indexOf(attachment).coerceAtLeast(0)
                val allFileIds    = imageItems.map { it.fileId }
                val allPreviewUrls = imageItems.map { attachmentRenderer.previewUrl(cellView.context, it) }
                eventSink?.onAttachmentAction(
                    MessageAttachmentAction.OpenImage(
                        fileIds = allFileIds,
                        previewUrls = allPreviewUrls,
                        clickedIndex = clickedIndex,
                        fileNames = imageItems.map { it.fileName },
                        sourceMessageIds = List(imageItems.size) { sourceMessageId ?: 0L },
                    )
                )
            }

        }
    }

    /** У голосового без текста время и статус встают справа в строку длительности, как в Telegram. */
    private fun applyVoiceInlineTime(
        timeStatus: View,
        item: MessageItem,
        displayed: List<Shared.MessageAttachment>,
        forwardQuotes: View,
    ) {
        val inline = item.text.isBlank() && item.outgoingAttachments.isEmpty() &&
            forwardQuotes.visibility != View.VISIBLE &&
            displayed.size == 1 && displayed[0].type == Shared.MessageAttachmentType.VOICE
        val params = timeStatus.layoutParams as ViewGroup.MarginLayoutParams
        val topMargin = ((if (inline) -VOICE_INLINE_TIME_OVERLAP_DP else TIME_STATUS_TOP_MARGIN_DP) *
            timeStatus.resources.displayMetrics.density).toInt()
        if (params.topMargin != topMargin) {
            params.topMargin = topMargin
            timeStatus.layoutParams = params
        }
    }

    // ─── Audio Row ────────────────────────────────────────────────────────────

    private fun inflateAudioRow(container: ViewGroup, attachment: Shared.MessageAttachment, isSentByMe: Boolean = false, sourceMessageId: Long? = null): View {
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
        var cachedDurationMillis = 0L
        binding.durationText.text = formatAudioTime(0L)

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
                cachedDurationMillis = durationMs.toLong()
                binding.durationText.text = formatAudioTime(cachedDurationMillis)
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

        fun startDownload() {
            autoDownloadViews?.setManualDownloading(binding.root, true)
            binding.downloadProgressBar.isIndeterminate = false
            updateUiForDownloading()
            binding.downloadProgressBar.progress = 0
            binding.root.tag = fileId
            binding.downloadButton.tag = fileId

            viewOperations.launch(binding.downloadButton) {
                try {
                    val file = attachmentLoader.download(fileId) { progress ->
                        binding.root.post {
                            if (binding.root.tag == fileId) {
                                binding.downloadProgressBar.progress = progress
                            }
                        }
                    }
                    if (binding.root.tag != fileId) return@launch

                    if (file != null) {
                        val durationMs = audioRenderer.duration(file)
                        updateUiForCached(durationMs)
                        loadVoiceWaveform(fileId, file, binding)
                    } else {
                        updateUiForNotCached()
                    }
                } finally {
                    if (binding.root.tag == fileId) autoDownloadViews?.setManualDownloading(binding.root, false)
                }
            }
        }

        val cachedFile = attachmentLoader.cached(fileId)
        if (cachedFile != null) {
            val durationMs = audioRenderer.duration(cachedFile)
            updateUiForCached(durationMs)
            loadVoiceWaveform(fileId, cachedFile, binding)
            if (audioPlaybackController.isActiveFile(fileId)) {
                updateAudioPlaybackUI(binding, audioPlaybackController.isPlaying())
                val duration = audioPlaybackController.duration()
                if (duration > 0) {
                    val progress = audioPlaybackController.currentPosition().toFloat() / duration
                    if (isVoice) binding.voiceWaveform.setProgress(progress) else binding.audioSeekBar.progress = (progress * 1000).toInt()
                }
            }
        } else {
            updateUiForNotCached()
        }

        binding.downloadButton.setOnClickListener {
            startDownload()
        }

        autoDownloadViews?.bind(binding.root, attachment) { state ->
            binding.root.stateDescription = if (state == AttachmentDownloadState.Failed) context.getString(R.string.auto_download_failed) else null
            when (state) {
                is AttachmentDownloadState.Cached -> {
                    updateUiForCached(audioRenderer.duration(state.file))
                    loadVoiceWaveform(fileId, state.file, binding)
                }
                is AttachmentDownloadState.Downloading -> {
                    updateUiForDownloading()
                    binding.downloadProgressBar.progress = state.progress
                    binding.downloadProgressBar.isIndeterminate = state.progress == 0
                    binding.downloadProgressBar.contentDescription = context.getString(R.string.cd_auto_download_progress, state.progress)
                }
                AttachmentDownloadState.Waiting, AttachmentDownloadState.Failed -> updateUiForNotCached()
            }
        }

        binding.playPauseButton.setOnClickListener {
            val file = attachmentLoader.cached(fileId) ?: return@setOnClickListener
            if (audioPlaybackController.isActiveFile(fileId)) {
                if (audioPlaybackController.isPlaying()) audioPlaybackController.pause() else audioPlaybackController.resume()
            } else {
                val source = currentList.firstOrNull { it.messageId == sourceMessageId }
                val sender = if (isSentByMe) context.getString(R.string.voice_sender_you) else
                    source?.let { item ->
                        eventSink?.senderInfo(item.senderId)?.first?.takeIf(String::isNotBlank)
                            ?: item.senderName?.takeIf(String::isNotBlank)
                            ?: playbackChatTitle.takeIf { !isGroupChat && it.isNotBlank() }
                            ?: context.getString(R.string.group_member_id, item.senderId)
                    }.orEmpty()
                audioPlaybackController.play(AudioTrack(fileId, playbackChatId, playbackChatTitle,
                    sender, sourceMessageId ?: 0L, isVoice, isGroupChat, playbackOtherUserId), file)
            }
        }
        viewOperations.launch(binding.playPauseButton) {
            playback.state.collect { state ->
                val active = state.track?.fileId == fileId
                updateAudioPlaybackUI(binding, active && state.isPlaying)
                if (active && state.durationMillis > 0L) {
                    val progress = (state.positionMillis.toFloat() / state.durationMillis).coerceIn(0f, 1f)
                    if (!binding.voiceWaveform.isPressed) binding.voiceWaveform.setProgress(progress)
                    if (!binding.audioSeekBar.isPressed) binding.audioSeekBar.progress = (progress * 1000).toInt()
                    binding.durationText.text = context.getString(R.string.audio_position,
                        formatAudioTime(state.positionMillis), formatAudioTime(state.durationMillis))
                    binding.voiceWaveform.contentDescription = context.getString(R.string.cd_voice_seek,
                        formatAudioTime(state.positionMillis), formatAudioTime(state.durationMillis))
                } else if (!active) {
                    binding.voiceWaveform.setProgress(0f)
                    binding.audioSeekBar.progress = 0
                    binding.durationText.text = formatAudioTime(cachedDurationMillis)
                }
            }
        }

        binding.audioSeekBar.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar, progress: Int, fromUser: Boolean) {
                if (fromUser && audioPlaybackController.isActiveFile(fileId)) {
                    val duration = audioPlaybackController.duration()
                    if (duration > 0) {
                        audioPlaybackController.seekTo((progress.toLong() * duration / 1000L).toInt())
                    }
                }
            }
            override fun onStartTrackingTouch(seekBar: SeekBar) {
                if (audioPlaybackController.isActiveFile(fileId)) audioPlaybackController.beginScrub()
            }
            override fun onStopTrackingTouch(seekBar: SeekBar) {
                if (audioPlaybackController.isActiveFile(fileId)) audioPlaybackController.endScrub()
            }
        })

        binding.voiceWaveform.onScrubChanged = { started ->
            if (audioPlaybackController.isActiveFile(fileId)) {
                if (started) audioPlaybackController.beginScrub() else audioPlaybackController.endScrub()
            }
        }
        binding.voiceWaveform.onSeekRequested = { progress ->
            if (audioPlaybackController.isActiveFile(fileId)) {
                val duration = audioPlaybackController.duration()
                if (duration > 0) {
                    audioPlaybackController.seekTo((progress * duration).toInt())
                }
            }
        }

        return binding.root
    }
    private fun getVideoDimensions(uri: Uri, context: Context): Pair<Int, Int>? {
        return try {
            val retriever = MediaMetadataRetriever()
            retriever.setDataSource(context, uri)
            val width = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_WIDTH)?.toIntOrNull() ?: 0
            val height = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_HEIGHT)?.toIntOrNull() ?: 0
            val rotation = retriever.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION)?.toIntOrNull() ?: 0
            retriever.release()
            if (width <= 0 || height <= 0) null
            else if (rotation == 90 || rotation == 270) Pair(height, width) else Pair(width, height)
        } catch (e: Exception) {
            null
        }
    }

    private fun loadVoiceWaveform(
        fileId: String,
        file: File,
        binding: ItemAttachmentAudioBinding
    ) {
        if (binding.voiceWaveform.visibility != View.VISIBLE) return

        val cached = audioPlaybackController.waveform(fileId)
        if (cached != null) {
            binding.voiceWaveform.setAmplitudes(cached)
            return
        }

        binding.voiceWaveform.resetAmplitudes()
        viewOperations.launch(binding.voiceWaveform) {
            val waveform = withContext(Dispatchers.IO) {
                AudioWaveformExtractor.extract(file)
            }
            if (binding.root.tag == fileId) {
                audioPlaybackController.cacheWaveform(fileId, waveform)
                binding.voiceWaveform.setAmplitudes(waveform)
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
        binding.playPauseButton.contentDescription = binding.root.context.getString(if (isPlaying) R.string.cd_pause else R.string.cd_play)
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
        val thumbnailUrl = attachmentRenderer.previewUrl(binding.root.context, attachment)

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
                getUrlCallback = { attachmentLoader.url(thumbnailFileId) },
                onError = { binding.videoThumbnail.setImageResource(R.drawable.ic_image_placeholder) }
            )
        }

        if (attachmentLoader.hasCached(fileId)) {
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

            viewOperations.launch(binding.root) {
                val file = attachmentLoader.download(fileId) { _ -> }
                binding.videoDownloadProgress.visibility = View.GONE
                if (file != null) {
                    binding.videoPlayButton.alpha = 1f
                    binding.videoPlayButton.isEnabled = true
                } else {
                    binding.videoDownloadButton.visibility = View.VISIBLE
                }
            }
        }

        // Play button
        binding.videoPlayButton.setOnClickListener {
            val cachedPath = attachmentLoader.cached(fileId)?.absolutePath
            eventSink?.onAttachmentAction(
                MessageAttachmentAction.OpenVideo(
                    fileId = fileId,
                    fileName = fileName,
                    cachedPath = cachedPath,
                )
            )
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
        val previewUrl = attachmentRenderer.previewUrl(context, attachment)

        binding.docFileName.text = fileName
        binding.root.tag = fileId
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
            binding.docMenuButton.visibility = View.VISIBLE
        }

        fun updateUiForNotCached() {
            binding.docDownloadButton.visibility = View.VISIBLE
            binding.docOpenButton.visibility = View.GONE
            binding.docDownloadProgress.visibility = View.GONE
            binding.docMenuButton.visibility = View.GONE
            binding.docDownloadButton.isEnabled = true
        }

        fun updateUiForDownloading() {
            binding.docDownloadButton.visibility = View.VISIBLE
            binding.docDownloadButton.isEnabled = false
            binding.docOpenButton.visibility = View.GONE
            binding.docDownloadProgress.isIndeterminate = false
            binding.docDownloadProgress.visibility = View.VISIBLE
            binding.docDownloadProgress.progress = 0
            binding.docMenuButton.visibility = View.GONE
        }

        if (attachmentLoader.hasCached(fileId)) {
            updateUiForCached()
        } else {
            updateUiForNotCached()
        }

        // Download button
        binding.docDownloadButton.setOnClickListener {
            autoDownloadViews?.setManualDownloading(binding.root, true)
            updateUiForDownloading()

            viewOperations.launch(binding.docDownloadButton) {
                try {
                    val file = attachmentLoader.download(fileId) { progress ->
                        binding.root.post {
                            if (binding.root.tag == fileId) binding.docDownloadProgress.progress = progress
                        }
                    }
                    if (binding.root.tag != fileId) return@launch
                    if (file != null) updateUiForCached() else updateUiForNotCached()
                } finally {
                    if (binding.root.tag == fileId) autoDownloadViews?.setManualDownloading(binding.root, false)
                }
            }
        }

        autoDownloadViews?.bind(binding.root, attachment) { state ->
            binding.root.stateDescription = if (state == AttachmentDownloadState.Failed) context.getString(R.string.auto_download_failed) else null
            when (state) {
                is AttachmentDownloadState.Cached -> updateUiForCached()
                is AttachmentDownloadState.Downloading -> {
                    updateUiForDownloading()
                    binding.docDownloadProgress.progress = state.progress
                    binding.docDownloadProgress.isIndeterminate = state.progress == 0
                    binding.docDownloadProgress.contentDescription = context.getString(R.string.cd_auto_download_progress, state.progress)
                }
                AttachmentDownloadState.Waiting, AttachmentDownloadState.Failed -> updateUiForNotCached()
            }
        }

        // Open button
        binding.docOpenButton.setOnClickListener {
            val cachedFile = attachmentLoader.cached(fileId) ?: return@setOnClickListener
            eventSink?.onAttachmentAction(
                MessageAttachmentAction.OpenDocument(
                    fileId = fileId,
                    fileName = fileName,
                    cachedFile = cachedFile,
                    previewUrl = previewUrl,
                )
            )
        }

        binding.docMenuButton.setOnClickListener { view ->
            showDocumentContextMenu(view, context, fileId, fileName, binding)
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
        val isCached = attachmentLoader.hasCached(fileId)
        if (!isCached) {
            // Меню сводилось к одному пункту "Удалить из кеша" — для нескачанного файла нет смысла показывать
            return
        }
        M3ContextMenu.show(
            anchor = anchor,
            items = listOf(M3ContextMenu.Item(
                id = R.id.action_delete_doc_from_cache,
                title = context.getString(R.string.file_remove_from_cache),
                icon = R.drawable.ic_action_delete,
                danger = true,
            )),
            alignEnd = true,
            onItemSelected = { menuItem ->
            when (menuItem.id) {
                R.id.action_delete_doc_from_cache -> {
                    attachmentLoader.deleteCached(fileId)
                    binding.docDownloadButton.visibility = View.VISIBLE
                    binding.docDownloadButton.isEnabled = true
                    binding.docOpenButton.visibility = View.GONE
                    binding.docDownloadProgress.visibility = View.GONE
                    binding.docMenuButton.visibility = View.GONE
                    eventSink?.onAttachmentAction(
                        MessageAttachmentAction.ToastRes(R.string.file_removed_from_cache)
                    )
                    true
                }
                else -> false
            }
            },
        )
    }

    // ─── Formatting Helpers ───────────────────────────────────────────────────

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
            if (!oldItem.localId.isNullOrBlank() && oldItem.localId == newItem.localId) return true
            return oldItem.messageId == newItem.messageId
        }
        override fun areContentsTheSame(oldItem: MessageItem, newItem: MessageItem) =
            if (oldItem.type == MessageType.FOOTER && newItem.type == MessageType.FOOTER) true
            else oldItem == newItem

        override fun getChangePayload(oldItem: MessageItem, newItem: MessageItem): Any? {
            val progressChanged = oldItem.uploadProgress != newItem.uploadProgress ||
                oldItem.outgoingState != newItem.outgoingState ||
                oldItem.outgoingAttachments != newItem.outgoingAttachments ||
                oldItem.readStatus != newItem.readStatus
            val selectionChanged = oldItem.isSelected != newItem.isSelected ||
                oldItem.selectionEnabled != newItem.selectionEnabled
            var flags = 0
            if (progressChanged) flags = flags or PAYLOAD_OUTGOING_PROGRESS
            if (selectionChanged) flags = flags or PAYLOAD_SELECTION
            if (flags == 0) return null
            return oldItem.copy(
                uploadProgress = newItem.uploadProgress,
                outgoingState = newItem.outgoingState,
                outgoingAttachments = newItem.outgoingAttachments,
                readStatus = newItem.readStatus,
                isSelected = newItem.isSelected,
                selectionEnabled = newItem.selectionEnabled,
            ).takeIf { it == newItem }?.let { flags }
        }
    }
}

// Compatibility aliases keep existing pinned/E2E imports stable while the row model lives outside
// the adapter package.
typealias MessageItem = com.barkfluff.client.chat.MessageItem
typealias MessageRowUi = com.barkfluff.client.chat.MessageRowUi
typealias MessageType = com.barkfluff.client.chat.MessageType
typealias ReadStatus = com.barkfluff.client.chat.ReadStatus
