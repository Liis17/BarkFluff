package com.barkfluff.client.calls

import android.content.Context
import android.graphics.Color
import android.graphics.Outline
import android.graphics.PorterDuff
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewOutlineProvider
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import coil.load
import coil.transform.CircleCropTransformation
import com.barkfluff.client.R
import com.barkfluff.client.utils.AvatarLoader
import io.livekit.android.renderer.SurfaceViewRenderer
import io.livekit.android.room.track.VideoTrack

/**
 * Плитка одного участника звонка. Два режима:
 *  • tile (сетка группового звонка) — карточка с закруглением, аватар по центру, снизу имя + статус мик.;
 *  • hero (1-на-1) — крупный аватар + имя по центру, без карточки.
 * При включённой камере/демонстрации показывает [SurfaceViewRenderer] на весь размер.
 */
class CallTileView(context: Context) : FrameLayout(context) {

    val renderer = SurfaceViewRenderer(context)

    private val avatarBlock: LinearLayout
    private val avatarFrame: FrameLayout
    private val avatarImage: ImageView
    private val avatarInitials: TextView
    private val waveform: WaveformView
    private val subtitle: TextView
    private val heroName: TextView
    private val bottomBar: LinearLayout
    private val nameLabel: TextView
    private val micChip: ImageView

    // Семантические акценты — фиксированные; нейтральные — из системной темы MD3.
    private val speakingColor = 0xFF43D67C.toInt()
    private val mutedColor = resolveColor(androidx.appcompat.R.attr.colorError)
    private val tileBg = resolveColor(com.google.android.material.R.attr.colorSurfaceContainerHigh)
    private val onSurface = resolveColor(com.google.android.material.R.attr.colorOnSurface)
    private val onSurfaceVariant = resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant)

    private val speakingBorder = GradientDrawable().apply {
        setColor(Color.TRANSPARENT)
        cornerRadius = dp(20).toFloat()
        setStroke(dp(2), speakingColor)
    }

    private var boundTrack: VideoTrack? = null
    private var rendererInitialized = false
    private var hero = false

    init {
        clipToOutline = true
        outlineProvider = object : ViewOutlineProvider() {
            override fun getOutline(view: View, outline: Outline) {
                val r = if (hero) 0f else dp(20).toFloat()
                outline.setRoundRect(0, 0, view.width, view.height, r)
            }
        }

        renderer.visibility = View.INVISIBLE
        addView(renderer, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))

        // Центральный блок: аватар + waveform/субтитр
        avatarBlock = LinearLayout(context).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
        }

        avatarFrame = FrameLayout(context)
        avatarImage = ImageView(context).apply { scaleType = ImageView.ScaleType.CENTER_CROP }
        avatarInitials = TextView(context).apply {
            gravity = Gravity.CENTER
            setTextColor(Color.WHITE)
            typeface = android.graphics.Typeface.create("sans-serif-medium", android.graphics.Typeface.NORMAL)
        }
        avatarFrame.addView(avatarImage, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))
        avatarFrame.addView(avatarInitials, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT))
        avatarBlock.addView(avatarFrame, LinearLayout.LayoutParams(dp(64), dp(64)))

        heroName = TextView(context).apply {
            setTextColor(onSurface)
            textSize = 28f
            gravity = Gravity.CENTER
            typeface = android.graphics.Typeface.create("sans-serif", android.graphics.Typeface.NORMAL)
            visibility = View.GONE
            setPadding(dp(16), dp(20), dp(16), 0)
        }
        avatarBlock.addView(heroName, LinearLayout.LayoutParams(
            LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT))

        waveform = WaveformView(context).apply {
            barColor = speakingColor
            visibility = View.GONE
        }
        avatarBlock.addView(waveform, LinearLayout.LayoutParams(
            LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT).apply { topMargin = dp(10) })

        subtitle = TextView(context).apply {
            setTextColor(onSurfaceVariant)
            textSize = 11f
            visibility = View.GONE
        }
        avatarBlock.addView(subtitle, LinearLayout.LayoutParams(
            LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT).apply { topMargin = dp(10) })

        addView(avatarBlock, LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT, Gravity.CENTER))

        // Нижняя панель плитки: имя + чип микрофона
        nameLabel = TextView(context).apply {
            setTextColor(onSurface)
            textSize = 13f
            maxLines = 1
            ellipsize = android.text.TextUtils.TruncateAt.END
            typeface = android.graphics.Typeface.create("sans-serif-medium", android.graphics.Typeface.NORMAL)
        }
        micChip = ImageView(context).apply {
            setBackgroundResource(R.drawable.bg_call_mic_chip)
            val p = dp(5)
            setPadding(p, p, p, p)
        }
        bottomBar = LinearLayout(context).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            addView(nameLabel, LinearLayout.LayoutParams(0, LayoutParams.WRAP_CONTENT, 1f))
            addView(micChip, LinearLayout.LayoutParams(dp(22), dp(22)))
        }
        addView(bottomBar, LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.WRAP_CONTENT,
            Gravity.BOTTOM).apply {
            setMargins(dp(12), 0, dp(12), dp(10))
        })
    }

    fun setHero(value: Boolean) {
        if (hero == value) return
        hero = value
        invalidateOutline()
        if (value) {
            background = null
            bottomBar.visibility = View.GONE
            heroName.visibility = View.VISIBLE
            avatarFrame.layoutParams = LinearLayout.LayoutParams(dp(112), dp(112))
            avatarInitials.textSize = 40f
        } else {
            setBackgroundColor(tileBg)
            bottomBar.visibility = View.VISIBLE
            heroName.visibility = View.GONE
            avatarFrame.layoutParams = LinearLayout.LayoutParams(dp(64), dp(64))
            avatarInitials.textSize = 22f
        }
        avatarFrame.requestLayout()
    }

    fun bind(spec: CallTile, info: TileInfo, engine: LiveKitCallEngine) {
        val displayName = info.displayName
        nameLabel.text = displayName
        heroName.text = displayName

        // Аватар: цветной круг с инициалами как фон, поверх — картинка (если есть).
        // На ошибке загрузки картинка остаётся прозрачной → видны инициалы.
        avatarInitials.background = GradientDrawable().apply {
            shape = GradientDrawable.OVAL
            setColor(info.accentColor)
        }
        avatarInitials.text = AvatarLoader.getInitials(displayName)
        if (info.avatarUrl.isNullOrBlank()) {
            avatarImage.setImageDrawable(null)
        } else {
            avatarImage.load(info.avatarUrl) {
                crossfade(150)
                transformations(CircleCropTransformation())
            }
        }

        val speaking = spec.participant.isSpeaking && !spec.isScreen
        val hasVideo = spec.track != null
        attachTrack(spec.track, engine)

        if (hasVideo) {
            avatarBlock.visibility = View.GONE
        } else {
            avatarBlock.visibility = View.VISIBLE
            waveform.setActive(speaking)
            if (!hero) {
                subtitle.visibility = if (speaking) View.GONE else View.VISIBLE
                subtitle.text = if (spec.isScreen) "демонстрация" else "молчит"
            } else {
                subtitle.visibility = View.GONE
            }
        }

        // Чип микрофона
        val micOff = !spec.participant.micEnabled
        micChip.setImageResource(if (micOff) R.drawable.ic_mic_off else R.drawable.ic_mic)
        val chipTint = when {
            micOff -> mutedColor
            speaking -> speakingColor
            else -> onSurfaceVariant
        }
        micChip.setColorFilter(chipTint, PorterDuff.Mode.SRC_IN)
        micChip.background.mutate().setTint((chipTint and 0x00FFFFFF) or 0x33000000)

        // Speaking-рамка (только в режиме плитки)
        foreground = if (speaking && !hero) speakingBorder else null
    }

    private fun attachTrack(track: VideoTrack?, engine: LiveKitCallEngine) {
        if (track === boundTrack) {
            renderer.visibility = if (track != null) View.VISIBLE else View.INVISIBLE
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
        } else {
            renderer.visibility = View.INVISIBLE
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

/** Разрешённые данные участника для отрисовки плитки (имя/аватар резолвятся в CallActivity). */
data class TileInfo(
    val displayName: String,
    val avatarUrl: String?,
    val userId: Long,
    val accentColor: Int
)

/** Описание одной плитки в раскладке экрана разговора. */
data class CallTile(
    val key: String,
    val participant: CallParticipant,
    val track: VideoTrack?,
    val isScreen: Boolean
)
