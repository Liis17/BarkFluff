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
import android.widget.FrameLayout
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
import io.livekit.android.renderer.SurfaceViewRenderer
import kotlinx.coroutines.launch

class CallActivity : AppCompatActivity(), LiveKitCallEngine.Listener {

    private lateinit var callId: String
    private lateinit var livekitUrl: String
    private lateinit var accessToken: String
    private lateinit var mediaType: String
    private lateinit var callTitle: String
    private lateinit var callEngine: LiveKitCallEngine

    private lateinit var statusText: TextView
    private lateinit var waitingText: TextView
    private lateinit var remoteRenderer: SurfaceViewRenderer
    private lateinit var localRenderer: SurfaceViewRenderer
    private lateinit var micButton: MaterialButton
    private lateinit var cameraButton: MaterialButton
    private lateinit var screenButton: MaterialButton

    private var micEnabled = true
    private var cameraEnabled = false
    private var screenShareEnabled = false
    private var pendingCameraToggleAfterPermission = false

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

        root.addView(FrameLayout(this).apply {
            setBackgroundColor(resolveColor(com.google.android.material.R.attr.colorSurfaceContainerLowest))
            layoutParams = LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                0,
                1f
            )

            remoteRenderer = SurfaceViewRenderer(this@CallActivity).apply {
                contentDescription = "Видео собеседника"
                visibility = View.INVISIBLE
            }
            addView(remoteRenderer, FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT
            ))

            waitingText = TextView(this@CallActivity).apply {
                text = "Ожидаем видео собеседника"
                gravity = Gravity.CENTER
                setTextAppearance(android.R.style.TextAppearance_Material_Body1)
                setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurfaceVariant))
            }
            addView(waitingText, FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                Gravity.CENTER
            ))

            localRenderer = SurfaceViewRenderer(this@CallActivity).apply {
                contentDescription = "Ваше видео"
                visibility = View.GONE
                setBackgroundColor(resolveColor(com.google.android.material.R.attr.colorSurfaceContainerHigh))
            }
            addView(localRenderer, FrameLayout.LayoutParams(dp(112), dp(156), Gravity.BOTTOM or Gravity.END).apply {
                marginEnd = dp(16)
                bottomMargin = dp(16)
            })
        })

        root.addView(LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER
            setPadding(dp(12), dp(12), dp(12), dp(20))

            micButton = controlButton(R.drawable.ic_mic, "Выключить микрофон") { toggleMicrophone() }
            cameraButton = controlButton(R.drawable.ic_video, "Включить камеру") { toggleCamera() }
            screenButton = controlButton(R.drawable.ic_screen_share, "Демонстрация экрана") { showMediaPicker() }
            val qualityButton = controlButton(R.drawable.ic_tune, "Качество звонка") { showQualitySheet() }
            val hangupButton = controlButton(R.drawable.ic_close, "Завершить звонок") { endCallAndClose() }.apply {
                setBackgroundColor(resolveColor(android.R.attr.colorError))
                iconTint = ColorStateList.valueOf(resolveColor(com.google.android.material.R.attr.colorOnError))
            }

            listOf(micButton, cameraButton, screenButton, qualityButton, hangupButton).forEach { button ->
                addView(button, LinearLayout.LayoutParams(dp(56), dp(56)).apply {
                    marginStart = dp(6)
                    marginEnd = dp(6)
                })
            }
        })

        return root
    }

    private fun connectToLiveKit() {
        lifecycleScope.launch {
            callEngine.connect(
                livekitUrl = livekitUrl,
                accessToken = accessToken,
                remoteRenderer = remoteRenderer,
                localRenderer = localRenderer,
                cameraOnStart = isVideoCall()
            ).onFailure {
                statusText.text = "Не удалось подключиться к звонку"
                Toast.makeText(this@CallActivity, "Ошибка подключения к LiveKit", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun toggleMicrophone() {
        lifecycleScope.launch {
            val enabled = !micEnabled
            callEngine.setMicrophoneEnabled(enabled)
                .onSuccess {
                    micEnabled = enabled
                    micButton.setIconResource(if (enabled) R.drawable.ic_mic else R.drawable.ic_mic_off)
                    micButton.contentDescription = if (enabled) "Выключить микрофон" else "Включить микрофон"
                }
        }
    }

    private fun toggleCamera() {
        if (!hasPermission(Manifest.permission.CAMERA)) {
            pendingCameraToggleAfterPermission = true
            permissionLauncher.launch(arrayOf(Manifest.permission.CAMERA))
            return
        }

        lifecycleScope.launch {
            val enabled = !cameraEnabled
            callEngine.setCameraEnabled(enabled)
                .onSuccess {
                    cameraEnabled = enabled
                    cameraButton.isSelected = enabled
                    cameraButton.contentDescription = if (enabled) "Выключить камеру" else "Включить камеру"
                    updateCallForegroundService()
                }.onFailure {
                    Toast.makeText(this@CallActivity, "Не удалось переключить камеру", Toast.LENGTH_SHORT).show()
                }
        }
    }

    private fun showMediaPicker() {
        val dialog = BottomSheetDialog(this)
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(8), dp(20), dp(20))
            addView(TextView(this@CallActivity).apply {
                text = "Что показать в звонке"
                setTextAppearance(android.R.style.TextAppearance_Material_Large)
                setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurface))
                setPadding(0, dp(8), 0, dp(12))
            })
            addView(sheetButton("Камера", R.drawable.ic_video) {
                dialog.dismiss()
                toggleCamera()
            })
            addView(sheetButton("Экран", R.drawable.ic_screen_share) {
                dialog.dismiss()
                startScreenShare()
            })
        }
        dialog.setContentView(content)
        dialog.show()
    }

    private fun showQualitySheet() {
        val dialog = BottomSheetDialog(this)
        val content = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(8), dp(20), dp(20))
            addView(TextView(this@CallActivity).apply {
                text = "Качество голоса"
                setTextAppearance(android.R.style.TextAppearance_Material_Large)
                setTextColor(resolveColor(com.google.android.material.R.attr.colorOnSurface))
                setPadding(0, dp(8), 0, dp(12))
            })
            qualityOptions().forEach { (title, quality) ->
                addView(sheetButton(title, R.drawable.ic_tune) {
                    dialog.dismiss()
                    setAudioQuality(quality)
                })
            }
        }
        dialog.setContentView(content)
        dialog.show()
    }

    private fun startScreenShare() {
        if (screenShareEnabled) {
            lifecycleScope.launch {
                callEngine.setScreenShareEnabled(false)
                    .onFailure { Toast.makeText(this@CallActivity, "Не удалось выключить демонстрацию", Toast.LENGTH_SHORT).show() }
            }
            return
        }
        val manager = getSystemService(MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
        screenShareLauncher.launch(manager.createScreenCaptureIntent())
    }

    private fun setAudioQuality(quality: CallsApiOuterClass.CallAudioQuality) {
        lifecycleScope.launch {
            if (!ensureCallsClient()) return@launch
            (application as BarkFluffApplication).callRepository.setAudioQuality(callId, quality)
                .onSuccess { Toast.makeText(this@CallActivity, "Качество звонка обновлено", Toast.LENGTH_SHORT).show() }
                .onFailure { Toast.makeText(this@CallActivity, "Не удалось изменить качество", Toast.LENGTH_SHORT).show() }
        }
    }

    private fun endCallAndClose() {
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
        callEngine.disconnect()
        CallForegroundService.stop(this)
        runCatching { remoteRenderer.release() }
        runCatching { localRenderer.release() }
        super.onDestroy()
    }

    override fun onConnecting() {
        statusText.text = "Соединение..."
    }

    override fun onConnected(cameraEnabled: Boolean) {
        statusText.text = "В разговоре"
        this.cameraEnabled = cameraEnabled
        cameraButton.isSelected = cameraEnabled
        cameraButton.contentDescription = if (cameraEnabled) "Выключить камеру" else "Включить камеру"
        updateCallForegroundService()
    }

    override fun onRemoteVideoAttached() {
        remoteRenderer.visibility = View.VISIBLE
        waitingText.visibility = View.GONE
        statusText.text = "В разговоре"
    }

    override fun onRemoteVideoDetached() {
        remoteRenderer.visibility = View.INVISIBLE
        waitingText.visibility = View.VISIBLE
    }

    override fun onLocalPreviewChanged(visible: Boolean) {
        localRenderer.visibility = if (visible) View.VISIBLE else View.GONE
    }

    override fun onReconnecting() {
        statusText.text = "Восстанавливаем соединение..."
    }

    override fun onDisconnected() {
        statusText.text = "Звонок завершён"
    }

    override fun onScreenShareChanged(enabled: Boolean) {
        screenShareEnabled = enabled
        screenButton.isSelected = enabled
        screenButton.contentDescription = if (enabled) "Выключить демонстрацию экрана" else "Демонстрация экрана"
        statusText.text = if (enabled) "Демонстрация экрана включена" else "В разговоре"
        updateCallForegroundService()
    }

    override fun onError(message: String) {
        statusText.text = message
    }

    private fun updateCallForegroundService() {
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

    private fun sheetButton(text: String, icon: Int, onClick: () -> Unit): MaterialButton =
        MaterialButton(this, null, com.google.android.material.R.attr.materialButtonOutlinedStyle).apply {
            this.text = text
            setIconResource(icon)
            iconGravity = MaterialButton.ICON_GRAVITY_TEXT_START
            gravity = Gravity.CENTER_VERTICAL
            minHeight = dp(48)
            setOnClickListener { onClick() }
        }

    private fun qualityOptions(): List<Pair<String, CallsApiOuterClass.CallAudioQuality>> = listOf(
        "Авто" to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_AUTO,
        "Низкое" to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_LOW,
        "Среднее" to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_MEDIUM,
        "Высокое" to CallsApiOuterClass.CallAudioQuality.CALL_AUDIO_QUALITY_HIGH
    )

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun resolveColor(attr: Int): Int {
        val out = android.util.TypedValue()
        theme.resolveAttribute(attr, out, true)
        return out.data
    }
}
