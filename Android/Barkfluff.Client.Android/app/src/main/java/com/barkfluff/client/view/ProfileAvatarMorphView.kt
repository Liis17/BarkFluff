package com.barkfluff.client.view

import android.content.Context
import android.graphics.Canvas
import android.graphics.Path
import android.graphics.RectF
import android.util.AttributeSet
import androidx.appcompat.widget.AppCompatImageView
import kotlin.math.max

/** Draws the avatar inside explicit header coordinates, without transforming the view. */
class ProfileAvatarMorphView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
) : AppCompatImageView(context, attrs) {
    private val avatarBounds = RectF()
    private val clipPath = Path()
    private var radius = 0f

    fun setAvatarBounds(left: Float, top: Float, size: Float, cornerRadius: Float) {
        avatarBounds.set(left, top, left + size, top + size)
        radius = cornerRadius
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        val image = drawable ?: return
        if (avatarBounds.isEmpty) return
        val imageWidth = image.intrinsicWidth.takeIf { it > 0 } ?: 1
        val imageHeight = image.intrinsicHeight.takeIf { it > 0 } ?: 1
        val scale = max(avatarBounds.width() / imageWidth, avatarBounds.height() / imageHeight)
        val saved = canvas.save()
        clipPath.reset()
        clipPath.addRoundRect(avatarBounds, radius, radius, Path.Direction.CW)
        canvas.clipPath(clipPath)
        canvas.translate(
            avatarBounds.centerX() - imageWidth * scale / 2f,
            avatarBounds.centerY() - imageHeight * scale / 2f,
        )
        canvas.scale(scale, scale)
        image.setBounds(0, 0, imageWidth, imageHeight)
        image.draw(canvas)
        canvas.restoreToCount(saved)
    }
}
