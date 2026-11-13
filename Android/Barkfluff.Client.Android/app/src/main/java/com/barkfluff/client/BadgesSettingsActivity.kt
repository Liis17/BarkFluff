package com.barkfluff.client

import android.os.Bundle
import android.util.Log
import android.view.View
import androidx.appcompat.app.AppCompatActivity
import androidx.lifecycle.lifecycleScope
import androidx.recyclerview.widget.GridLayoutManager
import com.barkfluff.client.badges.BadgeGridAdapter
import com.barkfluff.client.badges.BadgeImageLoader
import com.barkfluff.client.badges.BadgeViewerSheet
import com.barkfluff.client.cache.UserBadgeRepository
import com.barkfluff.client.data.GlobalParam
import com.barkfluff.client.databinding.ActivityBadgesSettingsBinding
import com.barkfluff.client.domain.model.UserBadgeInfo
import com.barkfluff.client.domain.model.BadgeFeatureUnavailableException
import com.google.android.material.snackbar.Snackbar
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.launch

@AndroidEntryPoint
class BadgesSettingsActivity : AppCompatActivity() {

    private lateinit var binding: ActivityBadgesSettingsBinding
    @javax.inject.Inject lateinit var userBadgeRepository: UserBadgeRepository

    private val userId by lazy { GlobalParam(this).userId }
    private var badges: List<UserBadgeInfo> = emptyList()
    private lateinit var gridAdapter: BadgeGridAdapter
    private var isChangingPrimary = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityBadgesSettingsBinding.inflate(layoutInflater)
        setContentView(binding.root)

        binding.toolbar.setNavigationOnClickListener { onBackPressedDispatcher.onBackPressed() }
        gridAdapter = BadgeGridAdapter { badge ->
            if (badge == null) {
                changePrimary(0)
            } else {
                BadgeViewerSheet.newInstance(badge, canChangePrimary = true)
                    .show(supportFragmentManager, BADGE_VIEWER_TAG)
            }
        }
        binding.badgeGrid.layoutManager = GridLayoutManager(this, GRID_COLUMNS)
        binding.badgeGrid.adapter = gridAdapter
        binding.currentBadgeCard.setOnClickListener {
            currentPrimary()?.let { badge ->
                BadgeViewerSheet.newInstance(badge, canChangePrimary = true)
                    .show(supportFragmentManager, BADGE_VIEWER_TAG)
            }
        }
        renderBadges()
        supportFragmentManager.setFragmentResultListener(BadgeViewerSheet.RESULT_KEY, this) { _, result ->
            changePrimary(result.getInt(BadgeViewerSheet.RESULT_BADGE_ID))
        }

        if (userId <= 0L) {
            showError(getString(R.string.badges_load_error))
            return
        }

        lifecycleScope.launch {
            userBadgeRepository.observe(userId).collect { updated ->
                badges = updated
                renderBadges()
            }
        }
        lifecycleScope.launch {
            val result = userBadgeRepository.refresh(userId)
            if (result.isFailure) {
                showFailure(result.exceptionOrNull(), R.string.badges_load_error, "Не удалось загрузить бейджи")
            }
        }
    }

    private fun renderBadges() {
        val primary = currentPrimary()
        binding.emptyBadges.visibility = if (badges.isEmpty()) View.VISIBLE else View.GONE
        binding.currentBadgeName.text = primary?.name ?: getString(R.string.badge_no_primary)
        binding.currentBadgeHint.setText(
            if (primary == null) R.string.badges_current_hint else R.string.badges_current_selected_hint
        )
        binding.currentBadgeArrow.visibility = if (primary == null) View.GONE else View.VISIBLE
        binding.currentBadgeCard.isClickable = primary != null && !isChangingPrimary
        binding.currentBadgeCard.isFocusable = primary != null && !isChangingPrimary
        if (primary == null) {
            BadgeImageLoader.showNoBadge(binding.currentBadgeImage)
        } else {
            BadgeImageLoader.loadMini(binding.currentBadgeImage, primary)
            binding.currentBadgeImage.contentDescription = primary.name
        }
        gridAdapter.showBadges(badges, primary?.badgeId)
    }

    private fun currentPrimary(): UserBadgeInfo? = badges.firstOrNull { it.isPrimary }

    private fun changePrimary(badgeId: Int) {
        if (isChangingPrimary) return
        if (badgeId == (currentPrimary()?.badgeId ?: 0)) return

        isChangingPrimary = true
        binding.currentBadgeCard.isEnabled = false
        gridAdapter.setInteractionsEnabled(false)
        lifecycleScope.launch {
            val result = userBadgeRepository.setPrimary(userId, badgeId)
            isChangingPrimary = false
            binding.currentBadgeCard.isEnabled = true
            val hasPrimaryBadge = currentPrimary() != null
            binding.currentBadgeCard.isClickable = hasPrimaryBadge
            binding.currentBadgeCard.isFocusable = hasPrimaryBadge
            gridAdapter.setInteractionsEnabled(true)
            if (result.isFailure) {
                showFailure(result.exceptionOrNull(), R.string.badge_set_error, "Не удалось изменить основной бейдж")
            }
        }
    }

    private fun showFailure(error: Throwable?, fallbackStringId: Int, operation: String) {
        if (error != null) Log.e(TAG, operation, error)
        val stringId = if (error is BadgeFeatureUnavailableException) {
            R.string.badge_feature_unavailable
        } else {
            fallbackStringId
        }
        showError(getString(stringId))
    }

    private fun showError(message: String) {
        Snackbar.make(binding.root, message, Snackbar.LENGTH_LONG).show()
    }

    private companion object {
        const val TAG = "BadgesSettingsActivity"
        const val BADGE_VIEWER_TAG = "badge-viewer"
        const val GRID_COLUMNS = 4
    }
}
