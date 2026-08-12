package com.barkfluff.client.calls

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.telecom.DisconnectCause
import android.graphics.Color
import android.graphics.PorterDuff
import android.media.projection.MediaProjectionManager
import android.os.Bundle
import android.util.Log
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.core.view.updateLayoutParams
import androidx.core.view.updatePadding
import androidx.lifecycle.lifecycleScope
import barkfluff.calls.CallsApiOuterClass
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.utils.AvatarLoader
import com.google.android.material.bottomsheet.BottomSheetDialog
import com.google.android.material.button.MaterialButton
import com.twilio.audioswitch.AudioDevice
import io.livekit.android.room.track.VideoQuality
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class CallActivity : AppCompatActivity(), LiveKitCallEngine.Listener {

    private lateinit var callId: String
    private lateinit var livekitUrl: String
    private lateinit var accessToken: String
    private lateinit var mediaType: String
    private lateinit var callTitle: String
    private lateinit var callEngine: LiveKitCallEngine

    private lateinit var callRoot: FrameLayout
    private lateinit var callContent: FrameLayout
    private lateinit var selfMiniContainer: FrameLayout
    private lateinit var topBar: LinearLayout
    private lateinit var callTitleText: TextView
    private lateinit var statusText: TextView
    private lateinit var statusDot: View
    private lateinit var participantBadge: LinearLayout
    private lateinit var participantCount: TextView
    private lateinit var controlBar: LinearLayout

    private lateinit var micButton: ImageView
    private lateinit var cameraButton: ImageView
    private lateinit var screenButton: ImageView

    private val tileViews = HashMap<String, CallTileView>()
    private val infoCache = HashMap<String, TileInfo>()
    private val resolving = HashSet<String>()
    private var lastLayoutSignature: String? = null
    private var lastParticipants: List<CallParticipant> = emptyList()
    private var focusedKey: String? = null

    private var pendingCameraToggleAfterPermission = false
    private var callEnded = false
    private var callStartedAtMs = 0L
    private var callTimerJob: Job? = null
    private var reconnectJob: Job? = null
    private var reconnectAttempt = 0
    private var lastForegroundCamera = false
    private var lastForegroundScreen = false
    private var desiredMicEnabled = true
    private var desiredCameraEnabled = false
    private var desiredScreenShareEnabled = false
    private var batteryOptimizationPromptedForSession = false

    private val speakingColor = 0xFF43D67C.toInt()

    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val initialGranted = requiredInitialPermissions().all { permissions[it] == true || hasPermission(it) }
        val cameraGranted = hasPermission(Manifest.permission.CAMERA)

        if (pendingCameraToggleAfterPermission) {
            pendingCameraToggleAfterPermission = false
            if (cameraGranted) toggleCamera()
            return@registerForActivityResult
        }

        if (initialGranted) {
            connectToLiveKit()
        } else {
            statusText.text = getString(R.string.call_permission_required)
        }
    }

    private val screenShareLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        val data = result.data
        if (result.resultCode != Activity.RESULT_OK || data == null) return@registerForActivityResult

        lifecycleScope.launch {
            callEngine.setScreenShareEnabled(true, data)
                .onSuccess {
                    desiredScreenShareEnabled = true
                    updateCallForegroundService(
                        cameraEnabled = lastParticipants.firstOrNull { it.isLocal }?.cameraEnabled ?: desiredCameraEnabled,
                        screenShareEnabled = true
                    )
                }
                .onFailure {
                    Toast.makeText(this@CallActivity, R.string.call_screen_share_enable_failed, Toast.LENGTH_SHORT).show()
                }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        livekitUrl = intent.getStringExtra(CallExtras.EXTRA_LIVEKIT_URL).orEmpty().ifBlank { GlobalParam(this).livekitUrl }
        accessToken = intent.getStringExtra(CallExtras.EXTRA_ACCESS_TOKEN).orEmpty()
        mediaType = intent.getStringExtra(CallExtras.EXTRA_MEDIA_TYPE).orEmpty()
        callTitle = intent.getStringExtra(CallExtras.EXTRA_CALLER_NAME).orEmpty().ifBlank {
            getString(R.string.call_title_default)
        }
        desiredCameraEnabled = isVideoCall()

        if (callId.isBlank()) {
            finish()
            return
        }

        callEngine = LiveKitCallEngine(applicationContext, lifecycleScope, this)
        setContentView(R.layout.activity_call)
        bindViews()
        setupWindowInsets()
        buildControls()
        observeParticipants()
        observeCallEvents()

        if (livekitUrl.isBlank() || accessToken.isBlank()) {
            statusText.text = getString(R.string.call_connection_data_missing)
            return
        }

        requestInitialPermissionsOrConnect()
    }

    private fun bindViews() {
        callRoot = findViewById(R.id.callRoot)
        callContent = findViewById(R.id.callContent)
        selfMiniContainer = findViewById(R.id.selfMiniContainer)
        topBar = findViewById(R.id.topBar)
        callTitleText = findViewById(R.id.callTitleText)
        statusText = findViewById(R.id.statusText)
        statusDot = findViewById(R.id.statusDot)
        participantBadge = findViewById(R.id.participantBadge)
        participantCount = findViewById(R.id.participantCount)
        controlBar = findViewById(R.id.controlBar)

        // Фон экрана — мягкий градиент из системных surface-цветов (адаптируется под тему)
        callRoot.background = android.graphics.drawable.GradientDrawable(
            android.graphics.drawable.GradientDrawable.Orientation.TOP_BOTTOM,
            intArrayOf(
                resolveColor(com.google.android.material.R.attr.colorSurface),
                resolveColor(com.google.android.material.R.attr.colorSurfaceContainerLow)
            )
        )

        callTitleText.text = callTitle
        statusText.text = getString(
            if (isVideoCall()) R.string.call_connecting_video else R.string.call_connecting_audio
        )
        statusDot.background.mutate().setTint(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))

        // Закруглённое стеклянное мини-окно «Вы»
        selfMiniContainer.clipToOutline = true
        selfMiniContainer.outlineProvider = object : android.view.ViewOutlineProvider() {
            override fun getOutline(view: View, outline: android.graphics.Outline) {
                outline.setRoundRect(0, 0, view.width, view.height, dp(18).toFloat())
            }
        }
    }

    private fun setupWindowInsets() {
        WindowCompat.setDecorFitsSystemWindows(window, false)
        // Светлые/тёмные иконки системных баров — по яркости системного фона (адаптация под тему MD3)
        val lightBars = androidx.core.graphics.ColorUtils.calculateLuminance(
            resolveColor(com.google.android.material.R.attr.colorSurface)) > 0.5
        WindowInsetsControllerCompat(window, callRoot).apply {
            isAppearanceLightStatusBars = lightBars
            isAppearanceLightNavigationBars = lightBars
        }
        val topBarPadTop = topBar.paddingTop
        val controlBottomMargin = (controlBar.layoutParams as ViewGroup.MarginLayoutParams).bottomMargin
        ViewCompat.setOnApplyWindowInsetsListener(callRoot) { _, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            topBar.updatePadding(top = topBarPadTop + bars.top)
            controlBar.updateLayoutParams<ViewGroup.MarginLayoutParams> {
                bottomMargin = controlBottomMargin + bars.bottom
            }
            // Контент участников не залезает под верхнюю панель и панель управления
            callContent.updatePadding(
                top = bars.top + dp(56),
                bottom = bars.bottom + dp(108),
                left = dp(12),
                right = dp(12)
            )
            selfMiniContainer.updateLayoutParams<ViewGroup.MarginLayoutParams> {
                topMargin = bars.top + dp(52)
                marginEnd = dp(16)
            }
            insets
        }
    }

    // region Панель управления

    private fun buildControls() {
        controlBar.removeAllViews()
        micButton = addControl(R.drawable.ic_mic, getString(R.string.call_control_microphone)) { toggleMicrophone() }
        cameraButton = addControl(R.drawable.ic_video, getString(R.string.call_control_camera)) { toggleCamera() }
        addControl(R.drawable.ic_call_end, getString(R.string.call_control_end), big = true) { endCallAndClose() }
        screenButton = addControl(R.drawable.ic_screen_share, getString(R.string.call_control_screen_share)) { startScreenShare() }
        addControl(R.drawable.ic_more_vert, getString(R.string.call_control_more)) { showMoreSheet() }
    }

    private fun addControl(icon: Int, label: String, big: Boolean = false, onClick: () -> Unit): ImageView {
        val container = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
        }
        val size = if (big) dp(60) else dp(52)
        val pad = dp(if (big) 16 else 15)
        val button = ImageView(this).apply {
            setImageResource(icon)
            scaleType = ImageView.ScaleType.FIT_CENTER
            setBackgroundResource(if (big) R.drawable.bg_call_btn_end else R.drawable.bg_call_btn_circle)
            setColorFilter(
                if (big) Color.WHITE else resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant),
                PorterDuff.Mode.SRC_IN
            )
            setPadding(pad, pad, pad, pad)
            isClickable = true
            isFocusable = true
            contentDescription = label
            setOnClickListener { onClick() }
        }
        container.addView(button, LinearLayout.LayoutParams(size, size))
        container.addView(TextView(this).apply {
            text = label
            textSize = 11f
            setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
            gravity = Gravity.CENTER
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
            topMargin = dp(7)
        })
        controlBar.addView(container, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        return button
    }

    // endregion

    private fun observeParticipants() {
        lifecycleScope.launch {
            callEngine.participants.collect { participants ->
                lastParticipants = participants
                renderTiles(participants)
            }
        }
    }

    private fun observeCallEvents() {
        val app = application as BarkFluffApplication
        lifecycleScope.launch {
            app.callEventsService.events.collect { event ->
                val endedId = when (event.eventCase) {
                    CallsApiOuterClass.CallEvent.EventCase.ENDED -> event.ended.callId
                    CallsApiOuterClass.CallEvent.EventCase.REJECTED -> event.rejected.callId
                    else -> null
                }
                if (endedId == callId) closeOnRemoteEnd()
            }
        }
    }

    private fun closeOnRemoteEnd() {
        if (callEnded) return
        callEnded = true
        reconnectJob?.cancel()
        stopCallTimer()
        callEngine.disconnect()
        CallForegroundService.stop(this)
        NotificationHelper.dismissCall(this, callId)
        CallTelecomRegistry.disconnect(callId, DisconnectCause.REMOTE)
        statusText.text = getString(R.string.call_ended)
        finish()
    }

    private fun connectToLiveKit() {
        lifecycleScope.launch {
            CallTelecomRegistry.markActive(callId)
            updateCallForegroundService(
                cameraEnabled = desiredCameraEnabled && hasPermission(Manifest.permission.CAMERA),
                screenShareEnabled = false
            )
            if (!connectOnce()) {
                scheduleLiveKitReconnect(getString(R.string.call_connection_failed))
            }
        }
    }

    private suspend fun connectOnce(): Boolean {
        Log.d(TAG, "connectToLiveKit: url=$livekitUrl, tokenLen=${accessToken.length}, callId=$callId")
        return callEngine.connect(
            livekitUrl = livekitUrl,
            accessToken = accessToken,
            cameraOnStart = desiredCameraEnabled && hasPermission(Manifest.permission.CAMERA)
        ).onSuccess {
            restoreLocalMediaState()
            reconnectAttempt = 0
        }.onFailure {
            Log.e(TAG, "connectToLiveKit failed: callEnded=$callEnded, finishing=$isFinishing", it)
            if (!callEnded && !isFinishing) {
                statusText.text = getString(R.string.call_connection_failed)
                Toast.makeText(this@CallActivity, R.string.call_livekit_connection_error, Toast.LENGTH_SHORT).show()
            }
        }.isSuccess
    }

    private suspend fun restoreLocalMediaState() {
        if (!desiredMicEnabled) {
            callEngine.setMicrophoneEnabled(false)
        }
        if (desiredCameraEnabled && hasPermission(Manifest.permission.CAMERA)) {
            callEngine.setCameraEnabled(true)
        }
        desiredScreenShareEnabled = false
    }

    private fun scheduleLiveKitReconnect(reason: String) {
        if (callEnded || isFinishing || isDestroyed) return
        if (reconnectJob?.isActive == true) return

        reconnectJob = lifecycleScope.launch {
            callEngine.disconnect()
            desiredScreenShareEnabled = false
            updateCallForegroundService(
                cameraEnabled = desiredCameraEnabled && hasPermission(Manifest.permission.CAMERA),
                screenShareEnabled = false
            )

            while (!callEnded && !isFinishing && reconnectAttempt < MAX_RECONNECT_ATTEMPTS) {
                reconnectAttempt++
                val delayMs = reconnectDelayMs(reconnectAttempt)
                statusText.text = getString(
                    R.string.call_reconnect_attempt,
                    reason,
                    reconnectAttempt,
                    MAX_RECONNECT_ATTEMPTS
                )
                delay(delayMs)

                if (!refreshLiveKitCredentialsForReconnect()) {
                    continue
                }

                if (connectOnce()) {
                    reconnectAttempt = 0
                    return@launch
                }
            }

            if (!callEnded && !isFinishing) {
                statusDot.background.mutate().setTint(resolveColor(androidx.appcompat.R.attr.colorError))
                statusText.text = getString(R.string.call_reconnect_failed)
            }
        }
    }

    private suspend fun refreshLiveKitCredentialsForReconnect(): Boolean {
        if (!ensureCallsClient()) return false

        val app = application as BarkFluffApplication
        app.grpcManager.ensureTokenValid(this)
        return app.callRepository.join(callId)
            .onSuccess { response ->
                livekitUrl = response.livekitUrl.ifBlank { livekitUrl.ifBlank { GlobalParam(this).livekitUrl } }
                accessToken = response.accessToken
                Log.d(TAG, "LiveKit reconnect credentials refreshed: callId=$callId")
            }
            .onFailure {
                Log.w(TAG, "JoinCall failed during LiveKit reconnect: callId=$callId", it)
            }
            .isSuccess
    }

    private fun reconnectDelayMs(attempt: Int): Long =
        when (attempt) {
            1 -> 2_000L
            2 -> 4_000L
            3 -> 8_000L
            4 -> 15_000L
            else -> 30_000L
        }

    // region Раскладка плиток

    private fun renderTiles(participants: List<CallParticipant>) {
        val local = participants.firstOrNull { it.isLocal }
        val remotes = participants.filter { !it.isLocal }
        val screenParticipant = participants.firstOrNull { it.screenTrack != null }

        val screenTile = screenParticipant?.let {
            CallTile("scr:${it.identity}", it, it.screenTrack, isScreen = true)
        }
        val cameraTiles = participants.map { CallTile("cam:${it.identity}", it, it.cameraTrack, isScreen = false) }

        val focused = focusedKey
        var mode = "grid"
        var heroTile: CallTile? = null
        var selfTile: CallTile? = null
        var bigTiles: List<CallTile> = emptyList()
        var stripTiles: List<CallTile> = emptyList()

        when {
            focused != null && (screenTile?.key == focused || cameraTiles.any { it.key == focused }) -> {
                mode = "stage"
                bigTiles = listOf(screenTile?.takeIf { it.key == focused } ?: cameraTiles.first { it.key == focused })
            }
            screenTile != null -> {
                mode = "stage"; bigTiles = listOf(screenTile); stripTiles = cameraTiles
            }
            remotes.size == 1 && local != null -> {
                mode = "single"
                heroTile = cameraTiles.first { it.participant.identity == remotes[0].identity }
                selfTile = cameraTiles.first { it.participant.isLocal }
            }
            else -> {
                mode = "grid"; bigTiles = cameraTiles
            }
        }

        val visible = (bigTiles + stripTiles + listOfNotNull(heroTile, selfTile))
        val signature = "$mode|" + visible.joinToString(",") { it.key }
        if (signature != lastLayoutSignature) {
            rebuildLayout(mode, bigTiles, stripTiles, heroTile, selfTile)
            lastLayoutSignature = signature
        }
        visible.forEach { tile ->
            tileViews[tile.key]?.bind(tile, infoFor(tile.participant), callEngine)
        }

        val visibleKeys = visible.map { it.key }.toSet()
        (tileViews.keys - visibleKeys).forEach { tileViews.remove(it)?.release() }

        updateTopBar(participants)
        updateControlStates(participants)
        renderCallDuration()
    }

    private fun rebuildLayout(
        mode: String,
        bigTiles: List<CallTile>,
        stripTiles: List<CallTile>,
        heroTile: CallTile?,
        selfTile: CallTile?
    ) {
        callContent.removeAllViews()
        selfMiniContainer.removeAllViews()
        selfMiniContainer.visibility = View.GONE

        when (mode) {
            "single" -> {
                if (heroTile != null) {
                    val hero = tileFor(heroTile).also { it.setHero(true) }
                    callContent.addView(hero, FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
                }
                if (selfTile != null) {
                    val self = tileFor(selfTile).also { it.setHero(false) }
                    selfMiniContainer.addView(self, FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
                    selfMiniContainer.visibility = View.VISIBLE
                }
            }
            "stage" -> {
                val column = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
                column.addView(tileFor(bigTiles.first()).also { it.setHero(false) }, weightParams(3f))
                if (stripTiles.isNotEmpty()) {
                    val strip = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
                    stripTiles.forEach { strip.addView(tileFor(it).also { v -> v.setHero(false) }, rowItemParams()) }
                    column.addView(strip, weightParams(1f))
                }
                callContent.addView(column, FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
            }
            else -> {
                val column = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
                addGrid(column, bigTiles)
                callContent.addView(column, FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
            }
        }
    }

    private fun addGrid(parent: LinearLayout, tiles: List<CallTile>) {
        if (tiles.isEmpty()) return
        val cols = if (tiles.size <= 1) 1 else 2
        var i = 0
        while (i < tiles.size) {
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            var c = 0
            while (c < cols && i < tiles.size) {
                row.addView(tileFor(tiles[i]).also { it.setHero(false) }, rowItemParams())
                i++; c++
            }
            parent.addView(row, weightParams(1f))
        }
    }

    private fun tileFor(tile: CallTile): CallTileView {
        val view = tileViews.getOrPut(tile.key) {
            CallTileView(this).also { v -> v.setOnClickListener { toggleFocus(tile.key) } }
        }
        (view.parent as? ViewGroup)?.removeView(view)
        return view
    }

    private fun toggleFocus(key: String) {
        focusedKey = if (focusedKey == key) null else key
        renderTiles(lastParticipants)
    }

    private fun weightParams(weight: Float) =
        LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, weight)

    private fun rowItemParams() =
        LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f).apply {
            setMargins(dp(4), dp(4), dp(4), dp(4))
        }

    /** Резолвит имя/аватар участника по userId (livekit identity). Кеширует; при промахе — async-догрузка. */
    private fun infoFor(participant: CallParticipant): TileInfo {
        infoCache[participant.identity]?.let { return it }

        val uid = participant.identity.toLongOrNull() ?: 0L
        if (participant.isLocal) {
            val info = TileInfo(getString(R.string.current_user), null, uid, AvatarLoader.colorForUser(uid))
            infoCache[participant.identity] = info
            return info
        }

        val livekitName = participant.name.takeIf { it.isNotBlank() && it != participant.identity }
        val placeholder = TileInfo(
            livekitName ?: getString(R.string.call_participant_default),
            null,
            uid,
            AvatarLoader.colorForUser(uid)
        )

        if (uid > 0L && resolving.add(participant.identity)) {
            lifecycleScope.launch {
                val user = (application as BarkFluffApplication).grpcManager.getUserData(uid).getOrNull()
                if (user != null) {
                    val name = "${user.firstName} ${user.lastName}".trim()
                        .ifBlank { user.username }.ifBlank { getString(R.string.call_participant_default) }
                    val url = user.profilePicturePreviewUrl.ifBlank { user.profilePictureUrl }.ifBlank { null }
                    infoCache[participant.identity] = TileInfo(name, url, uid, AvatarLoader.colorForUser(uid))
                    renderTiles(lastParticipants)
                } else {
                    resolving.remove(participant.identity)
                }
            }
        }
        return placeholder
    }

    private fun updateTopBar(participants: List<CallParticipant>) {
        val isGroup = participants.size > 2
        participantBadge.visibility = if (isGroup) View.VISIBLE else View.GONE
        participantCount.text = participants.size.toString()
    }

    private fun updateControlStates(participants: List<CallParticipant>) {
        val local = participants.firstOrNull { it.isLocal }
        val micOn = local?.micEnabled ?: true
        val cameraOn = local?.cameraEnabled ?: false
        val screenOn = local?.screenTrack != null

        micButton.setImageResource(if (micOn) R.drawable.ic_mic else R.drawable.ic_mic_off)
        applyButtonState(micButton, active = !micOn)
        micButton.contentDescription = getString(if (micOn) R.string.call_mic_disable else R.string.call_mic_enable)

        applyButtonState(cameraButton, active = cameraOn)
        cameraButton.contentDescription = getString(if (cameraOn) R.string.call_camera_disable else R.string.call_camera_enable)

        applyButtonState(screenButton, active = screenOn)
        screenButton.contentDescription = getString(
            if (screenOn) R.string.call_screen_share_disable else R.string.call_screen_share_enable
        )

        if (cameraOn != lastForegroundCamera || screenOn != lastForegroundScreen) {
            lastForegroundCamera = cameraOn
            lastForegroundScreen = screenOn
            updateCallForegroundService(cameraOn, screenOn)
        }
    }

    /**
     * active=true → кнопка подсвечена системным dynamic-цветом (colorPrimary) с контрастной иконкой;
     * иначе полупрозрачная с белой иконкой. Так панель управления адаптируется под системную тему MD3.
     */
    private fun applyButtonState(button: ImageView, active: Boolean) {
        button.isSelected = active
        if (active) {
            button.backgroundTintList = android.content.res.ColorStateList.valueOf(
                resolveColor(androidx.appcompat.R.attr.colorPrimary))
            button.setColorFilter(resolveColor(com.google.android.material.R.attr.colorOnPrimary), PorterDuff.Mode.SRC_IN)
        } else {
            button.backgroundTintList = null
            button.setColorFilter(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant), PorterDuff.Mode.SRC_IN)
        }
    }

    // endregion

    private fun toggleMicrophone() {
        lifecycleScope.launch {
            val enabled = !(lastParticipants.firstOrNull { it.isLocal }?.micEnabled ?: true)
            desiredMicEnabled = enabled
            callEngine.setMicrophoneEnabled(enabled)
        }
    }

    private fun toggleCamera() {
        if (!hasPermission(Manifest.permission.CAMERA)) {
            pendingCameraToggleAfterPermission = true
            permissionLauncher.launch(arrayOf(Manifest.permission.CAMERA))
            return
        }

        lifecycleScope.launch {
            val enabled = !(lastParticipants.firstOrNull { it.isLocal }?.cameraEnabled ?: false)
            desiredCameraEnabled = enabled
            callEngine.setCameraEnabled(enabled)
                .onFailure {
                    Toast.makeText(this@CallActivity, R.string.call_camera_toggle_failed, Toast.LENGTH_SHORT).show()
                }
        }
    }

    private fun flipCamera() {
        callEngine.flipCamera()
            .onFailure { Toast.makeText(this, R.string.call_camera_flip_failed, Toast.LENGTH_SHORT).show() }
    }

    private fun startScreenShare() {
        if (callEngine.isLocalScreenShareEnabled()) {
            lifecycleScope.launch {
                callEngine.setScreenShareEnabled(false)
                    .onSuccess {
                        desiredScreenShareEnabled = false
                        updateCallForegroundService(
                            cameraEnabled = lastParticipants.firstOrNull { it.isLocal }?.cameraEnabled ?: desiredCameraEnabled,
                            screenShareEnabled = false
                        )
                    }
                    .onFailure {
                        Toast.makeText(this@CallActivity, R.string.call_screen_share_disable_failed, Toast.LENGTH_SHORT).show()
                    }
            }
            return
        }
        val manager = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        screenShareLauncher.launch(manager.createScreenCaptureIntent())
    }

    private fun showMoreSheet() {
        val dialog = BottomSheetDialog(this)
        val content = sheetContainer(getString(R.string.call_more_title)).apply {
            if (lastParticipants.firstOrNull { it.isLocal }?.cameraEnabled == true) {
                addView(sheetButton(getString(R.string.call_flip_camera), R.drawable.ic_camera) {
                    dialog.dismiss(); flipCamera()
                })
            }
            addView(sheetButton(getString(R.string.call_audio_route), R.drawable.ic_tune) {
                dialog.dismiss(); showAudioRouteSheet()
            })
            addView(sheetButton(getString(R.string.call_voice_quality), R.drawable.ic_tune) {
                dialog.dismiss(); showAudioQualitySheet()
            })
            addView(sheetButton(getString(R.string.call_remote_video_quality), R.drawable.ic_tune) {
                dialog.dismiss(); showVideoQualitySheet()
            })
        }
        dialog.setContentView(content)
        dialog.show()
    }

    private fun showAudioRouteSheet() {
        val devices = callEngine.availableAudioDevices()
        if (devices.isEmpty()) {
            Toast.makeText(this, R.string.call_no_audio_devices, Toast.LENGTH_SHORT).show()
            return
        }
        val dialog = BottomSheetDialog(this)
        val content = sheetContainer(getString(R.string.call_audio_route)).apply {
            devices.forEach { device ->
                addView(sheetButton(audioDeviceLabel(device), R.drawable.ic_tune) {
                    dialog.dismiss()
                    callEngine.selectAudioDevice(device)
                })
            }
        }
        dialog.setContentView(content)
        dialog.show()
    }

    private fun showAudioQualitySheet() {
        val dialog = BottomSheetDialog(this)
        val content = sheetContainer(getString(R.string.call_voice_quality)).apply {
            audioQualityOptions().forEach { (title, quality) ->
                addView(sheetButton(title, R.drawable.ic_tune) {
                    dialog.dismiss()
                    setAudioQuality(quality)
                })
            }
        }
        dialog.setContentView(content)
        dialog.show()
    }

    private fun showVideoQualitySheet() {
        val dialog = BottomSheetDialog(this)
        val content = sheetContainer(getString(R.string.call_remote_video_quality)).apply {
            videoQualityOptions().forEach { (title, quality) ->
                addView(sheetButton(title, R.drawable.ic_tune) {
                    dialog.dismiss()
                    setRemoteVideoQuality(quality)
                })
            }
        }
        dialog.setContentView(content)
        dialog.show()
    }

    private fun setAudioQuality(quality: CallsApiOuterClass.CallAudioQuality) {
        lifecycleScope.launch {
            if (!ensureCallsClient()) return@launch
            (application as BarkFluffApplication).callRepository.setAudioQuality(callId, quality)
                .onSuccess { Toast.makeText(this@CallActivity, R.string.call_quality_updated, Toast.LENGTH_SHORT).show() }
                .onFailure { Toast.makeText(this@CallActivity, R.string.call_quality_change_failed, Toast.LENGTH_SHORT).show() }
        }
    }

    private fun setRemoteVideoQuality(quality: VideoQuality) {
        val remotes = lastParticipants.filterNot { it.isLocal }
        if (remotes.isEmpty()) {
            Toast.makeText(this, R.string.call_no_remote_participants, Toast.LENGTH_SHORT).show()
            return
        }
        remotes.forEach { callEngine.setRemoteVideoQuality(it.identity, quality) }
        Toast.makeText(this, R.string.call_video_quality_updated, Toast.LENGTH_SHORT).show()
    }

    private fun endCallAndClose() {
        if (callEnded) return
        callEnded = true
        reconnectJob?.cancel()
        lifecycleScope.launch {
            callEngine.disconnect()
            CallForegroundService.stop(this@CallActivity)
            CallTelecomRegistry.disconnect(callId, DisconnectCause.LOCAL)
            if (ensureCallsClient()) {
                (application as BarkFluffApplication).callRepository.end(callId)
            }
            NotificationHelper.dismissCall(this@CallActivity, callId)
            finish()
        }
    }

    override fun onDestroy() {
        reconnectJob?.cancel()
        stopCallTimer()
        callEngine.disconnect()
        CallForegroundService.stop(this)
        CallTelecomRegistry.disconnect(callId, DisconnectCause.LOCAL)
        tileViews.values.forEach { it.release() }
        tileViews.clear()
        super.onDestroy()
    }

    override fun onConnecting() {
        statusText.text = getString(R.string.connecting)
    }

    override fun onConnected(cameraEnabled: Boolean) {
        reconnectAttempt = 0
        CallTelecomRegistry.markActive(callId)
        statusDot.background.mutate().setTint(speakingColor)
        updateCallForegroundService(cameraEnabled, desiredScreenShareEnabled)
        if (!batteryOptimizationPromptedForSession) {
            batteryOptimizationPromptedForSession = true
            CallBatteryOptimizationHelper.requestIgnoreBatteryOptimizationsIfNeeded(this)
        }
        startCallTimer()
    }

    override fun onReconnecting() {
        stopCallTimer()
        statusText.text = getString(R.string.call_status_reconnecting)
    }

    override fun onDisconnected() {
        if (!callEnded && !isFinishing) {
            stopCallTimer()
            statusDot.background.mutate().setTint(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
            scheduleLiveKitReconnect(getString(R.string.call_status_connection_lost))
            return
        }
        stopCallTimer()
        statusDot.background.mutate().setTint(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
        statusText.text = getString(R.string.call_ended)
    }

    override fun onError() {
        val message = getString(R.string.call_connection_failed)
        if (!callEnded && !isFinishing) {
            stopCallTimer()
            statusText.text = message
            scheduleLiveKitReconnect(message)
            return
        }
        stopCallTimer()
        statusText.text = message
    }

    private fun startCallTimer() {
        if (callStartedAtMs == 0L) {
            callStartedAtMs = System.currentTimeMillis()
        }
        if (callTimerJob?.isActive == true) return

        callTimerJob = lifecycleScope.launch {
            while (true) {
                renderCallDuration()
                delay(1_000L)
            }
        }
    }

    private fun stopCallTimer() {
        callTimerJob?.cancel()
        callTimerJob = null
    }

    private fun renderCallDuration() {
        if (callStartedAtMs == 0L) {
            statusText.text = getString(R.string.call_status_in_call)
            return
        }
        val elapsedSeconds = ((System.currentTimeMillis() - callStartedAtMs) / 1_000L).coerceAtLeast(0L)
        statusText.text = getString(
            R.string.call_status_in_call_duration,
            getString(R.string.call_status_in_call),
            formatDuration(elapsedSeconds)
        )
    }

    private fun formatDuration(totalSeconds: Long): String {
        val minutes = totalSeconds / 60L
        val seconds = (totalSeconds % 60L).toString().padStart(2, '0')
        return "$minutes:$seconds"
    }

    private fun updateCallForegroundService(cameraEnabled: Boolean, screenShareEnabled: Boolean) {
        CallForegroundService.start(
            context = this,
            callId = callId,
            title = callTitle,
            mediaType = mediaType,
            livekitUrl = livekitUrl,
            accessToken = accessToken,
            cameraEnabled = cameraEnabled,
            screenShareEnabled = screenShareEnabled
        )
    }

    private fun requestInitialPermissionsOrConnect() {
        val missing = requiredInitialPermissions().filterNot(::hasPermission)
        if (missing.isEmpty()) {
            connectToLiveKit()
        } else {
            permissionLauncher.launch(missing.toTypedArray())
        }
    }

    private fun requiredInitialPermissions(): List<String> = buildList {
        add(Manifest.permission.RECORD_AUDIO)
        if (isVideoCall()) add(Manifest.permission.CAMERA)
    }

    private fun hasPermission(permission: String): Boolean =
        ContextCompat.checkSelfPermission(this, permission) == PackageManager.PERMISSION_GRANTED

    private fun isVideoCall(): Boolean = mediaType.equals("video", ignoreCase = true)

    private fun ensureCallsClient(): Boolean {
        val app = application as BarkFluffApplication
        if (app.grpcManager.callsClient != null) return true

        val callsAddress = GlobalParam(this).socketCalls
        if (callsAddress.isBlank()) return false

        return app.grpcManager.createCallsClient(callsAddress, this, includeDeviceInfo = true).isSuccess
    }

    private fun sheetContainer(title: String): LinearLayout =
        LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(8), dp(20), dp(20))
            addView(TextView(this@CallActivity).apply {
                text = title
                setTextAppearance(android.R.style.TextAppearance_Material_Large)
                setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurface))
                setPadding(0, dp(8), 0, dp(12))
            })
        }

    private fun sheetButton(text: String, icon: Int, onClick: () -> Unit): MaterialButton =
        MaterialButton(this, null, com.google.android.material.R.attr.materialButtonOutlinedStyle).apply {
            this.text = text
            setIconResource(icon)
            iconGravity = MaterialButton.ICON_GRAVITY_TEXT_START
            gravity = Gravity.CENTER_VERTICAL
            minHeight = dp(48)
            setOnClickListener { onClick() }
        }

    private fun audioDeviceLabel(device: AudioDevice): String = when (device) {
        is AudioDevice.Speakerphone -> getString(R.string.call_audio_device_speaker)
        is AudioDevice.Earpiece -> getString(R.string.call_audio_device_phone)
        is AudioDevice.WiredHeadset -> getString(R.string.call_audio_device_wired)
        is AudioDevice.BluetoothHeadset -> device.name
            .takeIf { it.isNotBlank() }
            ?.let { getString(R.string.call_audio_device_bluetooth, it) }
            ?: getString(R.string.call_audio_device_bluetooth_default)
    }

    private fun audioQualityOptions(): List<Pair<String, CallsApiOuterClass.CallAudioQuality>> = listOf(
        getString(R.string.call_quality_auto) to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_AUTO,
        getString(R.string.call_quality_low) to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_LOW,
        getString(R.string.call_quality_medium) to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_MEDIUM,
        getString(R.string.call_quality_high) to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_HIGH
    )

    private fun videoQualityOptions(): List<Pair<String, VideoQuality>> = listOf(
        getString(R.string.call_quality_low) to VideoQuality.LOW,
        getString(R.string.call_quality_medium) to VideoQuality.MEDIUM,
        getString(R.string.call_quality_high) to VideoQuality.HIGH
    )

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun resolveColor(attr: Int): Int {
        val out = android.util.TypedValue()
        theme.resolveAttribute(attr, out, true)
        return out.data
    }

    private companion object {
        const val TAG = "CallActivity"
        const val MAX_RECONNECT_ATTEMPTS = 8
    }
}
