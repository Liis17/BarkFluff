package com.barkfluff.client.calls

import android.animation.ObjectAnimator
import android.animation.PropertyValuesHolder
import android.animation.ValueAnimator
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.os.Build
import android.os.Bundle
import android.telecom.DisconnectCause
import android.view.View
import android.view.WindowManager
import android.view.animation.DecelerateInterpolator
import android.widget.ImageView
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import coil.request.ImageRequest
import coil.size.Size
import coil.transform.CircleCropTransformation
import com.barkfluff.client.BarkFluffApplication
import com.barkfluff.client.R
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.notifications.NotificationHelper
import com.barkfluff.client.repository.ChatRepository
import com.barkfluff.client.utils.AvatarLoader
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume

class IncomingCallActivity : AppCompatActivity() {

    private lateinit var callId: String
    private lateinit var callerName: String
    private lateinit var mediaType: String
    private var callerUserId: Long = 0L
    private var dismissReceiverRegistered = false
    private var actionTaken = false
    private val ringAnimators = mutableListOf<ValueAnimator>()

    private val dismissReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            if (intent.action != CallExtras.ACTION_DISMISS_INCOMING_CALL) return
            if (intent.getStringExtra(CallExtras.EXTRA_CALL_ID) == callId) {
                NotificationHelper.dismissCall(this@IncomingCallActivity, callId)
                finish()
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        configureLockScreenPresentation()

        callId = intent.getStringExtra(CallExtras.EXTRA_CALL_ID).orEmpty()
        callerName = intent.getStringExtra(CallExtras.EXTRA_CALLER_NAME).orEmpty().ifBlank { "BarkFluff" }
        mediaType = intent.getStringExtra(CallExtras.EXTRA_MEDIA_TYPE).orEmpty()
        callerUserId = intent.getLongExtra(CallExtras.EXTRA_CALLER_USER_ID, 0L)

        if (callId.isBlank()) {
            finish()
            return
        }

        (application as BarkFluffApplication).markCallPresented(callId)

        setContentView(R.layout.activity_incoming_call)
        bindViews()
        startAvatarRingAnimation()
        loadCallerInfo()
        registerDismissReceiver()

        if (intent.action == CallExtras.ACTION_ACCEPT_CALL) {
            acceptCall()
        }
    }

    private fun configureLockScreenPresentation() {
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) {
            setShowWhenLocked(true)
            setTurnScreenOn(true)
        } else {
            @Suppress("DEPRECATION")
            window.addFlags(
                WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED or
                    WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON
            )
        }
    }

    override fun onDestroy() {
        cancelAvatarRingAnimation()
        if (dismissReceiverRegistered) {
            unregisterReceiver(dismissReceiver)
            dismissReceiverRegistered = false
        }
        super.onDestroy()
    }

    private fun registerDismissReceiver() {
        val filter = IntentFilter(CallExtras.ACTION_DISMISS_INCOMING_CALL)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(dismissReceiver, filter, Context.RECEIVER_NOT_EXPORTED)
        } else {
            @Suppress("DEPRECATION")
            registerReceiver(dismissReceiver, filter)
        }
        dismissReceiverRegistered = true
    }

    private fun bindViews() {
        findViewById<TextView>(R.id.callerName).text = callerName
        findViewById<TextView>(R.id.avatarInitials).text = initialsOf(callerName)
        findViewById<TextView>(R.id.callType).text =
            if (mediaType.equals("video", ignoreCase = true)) "Видеозвонок" else "Аудиозвонок"

        findViewById<ImageView>(R.id.rejectButton).setOnClickListener { rejectCall() }
        findViewById<ImageView>(R.id.acceptButton).setOnClickListener { acceptCall() }
    }


    private fun startAvatarRingAnimation() {
        ringAnimators.clear()
        startRingPulse(findViewById(R.id.avatarRingOuter), startScale = 0.5f, maxAlpha = 0.24f, startDelay = 0L)
        startRingPulse(findViewById(R.id.avatarRingInner), startScale = 0.64f, maxAlpha = 0.36f, startDelay = RING_STAGGER_MS)
    }

    private fun startRingPulse(ring: View, startScale: Float, maxAlpha: Float, startDelay: Long) {
        ring.scaleX = startScale
        ring.scaleY = startScale
        ring.alpha = 0f

        val animator = ObjectAnimator.ofPropertyValuesHolder(
            ring,
            PropertyValuesHolder.ofFloat("scaleX", startScale, 1f),
            PropertyValuesHolder.ofFloat("scaleY", startScale, 1f),
            PropertyValuesHolder.ofFloat("alpha", maxAlpha, 0f)
        ).apply {
            duration = RING_DURATION_MS
            this.startDelay = startDelay
            repeatCount = ValueAnimator.INFINITE
            repeatMode = ValueAnimator.RESTART
            interpolator = DecelerateInterpolator()
            start()
        }
        ringAnimators += animator
    }

    private fun cancelAvatarRingAnimation() {
        ringAnimators.forEach { it.cancel() }
        ringAnimators.clear()
    }
    /**
     * Подтягивает реальное имя и аватар звонящего по userId через профиль.
     * При отсутствии userId/профиля остаются инициалы из bindViews().
     */
    private fun loadCallerInfo() {
        if (callerUserId <= 0L) return

        val app = application as BarkFluffApplication
        val repository = ChatRepository(this, app.grpcManager)
        val avatarImage = findViewById<ImageView>(R.id.avatarImage)
        val avatarInitials = findViewById<TextView>(R.id.avatarInitials)

        lifecycleScope.launch {
            val user = repository.getUserData(callerUserId).getOrNull() ?: return@launch

            val displayName = "${user.firstName} ${user.lastName}".trim().ifBlank { user.username }
            if (displayName.isNotBlank()) {
                callerName = displayName
                findViewById<TextView>(R.id.callerName).text = displayName
                avatarInitials.text = initialsOf(displayName)
            }

            val avatarFileId = user.profilePictureFileId
            if (avatarFileId.isNotBlank()) {
                loadCallerAvatarWithRetry(avatarImage, avatarInitials, avatarFileId, repository)
            }
        }
    }


    private suspend fun loadCallerAvatarWithRetry(
        avatarImage: ImageView,
        avatarInitials: TextView,
        avatarFileId: String,
        repository: ChatRepository
    ) {
        AvatarLoader.showPlaceholder(avatarInitials, callerName, callerUserId)
        avatarImage.visibility = View.GONE

        for (attempt in 1..AVATAR_LOAD_ATTEMPTS) {
            val avatarUrl = resolveAvatarUrl(avatarFileId, repository, forceRefresh = attempt > 1)
            if (!avatarUrl.isNullOrBlank() && loadAvatarUrl(avatarImage, avatarInitials, avatarFileId, avatarUrl)) {
                return
            }
            AvatarLoader.urlCache.remove(avatarFileId)
            if (attempt < AVATAR_LOAD_ATTEMPTS) {
                delay(AVATAR_LOAD_RETRY_DELAY_MS)
            }
        }
    }

    private suspend fun resolveAvatarUrl(
        avatarFileId: String,
        repository: ChatRepository,
        forceRefresh: Boolean
    ): String? {
        if (avatarFileId.startsWith("http://") || avatarFileId.startsWith("https://")) {
            return avatarFileId
        }

        if (!forceRefresh) {
            AvatarLoader.urlCache[avatarFileId]?.let { return it }
            AvatarLoader.getUrlFromCache(avatarFileId)?.let {
                AvatarLoader.urlCache[avatarFileId] = it
                return it
            }
        }

        val url = repository.getFileDownloadUrl(avatarFileId).getOrNull()
        if (!url.isNullOrBlank()) {
            AvatarLoader.urlCache[avatarFileId] = url
            AvatarLoader.putUrlInCache(avatarFileId, url)
        }
        return url
    }

    private suspend fun loadAvatarUrl(
        avatarImage: ImageView,
        avatarInitials: TextView,
        cacheKey: String,
        avatarUrl: String
    ): Boolean = suspendCancellableCoroutine { continuation ->
        avatarImage.tag = cacheKey

        val request = ImageRequest.Builder(avatarImage.context)
            .data(avatarUrl)
            .memoryCacheKey(cacheKey)
            .diskCacheKey(cacheKey)
            .crossfade(200)
            .transformations(CircleCropTransformation())
            .size(Size.ORIGINAL)
            .target(
                onSuccess = { drawable ->
                    if (avatarImage.tag == cacheKey) {
                        avatarImage.setImageDrawable(drawable)
                        avatarImage.visibility = View.VISIBLE
                        avatarInitials.visibility = View.GONE
                    }
                    if (continuation.isActive) continuation.resume(true)
                },
                onError = {
                    if (avatarImage.tag == cacheKey) {
                        avatarImage.visibility = View.GONE
                        AvatarLoader.showPlaceholder(avatarInitials, callerName, callerUserId)
                    }
                    if (continuation.isActive) continuation.resume(false)
                }
            )
            .build()

        val disposable = AvatarLoader.getImageLoader(avatarImage.context).enqueue(request)
        continuation.invokeOnCancellation { disposable.dispose() }
    }
    private fun initialsOf(name: String): String {
        val parts = name.trim().split(Regex("\\s+")).filter { it.isNotBlank() }
        return when {
            parts.isEmpty() -> "?"
            parts.size == 1 -> parts[0].take(1).uppercase()
            else -> (parts[0].take(1) + parts[1].take(1)).uppercase()
        }
    }

    private fun acceptCall() {
        if (actionTaken) return
        actionTaken = true
        lifecycleScope.launch {
            if (!ensureCallsClient()) return@launch
            CallTelecomRegistry.markAnswering(callId)
            // Обновляем токен до accept() — приложение могло проснуться из фона с истёкшим токеном,
            // и тогда стрим событий звонка оборвётся на 401 и сервер завершит звонок.
            (application as BarkFluffApplication).grpcManager.ensureTokenValid(this@IncomingCallActivity)
            val response = (application as BarkFluffApplication).callRepository.accept(callId)
            response.onSuccess {
                CallTelecomRegistry.markActive(callId)
                NotificationHelper.clearIncomingCallAlert(this@IncomingCallActivity, callId)
                startActivity(Intent(this@IncomingCallActivity, CallActivity::class.java).apply {
                    putExtra(CallExtras.EXTRA_CALL_ID, callId)
                    putExtra(CallExtras.EXTRA_CALLER_NAME, callerName)
                    putExtra(CallExtras.EXTRA_MEDIA_TYPE, mediaType)
                    putExtra(CallExtras.EXTRA_LIVEKIT_URL, it.livekitUrl)
                    putExtra(CallExtras.EXTRA_ACCESS_TOKEN, it.accessToken)
                })
                finish()
            }.onFailure {
                CallTelecomRegistry.clearAnswering(callId)
                actionTaken = false
                Toast.makeText(this@IncomingCallActivity, "Не удалось принять звонок", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun rejectCall() {
        if (actionTaken) return
        actionTaken = true
        lifecycleScope.launch {
            if (ensureCallsClient()) {
                (application as BarkFluffApplication).callRepository.reject(callId)
            }
            CallTelecomRegistry.disconnect(callId, DisconnectCause.REJECTED)
            NotificationHelper.dismissCall(this@IncomingCallActivity, callId)
            finish()
        }
    }

    private fun ensureCallsClient(): Boolean {
        val app = application as BarkFluffApplication
        if (app.grpcManager.callsClient != null) return true

        val globalParam = GlobalParam(this)
        val callsAddress = globalParam.socketCalls
        if (callsAddress.isBlank()) {
            Toast.makeText(this, "Сервер звонков не настроен", Toast.LENGTH_SHORT).show()
            return false
        }

        return app.grpcManager.createCallsClient(callsAddress, this, includeDeviceInfo = true).isSuccess
    }

    companion object {
        private const val AVATAR_LOAD_ATTEMPTS = 3
        private const val AVATAR_LOAD_RETRY_DELAY_MS = 700L
        private const val RING_DURATION_MS = 1800L
        private const val RING_STAGGER_MS = 700L
    }
}
