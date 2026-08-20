package com.barkfluff.client.view

import android.app.Activity
import android.content.res.ColorStateList
import android.graphics.Bitmap
import android.graphics.Rect
import android.graphics.RenderEffect
import android.graphics.Shader
import android.os.Handler
import android.os.Looper
import android.view.GestureDetector
import android.view.HapticFeedbackConstants
import android.view.LayoutInflater
import android.view.MotionEvent
import android.view.PixelCopy
import android.view.View
import android.view.animation.PathInterpolator
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import androidx.annotation.DrawableRes
import androidx.constraintlayout.widget.ConstraintLayout
import androidx.core.content.ContextCompat
import androidx.dynamicanimation.animation.SpringAnimation
import com.barkfluff.client.R
import com.google.android.material.card.MaterialCardView
import com.google.android.material.color.MaterialColors
import com.google.android.material.divider.MaterialDivider

/**
 * Полноэкранный оверлей действий над сообщением — аналог контекстного меню
 * Telegram (iOS-стиль): блюр+скрим фона, снимок пузыря "приподнимается" над
 * ним, карточка меню появляется рядом с каскадной анимацией пунктов.
 *
 * Презентационный слой: ничего не знает о доменной логике сообщения, только
 * рисует переданные действия и сообщает о выборе через колбэк.
 */
class MessageActionsOverlay(private val root: ConstraintLayout) {

    data class Action(
        val id: Int,
        @DrawableRes val icon: Int,
        val title: CharSequence,
        val danger: Boolean = false
    )

    var isShowing: Boolean = false
        private set

    private var container: FrameLayout? = null
    private var originalBubble: View? = null
    private var gestureDetector: GestureDetector? = null
    private var onDismissCallback: (() -> Unit)? = null
    private var activeToken = 0
    private val mainHandler = Handler(Looper.getMainLooper())

    private val density get() = root.resources.displayMetrics.density
    private fun dp(value: Int): Int = (value * density).toInt()

    private val easing = PathInterpolator(0.2f, 0f, 0f, 1f)

    fun show(
        bubble: View,
        actions: List<Action>,
        alignEnd: Boolean,
        onDismiss: (() -> Unit)? = null,
        onAction: (Int) -> Unit
    ) {
        if (isShowing) dismiss(animate = false)
        isShowing = true
        originalBubble = bubble
        onDismissCallback = onDismiss
        val token = ++activeToken

        val rootLoc = IntArray(2).also { root.getLocationInWindow(it) }
        val bubbleLoc = IntArray(2).also { bubble.getLocationInWindow(it) }
        val bubbleLeft = bubbleLoc[0] - rootLoc[0]
        val bubbleTop = bubbleLoc[1] - rootLoc[1]
        val bubbleWidth = bubble.width
        val bubbleHeight = bubble.height
        val backdropWidth = (root.width * BACKDROP_SCALE).toInt().coerceAtLeast(1)
        val backdropHeight = (root.height * BACKDROP_SCALE).toInt().coerceAtLeast(1)
        val rootRect = Rect(rootLoc[0], rootLoc[1], rootLoc[0] + root.width, rootLoc[1] + root.height)
        val bubbleRect = Rect(bubbleLoc[0], bubbleLoc[1], bubbleLoc[0] + bubbleWidth, bubbleLoc[1] + bubbleHeight)

        // Снимки берём через PixelCopy, а не View.draw(Canvas): дерево чата содержит
        // hardware-bitmap изображения (аватарки/вложения через Coil), а обычный Canvas
        // поверх Bitmap не умеет их рисовать — падает с "Software rendering doesn't
        // support hardware bitmaps". PixelCopy читает пиксели из отрендеренного окна,
        // поэтому работает с любым содержимым; View-перегрузки PixelCopy в этом SDK нет,
        // только Window — поэтому оба снимка берём как под-прямоугольники окна.
        captureWindowRegion(rootRect, backdropWidth, backdropHeight) { backdropBitmap ->
            if (token != activeToken) return@captureWindowRegion
            captureWindowRegion(bubbleRect, bubbleWidth, bubbleHeight) { bubbleBitmap ->
                if (token != activeToken) return@captureWindowRegion
                buildOverlay(
                    bubble, actions, alignEnd, onAction,
                    bubbleLeft, bubbleTop, bubbleWidth, bubbleHeight,
                    backdropBitmap, bubbleBitmap
                )
            }
        }
    }

    fun dismiss(animate: Boolean = true) {
        if (!isShowing) return
        isShowing = false
        activeToken++ // отменяет ещё не завершённый PixelCopy из незакрытого show()
        originalBubble?.visibility = View.VISIBLE
        originalBubble = null
        gestureDetector = null
        val overlayContainer = container
        container = null
        val callback = onDismissCallback
        onDismissCallback = null

        if (overlayContainer == null) {
            callback?.invoke()
            return
        }
        if (!animate) {
            root.removeView(overlayContainer)
            callback?.invoke()
            return
        }
        overlayContainer.animate()
            .alpha(0f)
            .setDuration(DISMISS_DURATION_MS)
            .setInterpolator(easing)
            .withEndAction {
                root.removeView(overlayContainer)
                callback?.invoke()
            }
            .start()
    }

    private fun buildOverlay(
        bubble: View,
        actions: List<Action>,
        alignEnd: Boolean,
        onAction: (Int) -> Unit,
        bubbleLeft: Int,
        bubbleTop: Int,
        bubbleWidth: Int,
        bubbleHeight: Int,
        backdropBitmap: Bitmap,
        bubbleBitmap: Bitmap
    ) {
        val context = root.context

        val overlayContainer = FrameLayout(context).apply {
            elevation = dp(OVERLAY_ELEVATION_DP).toFloat()
            isClickable = true
            isFocusable = true
        }
        root.addView(overlayContainer, constraintMatchParentParams())
        container = overlayContainer

        val backdropView = ImageView(context).apply {
            scaleType = ImageView.ScaleType.FIT_XY
            setImageBitmap(backdropBitmap)
            setRenderEffect(
                RenderEffect.createBlurEffect(
                    dp(BLUR_RADIUS_DP).toFloat(), dp(BLUR_RADIUS_DP).toFloat(), Shader.TileMode.CLAMP
                )
            )
            alpha = 0f
        }
        overlayContainer.addView(backdropView, frameMatchParentParams())

        val scrimView = View(context).apply {
            setBackgroundColor(ContextCompat.getColor(context, R.color.scrim_overlay))
            alpha = 0f
        }
        overlayContainer.addView(scrimView, frameMatchParentParams())

        bubble.visibility = View.INVISIBLE
        val bubbleImage = ImageView(context).apply {
            setImageBitmap(bubbleBitmap)
        }
        overlayContainer.addView(
            bubbleImage,
            FrameLayout.LayoutParams(bubbleWidth, bubbleHeight).apply {
                leftMargin = bubbleLeft
                topMargin = bubbleTop
            }
        )

        val cardView = LayoutInflater.from(context)
            .inflate(R.layout.overlay_message_actions, overlayContainer, false) as MaterialCardView
        val rowsContainer = cardView.findViewById<LinearLayout>(R.id.actionsList)
        val errorColor = MaterialColors.getColor(root, androidx.appcompat.R.attr.colorError)
        actions.forEachIndexed { index, action ->
            if (action.danger && index > 0) {
                rowsContainer.addView(buildDivider(context))
            }
            rowsContainer.addView(buildActionRow(context, rowsContainer, action, errorColor) {
                onAction(action.id)
                dismiss()
            })
        }
        cardView.isClickable = true
        cardView.alpha = 0f
        cardView.scaleX = CARD_INITIAL_SCALE
        cardView.scaleY = CARD_INITIAL_SCALE
        // LayoutParams из overlay_message_actions.xml (фиксированная ширина карточки)
        // сохраняем: с WRAP_CONTENT ширина зависела бы от leftMargin и карточку
        // прижимало к правому краю экрана.
        overlayContainer.addView(cardView)

        // Синхронный measure (без layout-паса) — как в оригинальном showMessageActionMenu,
        // чтобы сразу знать итоговые размеры карточки для позиционирования.
        cardView.measure(
            View.MeasureSpec.makeMeasureSpec(cardView.layoutParams.width, View.MeasureSpec.EXACTLY),
            View.MeasureSpec.makeMeasureSpec(root.height, View.MeasureSpec.AT_MOST)
        )
        positionMenuCard(cardView, bubbleImage, bubbleLeft, bubbleTop, bubbleWidth, bubbleHeight, alignEnd)

        animateIn(backdropView, scrimView, bubbleImage, cardView, rowsContainer)

        gestureDetector = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
            override fun onSingleTapUp(e: MotionEvent): Boolean {
                dismiss()
                return true
            }

            override fun onScroll(e1: MotionEvent?, e2: MotionEvent, distanceX: Float, distanceY: Float): Boolean {
                if (kotlin.math.abs(distanceY) > kotlin.math.abs(distanceX)) {
                    dismiss()
                    return true
                }
                return false
            }
        })
        overlayContainer.setOnTouchListener { _, event ->
            gestureDetector?.onTouchEvent(event)
            true
        }

        root.performHapticFeedback(HapticFeedbackConstants.CONTEXT_CLICK)
    }

    private fun buildActionRow(
        context: android.content.Context,
        parent: LinearLayout,
        action: Action,
        errorColor: Int,
        onClick: () -> Unit
    ): TextView {
        // parent обязателен: без него inflate не создаёт LayoutParams и высота
        // строки из стиля MessageActionItem теряется — пункты слипаются.
        val row = LayoutInflater.from(context)
            .inflate(R.layout.item_message_action_row, parent, false) as TextView
        row.text = action.title
        row.setCompoundDrawablesRelativeWithIntrinsicBounds(action.icon, 0, 0, 0)
        if (action.danger) {
            row.setTextColor(errorColor)
            row.compoundDrawableTintList = ColorStateList.valueOf(errorColor)
        }
        row.alpha = 0f
        row.translationY = dp(ROW_TRANSLATION_DP).toFloat()
        row.setOnClickListener { onClick() }
        return row
    }

    private fun buildDivider(context: android.content.Context): MaterialDivider {
        return MaterialDivider(context).apply {
            layoutParams = LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT
            ).apply {
                marginStart = dp(14)
                marginEnd = dp(14)
            }
        }
    }

    private fun positionMenuCard(
        cardView: View,
        bubbleImage: View,
        bubbleLeft: Int,
        bubbleTop: Int,
        bubbleWidth: Int,
        bubbleHeight: Int,
        alignEnd: Boolean
    ) {
        val margin = dp(EDGE_MARGIN_DP)
        val gap = dp(GAP_DP)
        val screenWidth = root.width
        val screenHeight = root.height
        val cardWidth = cardView.measuredWidth
        val cardHeight = cardView.measuredHeight

        val maxX = (screenWidth - cardWidth - margin).coerceAtLeast(margin)
        val x = if (alignEnd) {
            (bubbleLeft + bubbleWidth - cardWidth).coerceIn(margin, maxX)
        } else {
            bubbleLeft.coerceIn(margin, maxX)
        }

        val spaceBelow = screenHeight - (bubbleTop + bubbleHeight) - margin
        val spaceAbove = bubbleTop - margin
        val openBelow = spaceBelow >= cardHeight || spaceBelow >= spaceAbove

        var y = if (openBelow) bubbleTop + bubbleHeight + gap else bubbleTop - gap - cardHeight
        val maxY = (screenHeight - cardHeight - margin).coerceAtLeast(margin)

        if (openBelow && y > maxY) {
            // Меню упёрлось в низ экрана — поднимаем снимок пузыря вместе с меню, как в Telegram.
            val overflow = y - maxY
            (bubbleImage.layoutParams as FrameLayout.LayoutParams).topMargin = bubbleTop - overflow
            bubbleImage.requestLayout()
            y = maxY
        }
        y = y.coerceIn(margin, maxY)

        (cardView.layoutParams as FrameLayout.LayoutParams).apply {
            leftMargin = x
            topMargin = y
        }
        cardView.requestLayout()

        cardView.pivotX = if (alignEnd) cardWidth.toFloat() else 0f
        cardView.pivotY = if (openBelow) 0f else cardHeight.toFloat()
    }

    private fun animateIn(
        backdropView: View,
        scrimView: View,
        bubbleImage: View,
        cardView: View,
        rowsContainer: LinearLayout
    ) {
        backdropView.animate().alpha(1f).setDuration(BACKDROP_DURATION_MS).setInterpolator(easing).start()
        scrimView.animate().alpha(1f).setDuration(BACKDROP_DURATION_MS).setInterpolator(easing).start()

        SpringAnimation(bubbleImage, SpringAnimation.SCALE_X, BUBBLE_LIFT_SCALE).apply {
            spring.stiffness = SPRING_STIFFNESS
            spring.dampingRatio = SPRING_DAMPING
        }.start()
        SpringAnimation(bubbleImage, SpringAnimation.SCALE_Y, BUBBLE_LIFT_SCALE).apply {
            spring.stiffness = SPRING_STIFFNESS
            spring.dampingRatio = SPRING_DAMPING
        }.start()

        cardView.animate()
            .alpha(1f)
            .scaleX(1f)
            .scaleY(1f)
            .setDuration(CARD_DURATION_MS)
            .setInterpolator(easing)
            .start()

        for (i in 0 until rowsContainer.childCount) {
            val row = rowsContainer.getChildAt(i) as? TextView ?: continue
            row.animate()
                .alpha(1f)
                .translationY(0f)
                .setStartDelay(i * ROW_STAGGER_MS)
                .setDuration(ROW_DURATION_MS)
                .setInterpolator(easing)
                .start()
        }
    }

    /** Захват прямоугольника окна (в оконных координатах) через PixelCopy — в отличие от
     *  View.draw(Canvas) корректно читает hardware-bitmap изображения (Coil грузит их именно так). */
    private fun captureWindowRegion(srcRect: Rect, targetWidth: Int, targetHeight: Int, onResult: (Bitmap) -> Unit) {
        val bitmap = Bitmap.createBitmap(targetWidth.coerceAtLeast(1), targetHeight.coerceAtLeast(1), Bitmap.Config.ARGB_8888)
        val window = (root.context as? Activity)?.window
        if (window == null || srcRect.width() <= 0 || srcRect.height() <= 0) {
            onResult(bitmap)
            return
        }
        try {
            PixelCopy.request(window, srcRect, bitmap, { onResult(bitmap) }, mainHandler)
        } catch (_: IllegalArgumentException) {
            onResult(bitmap)
        }
    }

    private fun constraintMatchParentParams(): ConstraintLayout.LayoutParams =
        ConstraintLayout.LayoutParams(
            ConstraintLayout.LayoutParams.MATCH_PARENT, ConstraintLayout.LayoutParams.MATCH_PARENT
        ).apply {
            topToTop = ConstraintLayout.LayoutParams.PARENT_ID
            bottomToBottom = ConstraintLayout.LayoutParams.PARENT_ID
            startToStart = ConstraintLayout.LayoutParams.PARENT_ID
            endToEnd = ConstraintLayout.LayoutParams.PARENT_ID
        }

    private fun frameMatchParentParams(): FrameLayout.LayoutParams =
        FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT)

    companion object {
        private const val SPRING_STIFFNESS = 1400f
        private const val SPRING_DAMPING = 0.9f
        private const val BUBBLE_LIFT_SCALE = 1.03f
        private const val BACKDROP_DURATION_MS = 220L
        private const val CARD_DURATION_MS = 260L
        private const val ROW_DURATION_MS = 160L
        private const val ROW_STAGGER_MS = 18L
        private const val DISMISS_DURATION_MS = 180L
        private const val CARD_INITIAL_SCALE = 0.85f
        private const val ROW_TRANSLATION_DP = 8
        private const val BLUR_RADIUS_DP = 18
        private const val EDGE_MARGIN_DP = 12
        private const val GAP_DP = 8
        private const val OVERLAY_ELEVATION_DP = 20
        private const val BACKDROP_SCALE = 0.25f
    }
}
