package com.barkfluff.client.views

import android.content.Context
import android.util.AttributeSet
import androidx.appcompat.widget.AppCompatImageView

/**
 * ImageView у которого высота всегда равна ширине (квадрат 1:1).
 */
class SquareImageView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
    defStyleAttr: Int = 0
) : AppCompatImageView(context, attrs, defStyleAttr) {

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        // Force AT_MOST → EXACTLY so the cell always fills its span width,
        // regardless of the loaded image's intrinsic size.
        val w = MeasureSpec.getSize(widthMeasureSpec)
        val exactSpec = if (MeasureSpec.getMode(widthMeasureSpec) == MeasureSpec.UNSPECIFIED)
            widthMeasureSpec
        else
            MeasureSpec.makeMeasureSpec(w, MeasureSpec.EXACTLY)
        super.onMeasure(exactSpec, exactSpec)
    }
}
