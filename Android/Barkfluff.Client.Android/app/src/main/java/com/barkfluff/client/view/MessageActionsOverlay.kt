package com.barkfluff.client.view

import android.graphics.Point
import android.view.View
import androidx.annotation.DrawableRes
import androidx.constraintlayout.widget.ConstraintLayout

/** Message-action adapter that keeps chat-specific selection and dismissal callbacks out of the menu. */
class MessageActionsOverlay(@Suppress("unused") root: ConstraintLayout) {
    data class Action(
        val id: Int,
        @DrawableRes val icon: Int,
        val title: CharSequence,
        val danger: Boolean = false,
        val dividerBefore: Boolean = false,
    )

    private var menu: M3ContextMenu? = null
    private var onDismissCallback: (() -> Unit)? = null

    val isShowing: Boolean
        get() = menu?.isShowing == true

    fun show(
        bubble: View,
        actions: List<Action>,
        alignEnd: Boolean,
        barActions: List<Action> = emptyList(),
        touchPoint: Point? = null,
        onDismiss: (() -> Unit)? = null,
        onAction: (Int) -> Unit,
    ) {
        dismiss(animate = false)
        onDismissCallback = onDismiss
        menu = M3ContextMenu.show(
            anchor = bubble,
            items = actions.map { action ->
                M3ContextMenu.Item(
                    id = action.id,
                    title = action.title,
                    icon = action.icon,
                    danger = action.danger,
                    dividerBefore = action.dividerBefore || action.danger,
                )
            },
            barItems = barActions.map { M3ContextMenu.Item(id = it.id, title = it.title, icon = it.icon, danger = it.danger) },
            touchPoint = touchPoint,
            alignEnd = alignEnd,
            onItemSelected = { onAction(it.id) },
            highlightView = bubble,
            onDismiss = {
                menu = null
                val callback = onDismissCallback
                onDismissCallback = null
                callback?.invoke()
            },
        )
    }

    fun dismiss(animate: Boolean = true) {
        menu?.dismiss(animate)
    }
}
