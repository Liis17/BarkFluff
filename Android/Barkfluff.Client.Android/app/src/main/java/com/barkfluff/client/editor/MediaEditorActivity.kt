package com.barkfluff.client.editor

import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.util.Log
import android.view.View
import androidx.activity.OnBackPressedCallback
import androidx.appcompat.app.AppCompatActivity
import androidx.core.net.toUri
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updateLayoutParams
import androidx.core.view.updateMargins
import androidx.lifecycle.lifecycleScope
import androidx.viewpager2.widget.ViewPager2
import com.barkfluff.client.R
import com.barkfluff.client.databinding.ActivityMediaEditorBinding
import com.yalantis.ucrop.callback.BitmapCropCallback
import com.yalantis.ucrop.view.CropImageView
import com.yalantis.ucrop.view.OverlayView
import com.yalantis.ucrop.view.UCropView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream

/**
 * Полноценный редактор картинок: ViewPager2 по всему списку картинок галереи
 * + чекбокс выбора + caption + инструменты обрезки/поворота/отражения/рисования.
 *
 * Возвращает в caller (ImagePickerBottomSheet) обновлённый список выбранных URIs
 * и обновлённый текст caption. Если был нажат кнопка "Отправить" — флаг EXTRA_SEND=true.
 */
class MediaEditorActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMediaEditorBinding
    private lateinit var adapter: MediaEditorPagerAdapter

    private val allUris = mutableListOf<Uri>()
    private val selectedUris = mutableListOf<Uri>()

    private enum class Tool { NONE, CROP, ROTATE, FLIP, DRAW }
    private var currentTool: Tool = Tool.NONE

    /** Bitmap текущей страницы в edit-mode (rotate/flip/draw). Перед confirm — отображается на PhotoView. */
    private var workingBitmap: Bitmap? = null

    private var activeUCropView: UCropView? = null
    private var cropOutputFile: File? = null
    private var cropInputFile: File? = null

    companion object {
        private const val TAG = "MediaEditor"
        const val EXTRA_ALL_URIS = "all_uris"
        const val EXTRA_START_URI = "start_uri"
        const val EXTRA_PRESELECTED_URIS = "preselected_uris"
        const val EXTRA_CAPTION = "caption"
        const val EXTRA_RESULT_URIS = "result_uris"
        const val EXTRA_RESULT_CAPTION = "result_caption"
        const val EXTRA_RESULT_SEND = "result_send"

        fun newIntent(
            context: Context,
            allUris: List<Uri>,
            startUri: Uri,
            preselected: List<Uri>,
            caption: String
        ): Intent {
            return Intent(context, MediaEditorActivity::class.java).apply {
                putParcelableArrayListExtra(EXTRA_ALL_URIS, ArrayList(allUris))
                putExtra(EXTRA_START_URI, startUri)
                putParcelableArrayListExtra(EXTRA_PRESELECTED_URIS, ArrayList(preselected))
                putExtra(EXTRA_CAPTION, caption)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMediaEditorBinding.inflate(layoutInflater)
        setContentView(binding.root)

        readIntentData()
        applyStatusBarInsets()
        setupViewPager()
        setupTopBar()
        setupBottomBar()
        setupToolButtons()
        setupBackPress()
    }

    private fun readIntentData() {
        val all: ArrayList<Uri>? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableArrayListExtra(EXTRA_ALL_URIS, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableArrayListExtra(EXTRA_ALL_URIS)
        }
        val pre: ArrayList<Uri>? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableArrayListExtra(EXTRA_PRESELECTED_URIS, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableArrayListExtra(EXTRA_PRESELECTED_URIS)
        }
        val start: Uri? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(EXTRA_START_URI, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(EXTRA_START_URI)
        }

        if (all == null || all.isEmpty() || start == null) {
            finish()
            return
        }
        allUris.addAll(all)
        pre?.let { selectedUris.addAll(it) }
        // Если стартовая ещё не выбрана — добавим (auto-select on open)
        if (!selectedUris.contains(start)) selectedUris.add(start)

        binding.captionEditText.setText(intent.getStringExtra(EXTRA_CAPTION) ?: "")
    }

    private fun applyStatusBarInsets() {
        ViewCompat.setOnApplyWindowInsetsListener(binding.root) { _, insets ->
            val sysBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            val ime = insets.getInsets(WindowInsetsCompat.Type.ime()).bottom
            binding.topBar.updateLayoutParams<android.view.ViewGroup.MarginLayoutParams> {
                updateMargins(top = sysBars.top)
            }
            binding.confirmFab.updateLayoutParams<android.view.ViewGroup.MarginLayoutParams> {
                updateMargins(top = sysBars.top + dp(8))
            }
            // Когда клавиатура поднята — поле ввода и инструменты сидят над ней
            val bottomMargin = maxOf(ime, sysBars.bottom)
            binding.bottomBar.updateLayoutParams<android.view.ViewGroup.MarginLayoutParams> {
                updateMargins(bottom = bottomMargin)
            }
            insets
        }
    }

    private fun setupViewPager() {
        adapter = MediaEditorPagerAdapter(allUris) { uri -> loadBitmapDownsampled(uri) }
        binding.viewPager.adapter = adapter
        binding.viewPager.offscreenPageLimit = 1

        // Стартовая позиция
        val start: Uri? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(EXTRA_START_URI, Uri::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(EXTRA_START_URI)
        }
        val idx = start?.let { allUris.indexOf(it) } ?: 0
        binding.viewPager.setCurrentItem(idx.coerceAtLeast(0), false)

        binding.viewPager.registerOnPageChangeCallback(object : ViewPager2.OnPageChangeCallback() {
            override fun onPageSelected(position: Int) {
                // При свайпе сбрасываем активный инструмент
                resetTool()
                updateTopBar()
            }
        })

        updateTopBar()
    }

    private fun setupTopBar() {
        binding.closeButton.setOnClickListener { finishWithResult(send = false) }
        binding.checkboxTouchTarget.setOnClickListener { toggleCurrentSelection() }
    }

    private fun setupBottomBar() {
        binding.sendButton.setOnClickListener { finishWithResult(send = true) }
    }

    private fun setupToolButtons() {
        binding.btnCrop.setOnClickListener { activateTool(Tool.CROP) }
        binding.btnRotate.setOnClickListener { activateTool(Tool.ROTATE) }
        binding.btnFlip.setOnClickListener { activateTool(Tool.FLIP) }
        binding.btnDraw.setOnClickListener { activateTool(Tool.DRAW) }
        binding.btnUndo.setOnClickListener { onUndo() }
        binding.confirmFab.setOnClickListener { confirmTool() }
    }

    private fun setupBackPress() {
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                if (currentTool != Tool.NONE) {
                    cancelTool()
                } else {
                    finishWithResult(send = false)
                }
            }
        })
    }

    private fun toggleCurrentSelection() {
        val uri = currentUri() ?: return
        if (selectedUris.contains(uri)) {
            if (selectedUris.size <= 1) {
                // Не позволяем снять единственный выбор — без выбора нечего отправлять
                return
            }
            selectedUris.remove(uri)
        } else {
            selectedUris.add(uri)
        }
        updateTopBar()
    }

    private fun updateTopBar() {
        val uri = currentUri()
        val pos = binding.viewPager.currentItem + 1
        binding.counterText.text = "$pos / ${allUris.size}"

        val idx = uri?.let { selectedUris.indexOf(it) } ?: -1
        if (idx >= 0) {
            binding.selectionIndicator.setBackgroundResource(R.drawable.selection_indicator_selected_background)
            binding.checkIcon.visibility = View.GONE
            binding.selectionNumber.visibility = View.VISIBLE
            binding.selectionNumber.text = (idx + 1).toString()
        } else {
            binding.selectionIndicator.setBackgroundResource(R.drawable.selection_indicator_background)
            binding.checkIcon.visibility = View.GONE
            binding.selectionNumber.visibility = View.GONE
        }
    }

    private fun currentUri(): Uri? = allUris.getOrNull(binding.viewPager.currentItem)

    private fun currentHolder(): MediaEditorPagerAdapter.PageHolder? {
        val rv = binding.viewPager.getChildAt(0) as? androidx.recyclerview.widget.RecyclerView
            ?: return null
        return rv.findViewHolderForAdapterPosition(binding.viewPager.currentItem)
                as? MediaEditorPagerAdapter.PageHolder
    }

    // ===== Tools =====

    private fun activateTool(tool: Tool) {
        // Повторный тап в режиме ROTATE/FLIP — добавляет ещё одно преобразование
        if (currentTool == tool && (tool == Tool.ROTATE || tool == Tool.FLIP)) {
            val holder = currentHolder() ?: return
            if (tool == Tool.ROTATE) applyRotate(holder, 90) else applyFlip(holder)
            return
        }
        if (currentTool == tool) {
            cancelTool()
            return
        }
        if (currentTool != Tool.NONE) cancelTool()
        currentTool = tool
        showEditModeUi(true, tool)

        val holder = currentHolder() ?: run { cancelTool(); return }
        val source = holder.currentBitmap ?: run { cancelTool(); return }
        workingBitmap = source

        when (tool) {
            Tool.CROP -> startCrop(holder, source)
            Tool.ROTATE -> applyRotate(holder, 90)
            Tool.FLIP -> applyFlip(holder)
            Tool.DRAW -> startDraw(holder, source)
            Tool.NONE -> Unit
        }
    }

    private fun showEditModeUi(editing: Boolean, tool: Tool = Tool.NONE) {
        // bottomBar (caption + send) скрываем во время редактирования, чтобы случайно не отправить
        binding.bottomBar.visibility = if (editing) View.GONE else View.VISIBLE
        // toolsBar (crop/rotate/flip/draw) — ВСЕГДА виден, чтобы пользователь мог переключаться между инструментами
        binding.toolsBar.visibility = View.VISIBLE
        binding.confirmFab.visibility = if (editing) View.VISIBLE else View.GONE
        binding.btnUndo.visibility = if (editing && tool == Tool.DRAW) View.VISIBLE else View.GONE
        binding.colorPalette.visibility = if (editing && tool == Tool.DRAW) View.VISIBLE else View.GONE
        binding.brushSlider.visibility = if (editing && tool == Tool.DRAW) View.VISIBLE else View.GONE
        binding.checkboxTouchTarget.visibility = if (editing) View.GONE else View.VISIBLE

        // Подсветка активной кнопки в toolsBar (alpha)
        binding.btnCrop.alpha = if (editing && tool == Tool.CROP) 1f else 0.6f
        binding.btnRotate.alpha = if (editing && tool == Tool.ROTATE) 1f else 0.6f
        binding.btnFlip.alpha = if (editing && tool == Tool.FLIP) 1f else 0.6f
        binding.btnDraw.alpha = if (editing && tool == Tool.DRAW) 1f else 0.6f
        if (!editing) {
            binding.btnCrop.alpha = 1f
            binding.btnRotate.alpha = 1f
            binding.btnFlip.alpha = 1f
            binding.btnDraw.alpha = 1f
        }

        // Блокировка свайпа pager в режиме редактирования
        binding.viewPager.isUserInputEnabled = !editing
    }

    private fun cancelTool() {
        val holder = currentHolder()
        when (currentTool) {
            Tool.CROP -> teardownCrop(holder)
            Tool.DRAW -> teardownDraw(holder)
            else -> Unit
        }
        // Откатываем PhotoView к актуальному bitmap (из кеша или оригинал)
        holder?.let { rebindCurrentBitmap(it) }
        currentTool = Tool.NONE
        workingBitmap = null
        showEditModeUi(false)
    }

    private fun confirmTool() {
        val holder = currentHolder() ?: run { cancelTool(); return }
        when (currentTool) {
            Tool.CROP -> finishCrop(holder)
            Tool.ROTATE, Tool.FLIP -> {
                val bmp = workingBitmap ?: run { cancelTool(); return }
                saveEditedToCache(bmp, rotated = currentTool == Tool.ROTATE, flipped = currentTool == Tool.FLIP)
                finishToolNonAsync(holder)
            }
            Tool.DRAW -> finishDraw(holder)
            Tool.NONE -> Unit
        }
    }

    private fun finishToolNonAsync(holder: MediaEditorPagerAdapter.PageHolder) {
        currentTool = Tool.NONE
        workingBitmap = null
        showEditModeUi(false)
        rebindCurrentBitmap(holder)
    }

    // ----- CROP -----
    private fun startCrop(holder: MediaEditorPagerAdapter.PageHolder, src: Bitmap) {
        try {
            // Сохраняем src в tempFile — UCropView требует input URI
            val inFile = File.createTempFile("crop_in_", ".jpg", cacheDir)
            FileOutputStream(inFile).use { src.compress(Bitmap.CompressFormat.JPEG, 92, it) }
            val outFile = File.createTempFile("crop_out_", ".jpg", cacheDir)
            cropInputFile = inFile
            cropOutputFile = outFile

            val uCropView = UCropView(this, null)
            val cropImageView = uCropView.cropImageView
            val overlayView = uCropView.overlayView
            overlayView.setShowCropFrame(true)
            overlayView.setShowCropGrid(true)
            // Freestyle: каждая сторона/угол двигается независимо, без жёстких пропорций
            overlayView.setFreestyleCropMode(OverlayView.FREESTYLE_CROP_MODE_ENABLE)
            cropImageView.targetAspectRatio = CropImageView.SOURCE_IMAGE_ASPECT_RATIO
            cropImageView.setImageUri(Uri.fromFile(inFile), Uri.fromFile(outFile))

            // Отступы от краёв — чтобы ручки не были вплотную к рёбрам, где Android ловит back-swipe / recents
            val pad = dp(28)
            holder.cropContainer.setPadding(pad, pad, pad, pad)
            // Доп. защита: explicit system gesture exclusion на всю область UCropView
            uCropView.addOnLayoutChangeListener { v, l, t, r, b, _, _, _, _ ->
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    v.systemGestureExclusionRects = listOf(android.graphics.Rect(0, 0, r - l, b - t))
                }
            }

            holder.cropContainer.removeAllViews()
            holder.cropContainer.addView(uCropView)
            holder.cropContainer.visibility = View.VISIBLE
            holder.photoView.visibility = View.GONE
            activeUCropView = uCropView
        } catch (e: Exception) {
            Log.e(TAG, "startCrop failed", e)
            cancelTool()
        }
    }

    private fun teardownCrop(holder: MediaEditorPagerAdapter.PageHolder?) {
        if (holder != null && Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            activeUCropView?.systemGestureExclusionRects = emptyList()
        }
        holder?.cropContainer?.setPadding(0, 0, 0, 0)
        holder?.cropContainer?.removeAllViews()
        holder?.cropContainer?.visibility = View.GONE
        holder?.photoView?.visibility = View.VISIBLE
        cropInputFile?.delete()
        cropOutputFile?.delete()
        cropInputFile = null
        cropOutputFile = null
        activeUCropView = null
    }

    private fun finishCrop(holder: MediaEditorPagerAdapter.PageHolder) {
        val cropView = activeUCropView ?: run { cancelTool(); return }
        cropView.cropImageView.cropAndSaveImage(
            Bitmap.CompressFormat.JPEG,
            92,
            object : BitmapCropCallback {
                override fun onBitmapCropped(
                    resultUri: Uri,
                    offsetX: Int,
                    offsetY: Int,
                    imageWidth: Int,
                    imageHeight: Int
                ) {
                    lifecycleScope.launch {
                        val bytes = withContext(Dispatchers.IO) {
                            try {
                                val f = File(resultUri.path ?: return@withContext null)
                                f.readBytes()
                            } catch (e: Exception) { null }
                        } ?: run { cancelTool(); return@launch }
                        val uri = currentUri() ?: run { cancelTool(); return@launch }
                        val prev = MediaEditCache.get(uri)
                        MediaEditCache.put(
                            uri,
                            MediaEditCache.EditedImage(
                                bytes = bytes,
                                wasCropped = true,
                                wasRotated = prev?.wasRotated == true,
                                wasFlipped = prev?.wasFlipped == true,
                                wasDrawn = prev?.wasDrawn == true
                            )
                        )
                        teardownCrop(holder)
                        finishToolNonAsync(holder)
                    }
                }

                override fun onCropFailure(t: Throwable) {
                    Log.e(TAG, "crop failed", t)
                    cancelTool()
                }
            }
        )
    }

    // ----- ROTATE / FLIP -----
    private fun applyRotate(holder: MediaEditorPagerAdapter.PageHolder, degrees: Int) {
        val src = workingBitmap ?: return
        val matrix = Matrix().apply { postRotate(degrees.toFloat()) }
        val out = Bitmap.createBitmap(src, 0, 0, src.width, src.height, matrix, true)
        workingBitmap = out
        holder.photoView.setImageBitmap(out)
    }

    private fun applyFlip(holder: MediaEditorPagerAdapter.PageHolder) {
        val src = workingBitmap ?: return
        val matrix = Matrix().apply { preScale(-1f, 1f) }
        val out = Bitmap.createBitmap(src, 0, 0, src.width, src.height, matrix, true)
        workingBitmap = out
        holder.photoView.setImageBitmap(out)
    }

    // ----- DRAW -----
    private fun startDraw(holder: MediaEditorPagerAdapter.PageHolder, src: Bitmap) {
        // DrawingOverlayView сам рисует bitmap (с pinch-zoom/pan) — PhotoView под ней нужно скрыть
        holder.drawingOverlay.setSourceBitmap(src)
        holder.drawingOverlay.brushColor = binding.colorPalette.selectedColor()
        holder.drawingOverlay.brushWidthPx = binding.brushSlider.currentWidthPx()
        holder.drawingOverlay.visibility = View.VISIBLE
        holder.photoView.visibility = View.GONE

        binding.colorPalette.onColorSelected = { c ->
            holder.drawingOverlay.brushColor = c
            binding.brushSlider.brushColor = c
        }
        binding.brushSlider.onWidthChanged = { w ->
            holder.drawingOverlay.brushWidthPx = w
        }
        binding.brushSlider.brushColor = binding.colorPalette.selectedColor()
    }

    private fun teardownDraw(holder: MediaEditorPagerAdapter.PageHolder?) {
        holder?.drawingOverlay?.visibility = View.GONE
        holder?.drawingOverlay?.clearAll()
        holder?.drawingOverlay?.setSourceBitmap(null)
        holder?.photoView?.visibility = View.VISIBLE
        binding.colorPalette.onColorSelected = null
        binding.brushSlider.onWidthChanged = null
    }

    private fun onUndo() {
        if (currentTool != Tool.DRAW) return
        currentHolder()?.drawingOverlay?.undo()
    }

    private fun finishDraw(holder: MediaEditorPagerAdapter.PageHolder) {
        val result = holder.drawingOverlay.renderResultBitmap() ?: workingBitmap
        if (result == null) { cancelTool(); return }
        if (!holder.drawingOverlay.hasDrawings()) {
            // Ничего не рисовали — просто закрываем
            teardownDraw(holder)
            finishToolNonAsync(holder)
            return
        }
        saveEditedToCache(result, drawn = true)
        teardownDraw(holder)
        finishToolNonAsync(holder)
    }

    // ===== Helpers =====

    private fun saveEditedToCache(
        bmp: Bitmap,
        cropped: Boolean = false,
        rotated: Boolean = false,
        flipped: Boolean = false,
        drawn: Boolean = false
    ) {
        val uri = currentUri() ?: return
        val baos = java.io.ByteArrayOutputStream()
        bmp.compress(Bitmap.CompressFormat.JPEG, 92, baos)
        val prev = MediaEditCache.get(uri)
        MediaEditCache.put(
            uri,
            MediaEditCache.EditedImage(
                bytes = baos.toByteArray(),
                wasCropped = cropped || prev?.wasCropped == true,
                wasRotated = rotated || prev?.wasRotated == true,
                wasFlipped = flipped || prev?.wasFlipped == true,
                wasDrawn = drawn || prev?.wasDrawn == true
            )
        )
    }

    private fun rebindCurrentBitmap(holder: MediaEditorPagerAdapter.PageHolder) {
        val uri = currentUri() ?: return
        val edited = MediaEditCache.get(uri)?.bytes
        if (edited != null) {
            val bmp = BitmapFactory.decodeByteArray(edited, 0, edited.size)
            holder.currentBitmap = bmp
            holder.photoView.setImageBitmap(bmp)
        } else {
            holder.currentBitmap?.let { holder.photoView.setImageBitmap(it) }
        }
    }

    private fun resetTool() {
        if (currentTool != Tool.NONE) cancelTool()
    }

    private suspend fun loadBitmapDownsampled(uri: Uri, maxDim: Int = 2048): Bitmap? =
        withContext(Dispatchers.IO) {
            try {
                val opts = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                contentResolver.openInputStream(uri)?.use {
                    BitmapFactory.decodeStream(it, null, opts)
                }
                val w = opts.outWidth
                val h = opts.outHeight
                if (w <= 0 || h <= 0) return@withContext null
                var sample = 1
                while (w / sample > maxDim || h / sample > maxDim) sample *= 2
                val opts2 = BitmapFactory.Options().apply { inSampleSize = sample }
                contentResolver.openInputStream(uri)?.use {
                    BitmapFactory.decodeStream(it, null, opts2)
                }
            } catch (e: Exception) {
                Log.e(TAG, "loadBitmap failed", e)
                null
            }
        }

    private fun finishWithResult(send: Boolean) {
        val data = Intent().apply {
            putParcelableArrayListExtra(EXTRA_RESULT_URIS, ArrayList(selectedUris))
            putExtra(EXTRA_RESULT_CAPTION, binding.captionEditText.text.toString())
            putExtra(EXTRA_RESULT_SEND, send)
        }
        setResult(RESULT_OK, data)
        finish()
    }

    override fun onDestroy() {
        super.onDestroy()
        adapter.cancelAll()
        cropInputFile?.delete()
        cropOutputFile?.delete()
    }

    private fun dp(v: Int): Int = (v * resources.displayMetrics.density).toInt()
}
