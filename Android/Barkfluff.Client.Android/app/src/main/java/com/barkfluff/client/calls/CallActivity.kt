package com.barkfluff.client.calls

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.ColorStateList
import android.media.projection.MediaProjectionManager
import android.os.Bundle
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import barkfluff.calls.CallsApiOuterClass
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
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

    private lateinit var statusText: TextView
    private lateinit var videoArea: LinearLayout
    private lateinit var micButton: MaterialButton
    private lateinit var cameraButton: MaterialButton
    private lateinit var flipButton: MaterialButton
    private lateinit var screenButton: MaterialButton

    private val tileViews = HashMap<String, CallTileView>()
    private var lastLayoutSignature: String? = null
    private var lastParticipants: List<CallParticipant> = emptyList()
    private var focusedKey: String? = null

    private var pendingCameraToggleAfterPermission = false
    private var callEnded = false
    private var callStartedAtMs = 0L
    private var callTimerJob: Job? = null
    private var lastForegroundCamera = false
    private var lastForegroundScreen = false

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
            statusText.text = "Разрешите микрофон и камеру, чтобы начать звонок"
        }
    }

    private val screenShareLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        val data = result.data
        if (result.resultCode != Activity.RESULT_OK || data == null) return@registerForActivityResult

        lifecycleScope.launch {
            callEngine.setScreenShareEnabled(true, data)
                .onFailure { Toast.makeText(this@CallActivity, "Не удалось включить демонстрацию", Toast.LENGTH_SHORT).show() }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        livekitUrl = intent.getStringExtra(CallExtras.EXTRA_LIVEKIT_URL).orEmpty().ifBlank { GlobalParam(this).livekitUrl }
        accessToken = intent.getStringExtra(CallExtras.EXTRA_ACCESS_TOKEN).orEmpty()
        mediaType = intent.getStringExtra(CallExtras.EXTRA_MEDIA_TYPE).orEmpty()
        callTitle = intent.getStringExtra(CallExtras.EXTRA_CALLER_NAME).orEmpty().ifBlank { "Звонок" }

        if (callId.isBlank()) {
            finish()
            return
        }

        callEngine = LiveKitCallEngine(applicationContext, lifecycleScope, this)
        setContentView(buildContent())
        observeParticipants()
        observeCallEvents()

        if (livekitUrl.isBlank() || accessToken.isBlank()) {
            statusText.text = "Нет данных для подключения к LiveKit"
            return
        }

        requestInitialPermissionsOrConnect()
    }

    private fun buildContent(): View {
        val root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(resolveColor(com.google.android.material.R.attr.colorSurface))
            layoutParams = ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            )
        }

        root.addView(LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(24), dp(20), dp(12))

            addView(TextView(this@CallActivity).apply {
                text = callTitle
                maxLines = 1
                ellipsize = android.text.TextUtils.TruncateAt.END
                setTextAppearance(android.R.style.TextAppearance_Material_Large)
                setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurface))
            })

            statusText = TextView(this@CallActivity).apply {
                text = if (isVideoCall()) "Подключение видеозвонка..." else "Подключение аудиозвонка..."
                setPadding(0, dp(4), 0, 0)
                setTextAppearance(android.R.style.TextAppearance_Material_Body1)
                setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
            }
            addView(statusText)
        })

        videoArea = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setBackgroundColor(resolveColor(com.google.android.material.R.attr.colorSurfaceContainerLowest))
            layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f)
        }
        root.addView(videoArea)

        root.addView(LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            setPadding(dp(12), dp(12), dp(12), dp(20))

            micButton = controlButton(R.drawable.ic_mic, "Выключить микрофон") { toggleMicrophone() }
            cameraButton = controlButton(R.drawable.ic_video, "Включить камеру") { toggleCamera() }
            flipButton = controlButton(R.drawable.ic_video, "Перевернуть камеру") { flipCamera() }.apply {
                visibility = View.GONE
            }
            screenButton = controlButton(R.drawable.ic_screen_share, "Демонстрация экрана") { startScreenShare() }
            val moreButton = controlButton(R.drawable.ic_tune, "Дополнительно") { showMoreSheet() }
            val hangupButton = controlButton(R.drawable.ic_close, "Завершить звонок") { endCallAndClose() }.apply {
                setBackgroundColor(resolveColor(android.R.attr.colorError))
                iconTint = ColorStateList.valueOf(resolveColor(com.google.android.material.R.attr.colorOnError))
            }

            listOf(micButton, cameraButton, flipButton, screenButton, moreButton, hangupButton).forEach { button ->
                addView(button, LinearLayout.LayoutParams(dp(56), dp(56)).apply {
                    marginStart = dp(6)
                    marginEnd = dp(6)
                })
            }
        })

        return root
    }

    private fun observeParticipants() {
        lifecycleScope.launch {
            callEngine.participants.collect { participants ->
                lastParticipants = participants
                renderTiles(participants)
            }
        }
    }

    /**
     * Реагирует на сырой стрим [CallEventsService.events] по своему [callId]: завершение/отклонение
     * (в т.ч. инициированное собеседником или со второго устройства) закрывает экран. Работает и для
     * звонящего, у которого [CallEventsService.currentCall] не инициализируется.
     */
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
        stopCallTimer()
        callEngine.disconnect()
        CallForegroundService.stop(this)
        NotificationHelper.dismissCall(this, callId)
        statusText.text = "Звонок завершён"
        finish()
    }

    private fun connectToLiveKit() {
        lifecycleScope.launch {
            callEngine.connect(
                livekitUrl = livekitUrl,
                accessToken = accessToken,
                cameraOnStart = isVideoCall()
            ).onFailure {
                statusText.text = "Не удалось подключиться к звонку"
                Toast.makeText(this@CallActivity, "Ошибка подключения к LiveKit", Toast.LENGTH_SHORT).show()
            }
        }
    }

    // region Раскладка плиток

    private fun renderTiles(participants: List<CallParticipant>) {
        val screenTile = participants.firstOrNull { it.screenTrack != null }?.let {
            CallTile("scr:${it.identity}", it, it.screenTrack, isScreen = true)
        }
        val cameraTiles = participants.map { CallTile("cam:${it.identity}", it, it.cameraTrack, isScreen = false) }

        val focused = focusedKey
        val mode: String
        val bigTiles: List<CallTile>
        val stripTiles: List<CallTile>
        when {
            focused != null && screenTile?.key == focused -> {
                mode = "focus"; bigTiles = listOf(screenTile); stripTiles = emptyList()
            }
            focused != null && cameraTiles.any { it.key == focused } -> {
                mode = "focus"; bigTiles = listOf(cameraTiles.first { it.key == focused }); stripTiles = emptyList()
            }
            screenTile != null -> {
                mode = "screen"; bigTiles = listOf(screenTile); stripTiles = cameraTiles
            }
            else -> {
                mode = "grid"; bigTiles = cameraTiles; stripTiles = emptyList()
            }
        }

        val visible = bigTiles + stripTiles
        val signature = "$mode|" + visible.joinToString(",") { it.key }
        if (signature != lastLayoutSignature) {
            rebuildLayout(mode, bigTiles, stripTiles)
            lastLayoutSignature = signature
        }
        visible.forEach { tileViews[it.key]?.bind(it, callEngine) }

        val visibleKeys = visible.map { it.key }.toSet()
        (tileViews.keys - visibleKeys).forEach { tileViews.remove(it)?.release() }

        updateControlStates(participants)
        renderCallDuration()
    }

    private fun rebuildLayout(mode: String, bigTiles: List<CallTile>, stripTiles: List<CallTile>) {
        videoArea.removeAllViews()
        when (mode) {
            "focus" -> videoArea.addView(tileFor(bigTiles.first()), weightParams(1f))
            "screen" -> {
                videoArea.addView(tileFor(bigTiles.first()), weightParams(3f))
                if (stripTiles.isNotEmpty()) {
                    val strip = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
                    stripTiles.forEach { strip.addView(tileFor(it), rowItemParams()) }
                    videoArea.addView(strip, weightParams(1f))
                }
            }
            else -> addGrid(bigTiles)
        }
    }

    private fun addGrid(tiles: List<CallTile>) {
        if (tiles.isEmpty()) return
        val cols = if (tiles.size <= 1) 1 else 2
        var i = 0
        while (i < tiles.size) {
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            var c = 0
            while (c < cols && i < tiles.size) {
                row.addView(tileFor(tiles[i]), rowItemParams())
                i++; c++
            }
            videoArea.addView(row, weightParams(1f))
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
            setMargins(dp(2), dp(2), dp(2), dp(2))
        }

    private fun updateControlStates(participants: List<CallParticipant>) {
        val local = participants.firstOrNull { it.isLocal }
        val micOn = local?.micEnabled ?: true
        val cameraOn = local?.cameraEnabled ?: false
        val screenOn = local?.screenTrack != null

        micButton.setIconResource(if (micOn) R.drawable.ic_mic else R.drawable.ic_mic_off)
        micButton.contentDescription = if (micOn) "Выключить микрофон" else "Включить микрофон"

        cameraButton.isSelected = cameraOn
        cameraButton.contentDescription = if (cameraOn) "Выключить камеру" else "Включить камеру"

        flipButton.visibility = if (cameraOn) View.VISIBLE else View.GONE

        screenButton.isSelected = screenOn
        screenButton.contentDescription = if (screenOn) "Выключить демонстрацию экрана" else "Демонстрация экрана"

        if (cameraOn != lastForegroundCamera || screenOn != lastForegroundScreen) {
            lastForegroundCamera = cameraOn
            lastForegroundScreen = screenOn
            updateCallForegroundService(cameraOn, screenOn)
        }
    }

    // endregion

    private fun toggleMicrophone() {
        lifecycleScope.launch {
            val enabled = !(lastParticipants.firstOrNull { it.isLocal }?.micEnabled ?: true)
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
            callEngine.setCameraEnabled(enabled)
                .onFailure { Toast.makeText(this@CallActivity, "Не удалось переключить камеру", Toast.LENGTH_SHORT).show() }
        }
    }

    private fun flipCamera() {
        callEngine.flipCamera()
            .onFailure { Toast.makeText(this, "Не удалось перевернуть камеру", Toast.LENGTH_SHORT).show() }
    }

    private fun startScreenShare() {
        if (callEngine.isLocalScreenShareEnabled()) {
            lifecycleScope.launch {
                callEngine.setScreenShareEnabled(false)
                    .onFailure { Toast.makeText(this@CallActivity, "Не удалось выключить демонстрацию", Toast.LENGTH_SHORT).show() }
            }
            return
        }
        val manager = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        screenShareLauncher.launch(manager.createScreenCaptureIntent())
    }

    private fun showMoreSheet() {
        val dialog = BottomSheetDialog(this)
        val content = sheetContainer("Дополнительно").apply {
            addView(sheetButton("Маршрут звука", R.drawable.ic_tune) {
                dialog.dismiss(); showAudioRouteSheet()
            })
            addView(sheetButton("Качество голоса", R.drawable.ic_tune) {
                dialog.dismiss(); showAudioQualitySheet()
            })
            addView(sheetButton("Качество видео собеседника", R.drawable.ic_tune) {
                dialog.dismiss(); showVideoQualitySheet()
            })
        }
        dialog.setContentView(content)
        dialog.show()
    }

    private fun showAudioRouteSheet() {
        val devices = callEngine.availableAudioDevices()
        if (devices.isEmpty()) {
            Toast.makeText(this, "Нет доступных аудиоустройств", Toast.LENGTH_SHORT).show()
            return
        }
        val dialog = BottomSheetDialog(this)
        val content = sheetContainer("Маршрут звука").apply {
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
        val content = sheetContainer("Качество голоса").apply {
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
        val content = sheetContainer("Качество видео собеседника").apply {
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
                .onSuccess { Toast.makeText(this@CallActivity, "Качество звонка обновлено", Toast.LENGTH_SHORT).show() }
                .onFailure { Toast.makeText(this@CallActivity, "Не удалось изменить качество", Toast.LENGTH_SHORT).show() }
        }
    }

    private fun setRemoteVideoQuality(quality: VideoQuality) {
        val remotes = lastParticipants.filterNot { it.isLocal }
        if (remotes.isEmpty()) {
            Toast.makeText(this, "Нет удалённых участников", Toast.LENGTH_SHORT).show()
            return
        }
        remotes.forEach { callEngine.setRemoteVideoQuality(it.identity, quality) }
        Toast.makeText(this, "Качество видео обновлено", Toast.LENGTH_SHORT).show()
    }

    private fun endCallAndClose() {
        if (callEnded) return
        callEnded = true
        lifecycleScope.launch {
            callEngine.disconnect()
            CallForegroundService.stop(this@CallActivity)
            if (ensureCallsClient()) {
                (application as BarkFluffApplication).callRepository.end(callId)
            }
            NotificationHelper.dismissCall(this@CallActivity, callId)
            finish()
        }
    }

    override fun onDestroy() {
        stopCallTimer()
        callEngine.disconnect()
        CallForegroundService.stop(this)
        tileViews.values.forEach { it.release() }
        tileViews.clear()
        super.onDestroy()
    }

    override fun onConnecting() {
        statusText.text = "Соединение..."
    }

    override fun onConnected(cameraEnabled: Boolean) {
        startCallTimer()
    }

    override fun onReconnecting() {
        statusText.text = "Восстанавливаем соединение..."
    }

    override fun onDisconnected() {
        stopCallTimer()
        statusText.text = "Звонок завершён"
    }

    override fun onError(message: String) {
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
            statusText.text = "В разговоре"
            return
        }
        val elapsedSeconds = ((System.currentTimeMillis() - callStartedAtMs) / 1_000L).coerceAtLeast(0L)
        statusText.text = "В разговоре · ${formatDuration(elapsedSeconds)}"
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

    private fun controlButton(icon: Int, description: String, onClick: () -> Unit): MaterialButton =
        MaterialButton(this, null, com.google.android.material.R.attr.materialIconButtonStyle).apply {
            text = ""
            setIconResource(icon)
            contentDescription = description
            minWidth = dp(48)
            minHeight = dp(48)
            cornerRadius = dp(28)
            insetTop = 0
            insetBottom = 0
            setOnClickListener { onClick() }
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
        is AudioDevice.Speakerphone -> "Динамик"
        is AudioDevice.Earpiece -> "Телефон"
        is AudioDevice.WiredHeadset -> "Проводная гарнитура"
        is AudioDevice.BluetoothHeadset -> device.name.ifBlank { "Bluetooth-гарнитура" }
    }

    private fun audioQualityOptions(): List<Pair<String, CallsApiOuterClass.CallAudioQuality>> = listOf(
        "Авто" to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_AUTO,
        "Низкое" to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_LOW,
        "Среднее" to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_MEDIUM,
        "Высокое" to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_HIGH
    )

    private fun videoQualityOptions(): List<Pair<String, VideoQuality>> = listOf(
        "Низкое" to VideoQuality.LOW,
        "Среднее" to VideoQuality.MEDIUM,
        "Высокое" to VideoQuality.HIGH
    )

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun resolveColor(attr: Int): Int {
        val out = android.util.TypedValue()
        theme.resolveAttribute(attr, out, true)
        return out.data
    }
}
