package com.barkfluff.client.calls

import android.content.Context
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.TextView
import com.barkfluff.client.R
import io.livekit.android.renderer.SurfaceViewRenderer
import io.livekit.android.room.track.VideoTrack

/**
 * Плитка одного участника (камера или демонстрация экрана). Держит свой [SurfaceViewRenderer]
 * на весь жизненный цикл; renderer инициализируется один раз (EGL-контекст Room).
 */
class CallTileView(context: Context) : FrameLayout(context) {

    val renderer = SurfaceViewRenderer(context)
    private val placeholder: TextView
    private val nameLabel: TextView
    private val micIcon: ImageView
    private val speakingBorder = GradientDrawable().apply {
        setColor(Color.TRANSPARENT)
        setStroke(dp(3), resolveColor(androidx.appcompat.R.attr.colorPrimary))
    }

    private var boundTrack: VideoTrack? = null
    private var rendererInitialized = false

    init {
        setBackgroundColor(resolveColor(com.google.android.material.R.attr.colorSurfaceContainerHigh))

        renderer.visibility = View.INVISIBLE
        addView(renderer, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))

        placeholder = TextView(context).apply {
            gravity = Gravity.CENTER
            setTextAppearance(android.R.style.TextAppearance_Material_Body1)
            setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
        }
        addView(placeholder, LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT, Gravity.CENTER))

        nameLabel = TextView(context).apply {
            setTextColor(Color.WHITE)
            setBackgroundColor(0x66000000)
            setPadding(dp(8), dp(2), dp(8), dp(2))
            maxLines = 1
        }
        addView(nameLabel, LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT, Gravity.BOTTOM or Gravity.START).apply {
            marginStart = dp(8)
            bottomMargin = dp(8)
        })

        micIcon = ImageView(context).apply {
            setImageResource(R.drawable.ic_mic_off)
            setColorFilter(Color.WHITE)
            setBackgroundColor(0x66000000)
            setPadding(dp(4), dp(4), dp(4), dp(4))
            visibility = View.GONE
        }
        addView(micIcon, LayoutParams(dp(28), dp(28), Gravity.BOTTOM or Gravity.END).apply {
            marginEnd = dp(8)
            bottomMargin = dp(8)
        })
    }

    fun bind(spec: CallTile, engine: LiveKitCallEngine) {
        nameLabel.text = if (spec.isScreen) "${spec.participant.name} · экран" else spec.participant.name
        micIcon.visibility = if (!spec.isScreen && !spec.participant.micEnabled) View.VISIBLE else View.GONE
        placeholder.text = if (spec.isScreen) "Демонстрация экрана" else "Камера выключена"

        attachTrack(spec.track, engine)
        foreground = if (spec.participant.isSpeaking && !spec.isScreen) speakingBorder else null
    }

    private fun attachTrack(track: VideoTrack?, engine: LiveKitCallEngine) {
        if (track === boundTrack) {
            renderer.visibility = if (track != null) View.VISIBLE else View.INVISIBLE
            placeholder.visibility = if (track != null) View.GONE else View.VISIBLE
            return
        }
        boundTrack?.removeRenderer(renderer)
        boundTrack = track
        if (track != null) {
            if (!rendererInitialized) {
                engine.initRenderer(renderer)
                rendererInitialized = true
            }
            track.addRenderer(renderer)
            renderer.visibility = View.VISIBLE
            placeholder.visibility = View.GONE
        } else {
            renderer.visibility = View.INVISIBLE
            placeholder.visibility = View.VISIBLE
        }
    }

    fun release() {
        boundTrack?.removeRenderer(renderer)
        boundTrack = null
        runCatching { renderer.release() }
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun resolveColor(attr: Int): Int {
        val out = TypedValue()
        context.theme.resolveAttribute(attr, out, true)
        return out.data
    }
}

/** Описание одной плитки в раскладке экрана разговора. */
data class CallTile(
    val key: String,
    val participant: CallParticipant,
    val track: VideoTrack?,
    val isScreen: Boolean
)
