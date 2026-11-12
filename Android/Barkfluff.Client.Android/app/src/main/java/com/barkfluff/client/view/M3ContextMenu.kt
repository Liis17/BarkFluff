package com.barkfluff.client.view

import android.animation.ValueAnimator
import android.content.Context
import android.content.ContextWrapper
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.Rect
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.view.ViewGroup
import android.view.animation.PathInterpolator
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.annotation.DrawableRes
import androidx.core.graphics.ColorUtils
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import com.barkfluff.client.R
import com.google.android.material.card.MaterialCardView
import com.google.android.material.color.MaterialColors

/** Shared anchored Material 3 menu for the app's small contextual actions. */
class M3ContextMenu private constructor(
    private val root: ViewGroup,
    private val overlay: FrameLayout,
    private val menu: MaterialCardView,
    private val scrim: View,
    private val highlight: View?,
    private val backCallback: OnBackPressedCallback?,
    private val onDismiss: (() -> Unit)?,
) {
    data class Item(
        val id: Int,
        val title: CharSequence,
        @DrawableRes val icon: Int,
        val danger: Boolean = false,
        val dividerBefore: Boolean = false,
        val checked: Boolean? = null,
    )

    var isShowing: Boolean = true
        private set

    private var finishing = false

    fun dismiss(animate: Boolean = true) {
        if (!isShowing || finishing) return
        isShowing = false
        finishing = true
        backCallback?.remove()
        overlay.setOnKeyListener(null)
        if (!animate || !ValueAnimator.areAnimatorsEnabled()) {
            finishDismiss()
            return
        }
        val accelerate = PathInterpolator(0.3f, 0f, 0.8f, 0.15f)
        menu.animate()
            .alpha(0f)
            .scaleX(0.96f)
            .scaleY(0.96f)
            .setDuration(DISMISS_DURATION_MS)
            .setInterpolator(accelerate)
            .start()
        scrim.animate()
            .alpha(0f)
            .setDuration(DISMISS_DURATION_MS)
            .setInterpolator(accelerate)
            .withEndAction { finishDismiss() }
            .start()
        highlight?.animate()?.alpha(0f)?.setDuration(DISMISS_DURATION_MS)?.setInterpolator(accelerate)?.start()
    }

    private fun finishDismiss() {
        root.removeView(overlay)
        onDismiss?.invoke()
    }

    companion object {
        private const val MENU_WIDTH_DP = 280
        private const val MENU_CORNER_DP = 18
        private const val MENU_ELEVATION_DP = 3
        private const val ROW_HEIGHT_DP = 48
        private const val EDGE_MARGIN_DP = 8
        private const val MENU_GAP_DP = 8
        private const val OPEN_DURATION_MS = 200L
        private const val DISMISS_DURATION_MS = 120L
        private const val INITIAL_SCALE = 0.92f

        private val OPEN_INTERPOLATOR = PathInterpolator(0.2f, 0f, 0f, 1f)

        fun show(
            anchor: View,
            items: List<Item>,
            alignEnd: Boolean = false,
            onItemSelected: (Item) -> Unit,
            onDismiss: (() -> Unit)? = null,
            highlightView: View? = null,
        ): M3ContextMenu? {
            if (items.isEmpty() || !anchor.isAttachedToWindow) return null
            val root = anchor.rootView as? ViewGroup ?: return null
            if (root.width <= 0 || root.height <= 0) return null

            val context = anchor.context
            val density = context.resources.displayMetrics.density
            fun dp(value: Int) = (value * density).toInt()
            val edgeMargin = minOf(dp(EDGE_MARGIN_DP), root.width / 2)
            val rootLocation = IntArray(2).also(root::getLocationOnScreen)
            val anchorLocation = IntArray(2).also(anchor::getLocationOnScreen)
            val anchorRect = Rect(
                anchorLocation[0], anchorLocation[1],
                anchorLocation[0] + anchor.width, anchorLocation[1] + anchor.height,
            )
            anchorRect.offset(-rootLocation[0], -rootLocation[1])

            val insets = ViewCompat.getRootWindowInsets(root)
            val bars = insets?.getInsets(
                WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout()
            )
            val ime = insets?.getInsets(WindowInsetsCompat.Type.ime())
            val safeTop = maxOf(bars?.top ?: 0, dp(EDGE_MARGIN_DP))
            val safeBottom = root.height - maxOf(bars?.bottom ?: 0, ime?.bottom ?: 0, dp(EDGE_MARGIN_DP))
            val safeHeight = (safeBottom - safeTop).coerceAtLeast(dp(ROW_HEIGHT_DP))
            val menuWidth = minOf(dp(MENU_WIDTH_DP), (root.width - 2 * edgeMargin).coerceAtLeast(1))

            val overlay = FrameLayout(context).apply {
                isClickable = true
                isFocusable = true
                isFocusableInTouchMode = true
                descendantFocusability = ViewGroup.FOCUS_AFTER_DESCENDANTS
                setOnClickListener { /* Scrim click is handled by its own view below. */ }
            }
            val scrimColor = ColorUtils.setAlphaComponent(Color.BLACK, 0x36)
            val scrim = View(context).apply {
                setBackgroundColor(scrimColor)
                alpha = 0f
                setOnClickListener { /* Replaced after the handle is constructed. */ }
            }
            overlay.addView(scrim, FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT,
            ))

            val highlight = highlightView?.let { view ->
                val location = IntArray(2).also(view::getLocationOnScreen)
                val rect = Rect(
                    location[0] - rootLocation[0],
                    location[1] - rootLocation[1],
                    location[0] - rootLocation[0] + view.width,
                    location[1] - rootLocation[1] + view.height,
                )
                View(context).apply {
                    background = GradientDrawable().apply {
                        val primary = MaterialColors.getColor(view, androidx.appcompat.R.attr.colorPrimary)
                        setColor(ColorUtils.setAlphaComponent(primary, 0x38))
                        cornerRadius = dp(MENU_CORNER_DP).toFloat()
                    }
                    alpha = 0f
                    isClickable = false
                    layoutParams = FrameLayout.LayoutParams(rect.width(), rect.height()).apply {
                        leftMargin = rect.left
                        topMargin = rect.top
                    }
                }
            }
            highlight?.let { overlay.addView(it) }

            val scrollView = ScrollView(context).apply {
                isFillViewport = false
                clipToPadding = false
                overScrollMode = View.OVER_SCROLL_IF_CONTENT_SCROLLS
                setPadding(0, dp(6), 0, dp(6))
            }
            var menuHandle: M3ContextMenu? = null
            val rows = LinearLayout(context).apply { orientation = LinearLayout.VERTICAL }
            items.forEach { item ->
                if (item.dividerBefore) {
                    rows.addView(View(context).apply {
                        setBackgroundColor(MaterialColors.getColor(this, com.google.android.material.R.attr.colorOutlineVariant))
                        alpha = 0.55f
                    }, LinearLayout.LayoutParams(
                        LinearLayout.LayoutParams.MATCH_PARENT, dp(1),
                    ).apply {
                        marginStart = dp(12)
                        marginEnd = dp(12)
                        topMargin = dp(3)
                        bottomMargin = dp(3)
                    })
                }
                rows.addView(buildRow(context, item, ::dp) {
                    menuHandle?.dismiss()
                    onItemSelected(item)
                }, LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT, dp(ROW_HEIGHT_DP),
                ))
            }
            scrollView.addView(rows, FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.WRAP_CONTENT,
            ))

            val surfaceColor = MaterialColors.getColor(
                anchor,
                com.google.android.material.R.attr.colorSurfaceContainer,
            )
            val menu = MaterialCardView(context).apply {
                radius = dp(MENU_CORNER_DP).toFloat()
                cardElevation = dp(MENU_ELEVATION_DP).toFloat()
                setCardBackgroundColor(surfaceColor)
                strokeWidth = 0
                clipToOutline = true
                addView(scrollView, FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT,
                    FrameLayout.LayoutParams.WRAP_CONTENT,
                ))
                setOnClickListener { /* Keep taps inside the menu from reaching the scrim. */ }
                alpha = 0f
                scaleX = INITIAL_SCALE
                scaleY = INITIAL_SCALE
            }

            val menuLayoutParams = FrameLayout.LayoutParams(menuWidth, ViewGroup.LayoutParams.WRAP_CONTENT)
            overlay.addView(menu, menuLayoutParams)
            root.addView(overlay, ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT,
            ))

            menu.measure(
                View.MeasureSpec.makeMeasureSpec(menuWidth, View.MeasureSpec.EXACTLY),
                View.MeasureSpec.makeMeasureSpec(safeHeight, View.MeasureSpec.AT_MOST),
            )
            val contentHeight = menu.measuredHeight
            if (contentHeight > safeHeight) {
                scrollView.layoutParams = FrameLayout.LayoutParams(
                    FrameLayout.LayoutParams.MATCH_PARENT,
                    safeHeight,
                )
                menu.measure(
                    View.MeasureSpec.makeMeasureSpec(menuWidth, View.MeasureSpec.EXACTLY),
                    View.MeasureSpec.makeMeasureSpec(safeHeight, View.MeasureSpec.EXACTLY),
                )
            }
            val menuHeight = menu.measuredHeight.coerceAtMost(safeHeight)
            val margin = edgeMargin
            val gap = dp(MENU_GAP_DP)
            val minX = margin
            val maxX = (root.width - menuWidth - margin).coerceAtLeast(minX)
            val menuX = if (alignEnd) anchorRect.right - menuWidth else anchorRect.left
            val x = menuX.coerceIn(minX, maxX)
            val belowSpace = safeBottom - anchorRect.bottom - gap
            val aboveSpace = anchorRect.top - safeTop - gap
            val openBelow = belowSpace >= menuHeight || belowSpace >= aboveSpace
            val desiredY = if (openBelow) anchorRect.bottom + gap else anchorRect.top - gap - menuHeight
            val y = desiredY.coerceIn(safeTop, (safeBottom - menuHeight).coerceAtLeast(safeTop))
            menuLayoutParams.leftMargin = x
            menuLayoutParams.topMargin = y
            menu.layoutParams = menuLayoutParams
            menu.pivotX = (anchorRect.centerX() - x).coerceIn(0, menuWidth).toFloat()
            menu.pivotY = (anchorRect.centerY() - y).coerceIn(0, menuHeight).toFloat()

            val activity = findComponentActivity(context)
            lateinit var holder: M3ContextMenu
            val backCallback = activity?.let { activity ->
                object : OnBackPressedCallback(true) {
                    override fun handleOnBackPressed() {
                        holder.dismiss()
                    }
                }.also { callback -> activity.onBackPressedDispatcher.addCallback(callback) }
            }
            holder = M3ContextMenu(root, overlay, menu, scrim, highlight, backCallback, onDismiss)
            menuHandle = holder
            scrim.setOnClickListener { holder.dismiss() }
            overlay.setOnKeyListener { _, keyCode, event ->
                if (keyCode == KeyEvent.KEYCODE_BACK && event.action == KeyEvent.ACTION_UP) {
                    holder.dismiss()
                    true
                } else false
            }
            overlay.requestFocus()

            if (ValueAnimator.areAnimatorsEnabled()) {
                scrim.animate().alpha(1f).setDuration(OPEN_DURATION_MS).setInterpolator(OPEN_INTERPOLATOR).start()
                highlight?.animate()?.alpha(1f)?.setDuration(OPEN_DURATION_MS)?.setInterpolator(OPEN_INTERPOLATOR)?.start()
                menu.animate()
                    .alpha(1f)
                    .scaleX(1f)
                    .scaleY(1f)
                    .setDuration(OPEN_DURATION_MS)
                    .setInterpolator(OPEN_INTERPOLATOR)
                    .start()
            } else {
                scrim.alpha = 1f
                highlight?.alpha = 1f
                menu.alpha = 1f
                menu.scaleX = 1f
                menu.scaleY = 1f
            }
            return holder
        }

        private fun buildRow(
            context: Context,
            item: Item,
            dp: (Int) -> Int,
            onClick: () -> Unit,
        ): View {
            val row = LinearLayout(context).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
                setPadding(dp(16), 0, dp(16), 0)
                isClickable = true
                isFocusable = true
                val value = TypedValue()
                if (context.theme.resolveAttribute(android.R.attr.selectableItemBackground, value, true)) {
                    setBackgroundResource(value.resourceId)
                }
                setOnClickListener { onClick() }
            }
            val color = MaterialColors.getColor(
                row,
                if (item.danger) androidx.appcompat.R.attr.colorError
                else com.google.android.material.R.attr.colorOnSurfaceVariant,
            )
            row.addView(ImageView(context).apply {
                setImageResource(item.icon)
                imageTintList = ColorStateList.valueOf(color)
                scaleType = ImageView.ScaleType.CENTER_INSIDE
                contentDescription = null
                importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
            }, LinearLayout.LayoutParams(dp(24), dp(24)).apply { marginEnd = dp(12) })

            row.addView(TextView(context).apply {
                text = item.title
                setTextAppearance(com.google.android.material.R.style.TextAppearance_Material3_BodyLarge)
                setTextColor(if (item.danger) MaterialColors.getColor(row, androidx.appcompat.R.attr.colorError)
                else MaterialColors.getColor(row, com.google.android.material.R.attr.colorOnSurface))
                maxLines = 1
                ellipsize = android.text.TextUtils.TruncateAt.END
            }, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))

            if (item.checked == true) {
                row.addView(ImageView(context).apply {
                    setImageResource(R.drawable.ic_check)
                    imageTintList = ColorStateList.valueOf(
                        MaterialColors.getColor(row, androidx.appcompat.R.attr.colorPrimary)
                    )
                    scaleType = ImageView.ScaleType.CENTER_INSIDE
                    importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
                }, LinearLayout.LayoutParams(dp(24), dp(24)).apply { marginStart = dp(8) })
            } else if (item.checked == false) {
                row.addView(View(context), LinearLayout.LayoutParams(dp(24), dp(24)).apply { marginStart = dp(8) })
            }
            return row
        }

        private fun findComponentActivity(context: Context): ComponentActivity? {
            var current: Context? = context
            while (current is ContextWrapper) {
                if (current is ComponentActivity) return current
                current = current.baseContext
            }
            return current as? ComponentActivity
        }
    }

}
