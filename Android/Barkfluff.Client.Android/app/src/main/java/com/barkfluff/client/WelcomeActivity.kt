package com.barkfluff.client

import android.Manifest
import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.drawable.RotateDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.Settings
import android.view.Gravity
import android.view.MotionEvent
import android.view.VelocityTracker
import android.view.View
import android.view.ViewConfiguration
import android.widget.FrameLayout
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import androidx.dynamicanimation.animation.FloatValueHolder
import androidx.dynamicanimation.animation.SpringAnimation
import androidx.dynamicanimation.animation.SpringForce
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityWelcomeBinding
import com.barkfluff.client.utils.LegalDocsRepository
import com.google.android.material.color.DynamicColors
import kotlin.math.abs

class WelcomeActivity : AppCompatActivity() {

    private lateinit var binding: ActivityWelcomeBinding
    private var legalExpanded = false
    private var legalAnimation: SpringAnimation? = null
    private var legalVelocityTracker: VelocityTracker? = null

    // Permission launcher for normal permissions
    private val permissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { permissions ->
        val allGranted = permissions.values.all { it }
        if (allGranted) {
            Toast.makeText(this, R.string.welcome_permissions_granted, Toast.LENGTH_SHORT).show()
        }
        navigateToMain()
    }

    // Launcher for MANAGE_EXTERNAL_STORAGE permission (Android 11+)
    private val manageStorageLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            if (Environment.isExternalStorageManager()) {
                Toast.makeText(this, R.string.welcome_storage_granted, Toast.LENGTH_SHORT).show()
                requestRemainingPermissions()
            } else {
                Toast.makeText(this, R.string.welcome_storage_denied, Toast.LENGTH_SHORT).show()
                navigateToMain()
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        DynamicColors.applyToActivityIfAvailable(this)
        super.onCreate(savedInstanceState)

        binding = ActivityWelcomeBinding.inflate(layoutInflater)
        setContentView(binding.root)

        applyAdaptiveContentWidth()
        configureFeatureChipsAccessibility()

        // Edge-to-edge: инсеты вешаем на contentPanel, а не на android.R.id.content.
        // Иначе весь корень уезжает вниз, над ним остаётся полоса windowBackground другого
        // цвета, а декоративные круги обрезаются по нижней границе статус-бара.
        ViewCompat.setOnApplyWindowInsetsListener(binding.contentPanel) { v, insets ->
            val systemBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.updatePadding(
                top = systemBars.top + WELCOME_TOP_PADDING_DP.dpToPx(),
                bottom = systemBars.bottom + WELCOME_BOTTOM_PADDING_DP.dpToPx()
            )
            insets
        }

        setupClickListeners()
        setupLegalDisclosure(savedInstanceState?.getBoolean("legalExpanded") == true)
    }

    @SuppressLint("ClickableViewAccessibility") // Taps still call performClick; keyboard/TalkBack use the same listener.
    private fun setupLegalDisclosure(initiallyExpanded: Boolean) {
        legalExpanded = initiallyExpanded
        var progress = if (initiallyExpanded) 1f else 0f
        val travel = 48.dpToPx().toFloat()
        val touchSlop = ViewConfiguration.get(this).scaledTouchSlop
        var downY = 0f
        var downX = 0f
        var downProgress = progress
        var dragging = false
        val chevron = RotateDrawable().apply {
            drawable = binding.legalToggle.drawable
            fromDegrees = 0f
            toDegrees = 180f
        }
        binding.legalToggle.setImageDrawable(chevron)

        fun render(value: Float) {
            progress = value.coerceIn(0f, 1f)
            chevron.level = (10_000 * progress).toInt()
            binding.legalLinks.apply {
                visibility = if (progress > 0f) View.VISIBLE else View.INVISIBLE
                alpha = progress
                translationY = travel * (1f - progress)
                importantForAccessibility = if (progress == 1f) {
                    View.IMPORTANT_FOR_ACCESSIBILITY_AUTO
                } else {
                    View.IMPORTANT_FOR_ACCESSIBILITY_NO_HIDE_DESCENDANTS
                }
            }
            binding.termsLink.isEnabled = progress == 1f
            binding.privacyLink.isEnabled = progress == 1f
        }

        val spring = SpringAnimation(FloatValueHolder(progress)).apply {
            setMinValue(0f)
            setMaxValue(1f)
            setMinimumVisibleChange(0.001f)
            spring = SpringForce(progress).apply {
                stiffness = 1400f
                dampingRatio = 1f
            }
            addUpdateListener { _, value, _ -> render(value) }
        }
        legalAnimation = spring

        fun settle(expanded: Boolean, velocity: Float = 0f) {
            legalExpanded = expanded
            binding.legalToggle.contentDescription = getString(
                if (expanded) R.string.welcome_hide_legal else R.string.welcome_show_legal
            )
            spring.cancel()
            val target = if (expanded) 1f else 0f
            if (ValueAnimator.areAnimatorsEnabled()) {
                spring.setStartValue(progress).setStartVelocity(velocity)
                spring.animateToFinalPosition(target)
            } else {
                render(target)
            }
        }

        render(progress)
        binding.legalToggle.contentDescription = getString(
            if (initiallyExpanded) R.string.welcome_hide_legal else R.string.welcome_show_legal
        )
        binding.legalToggle.setOnClickListener { settle(!legalExpanded) }
        binding.legalToggle.setOnTouchListener { view, event ->
            when (event.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    spring.cancel()
                    downY = event.rawY
                    downX = event.rawX
                    downProgress = progress
                    dragging = false
                    legalVelocityTracker?.recycle()
                    legalVelocityTracker = VelocityTracker.obtain()
                    legalVelocityTracker?.addMovement(event)
                    view.parent.requestDisallowInterceptTouchEvent(true)
                }
                MotionEvent.ACTION_MOVE -> {
                    legalVelocityTracker?.addMovement(event)
                    val dy = event.rawY - downY
                    if (abs(dy) > touchSlop && abs(dy) > abs(event.rawX - downX)) dragging = true
                    if (dragging) render(downProgress - dy / travel)
                }
                MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                    legalVelocityTracker?.addMovement(event)
                    legalVelocityTracker?.computeCurrentVelocity(1000)
                    val velocity = -(legalVelocityTracker?.yVelocity ?: 0f) / travel
                    if (event.actionMasked == MotionEvent.ACTION_CANCEL) {
                        settle(legalExpanded)
                    } else if (dragging) {
                        settle(progress + velocity * 0.12f > 0.5f, velocity)
                    } else {
                        view.performClick()
                    }
                    legalVelocityTracker?.recycle()
                    legalVelocityTracker = null
                    view.parent.requestDisallowInterceptTouchEvent(false)
                }
            }
            true
        }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        outState.putBoolean("legalExpanded", legalExpanded)
        super.onSaveInstanceState(outState)
    }

    override fun onDestroy() {
        legalAnimation?.cancel()
        legalVelocityTracker?.recycle()
        super.onDestroy()
    }

    /**
     * На compact-экранах сохраняем full-width композицию макета 1c.
     * В окнах Medium/Expanded ограничиваем ширину контента и центрируем его,
     * чтобы CTA и hero не растягивались на всю ширину планшета или landscape-окна.
     */
    private fun applyAdaptiveContentWidth() {
        if (resources.configuration.screenWidthDp < MEDIUM_WINDOW_MIN_WIDTH_DP) return

        val sideMarginPx = resources.getDimensionPixelSize(R.dimen.welcome_medium_window_margin)
        val maxContentWidthPx = resources.getDimensionPixelSize(R.dimen.welcome_content_max_width)
        val availableWidthPx = resources.configuration.screenWidthDp.dpToPx()
        val contentWidthPx = minOf(maxContentWidthPx, availableWidthPx - sideMarginPx * 2)
        if (contentWidthPx <= 0) return

        val layoutParams = binding.contentPanel.layoutParams as? FrameLayout.LayoutParams
            ?: return
        layoutParams.width = contentWidthPx
        layoutParams.gravity = Gravity.CENTER_HORIZONTAL
        binding.contentPanel.layoutParams = layoutParams
    }

    /**
     * Feature-чипы — информационные метки, а не действия. Оставляем их
     * узлами TalkBack для чтения, но убираем лишний keyboard focus, который
     * Material Chip может включить после инфлейта.
     */
    private fun configureFeatureChipsAccessibility() {
        listOf(binding.chipFast, binding.chipPrivate, binding.chipOwnPack).forEach { chip ->
            chip.isClickable = false
            chip.isFocusable = false
            chip.isFocusableInTouchMode = false
            chip.importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_YES
        }
    }

    /**
     * Согласие требуется, если оно ещё не давалось или соглашение обновилось с прошлого раза.
     * Редакцию берём из самого документа, а не из версии приложения.
     */
    private fun legalConsentRequired(): Boolean {
        val current = runCatching { LegalDocsRepository.revision(this) }.getOrDefault("")
        return current.isNotEmpty() && current != GlobalParam(this).acceptedLegalRevision
    }

    private fun requestLegalConsent() {
        supportFragmentManager.setFragmentResultListener(
            LegalConsentBottomSheet.RESULT_KEY,
            this
        ) { _, result ->
            if (result.getBoolean(LegalConsentBottomSheet.RESULT_ACCEPTED)) {
                GlobalParam(this).acceptedLegalRevision =
                    runCatching { LegalDocsRepository.revision(this) }.getOrDefault("")
                navigateToSelectServer()
            }
        }
        LegalConsentBottomSheet.forConsent()
            .show(supportFragmentManager, LegalConsentBottomSheet.TAG)
    }

    private fun setupClickListeners() {
        binding.startButton.setOnClickListener {
            if (legalConsentRequired()) requestLegalConsent() else navigateToSelectServer()
        }

        binding.termsLink.setOnClickListener {
            LegalConsentBottomSheet.forReading(LegalConsentBottomSheet.TAB_TERMS)
                .show(supportFragmentManager, LegalConsentBottomSheet.TAG)
        }

        binding.privacyLink.setOnClickListener {
            LegalConsentBottomSheet.forReading(LegalConsentBottomSheet.TAB_PRIVACY)
                .show(supportFragmentManager, LegalConsentBottomSheet.TAG)
        }
    }

    private fun navigateToSelectServer() {
        val intent = Intent(this, SelectServerActivity::class.java)
        startActivity(intent)
        overridePendingTransition(android.R.anim.fade_in, android.R.anim.fade_out)
        finish() // Блокируем возврат на эту страницу
    }

    private fun requestAllPermissions() {
        // First, request MANAGE_EXTERNAL_STORAGE for Android 11+
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            if (!Environment.isExternalStorageManager()) {
                val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION).apply {
                    data = Uri.parse("package:$packageName")
                }
                manageStorageLauncher.launch(intent)
                return
            }
        }

        // If we already have storage access or on older Android, request remaining permissions
        requestRemainingPermissions()
    }

    private fun requestRemainingPermissions() {
        val permissionsToRequest = mutableListOf<String>()

        // Add notification permission for Android 13+
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.POST_NOTIFICATIONS
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.POST_NOTIFICATIONS)
            }
        }

        // Add media permissions for Android 13+
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_MEDIA_IMAGES
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.READ_MEDIA_IMAGES)
            }
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_MEDIA_VIDEO
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.READ_MEDIA_VIDEO)
            }
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_MEDIA_AUDIO
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.READ_MEDIA_AUDIO)
            }
        } else if (Build.VERSION.SDK_INT <= Build.VERSION_CODES.Q) {
            // For Android 10 and below
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.READ_EXTERNAL_STORAGE
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.READ_EXTERNAL_STORAGE)
            }
            if (ContextCompat.checkSelfPermission(
                    this,
                    Manifest.permission.WRITE_EXTERNAL_STORAGE
                ) != PackageManager.PERMISSION_GRANTED
            ) {
                permissionsToRequest.add(Manifest.permission.WRITE_EXTERNAL_STORAGE)
            }
        }

        if (permissionsToRequest.isNotEmpty()) {
            permissionLauncher.launch(permissionsToRequest.toTypedArray())
        } else {
            navigateToMain()
        }
    }

    private fun navigateToMain() {
        val intent = Intent(this, SelectServerActivity::class.java)
        startActivity(intent)
        overridePendingTransition(android.R.anim.fade_in, android.R.anim.fade_out)
    }

    private fun Int.dpToPx(): Int = (this * resources.displayMetrics.density).toInt()

    companion object {
        private const val MEDIUM_WINDOW_MIN_WIDTH_DP = 600

        /** Отступ hero-блока от статус-бара; складывается с системным инсетом. */
        private const val WELCOME_TOP_PADDING_DP = 24
        private const val WELCOME_BOTTOM_PADDING_DP = 24
    }
}
