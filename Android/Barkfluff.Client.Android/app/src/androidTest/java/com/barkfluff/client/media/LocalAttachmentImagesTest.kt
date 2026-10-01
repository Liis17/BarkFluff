package com.barkfluff.client.media

import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.drawable.Drawable
import android.util.Base64
import android.widget.ImageView
import androidx.core.graphics.drawable.toBitmap
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import barkfluff.shared.Shared
import coil.memory.MemoryCache
import com.barkfluff.client.StorageSettingsActivity
import com.barkfluff.client.utils.AvatarLoader
import com.barkfluff.client.utils.ImageLoadHelper
import java.io.File
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class LocalAttachmentImagesTest {
    private val instrumentation get() = InstrumentationRegistry.getInstrumentation()
    private val context get() = instrumentation.targetContext

    @Test fun photosGifsAndVideoFramesLoadFromExtensionlessCacheFiles() {
        val directory = File(context.cacheDir, "autodownload-render-test").apply { mkdirs() }
        val photo = File(directory, "photo")
        val gif = File(directory, "gif")
        val video = File(directory, "video")
        try {
            photo.outputStream().use { output ->
                Bitmap.createBitmap(16, 16, Bitmap.Config.ARGB_8888).apply { eraseColor(Color.GREEN) }
                    .compress(Bitmap.CompressFormat.PNG, 100, output)
            }
            gif.writeBytes(Base64.decode("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", Base64.DEFAULT))
            instrumentation.context.assets.open("autodownload/frame.mp4").use { input -> video.outputStream().use(input::copyTo) }
            ActivityScenario.launch(StorageSettingsActivity::class.java).use { scenario ->
                for ((type, file) in listOf(Shared.MessageAttachmentType.IMAGE to photo,
                    Shared.MessageAttachmentType.GIF to gif, Shared.MessageAttachmentType.VIDEO to video)) {
                    val loaded = CountDownLatch(1)
                    lateinit var view: ImageView
                    scenario.onActivity { activity ->
                        view = object : ImageView(activity) {
                            override fun setImageDrawable(drawable: Drawable?) {
                                super.setImageDrawable(drawable)
                                if (drawable != null) loaded.countDown()
                            }
                        }
                        activity.setContentView(view)
                        val attachment = Shared.MessageAttachment.newBuilder().setFileId("render-test-${type.name}-${System.nanoTime()}")
                            .setType(type).build()
                        ImageLoadHelper.loadLocal(view, attachment, file)
                    }
                    assertTrue("Local $type did not render", loaded.await(10, TimeUnit.SECONDS))
                    scenario.onActivity {
                        val bitmap = view.drawable.toBitmap(16, 16).copy(Bitmap.Config.ARGB_8888, false)
                        when (type) {
                            Shared.MessageAttachmentType.IMAGE -> assertTrue(Color.green(bitmap.getPixel(8, 8)) > 200)
                            Shared.MessageAttachmentType.VIDEO -> assertTrue(Color.red(bitmap.getPixel(8, 8)) > 200 && Color.green(bitmap.getPixel(8, 8)) < 50)
                            else -> assertTrue(view.drawable.intrinsicWidth > 0)
                        }
                    }
                }
            }
        } finally { directory.deleteRecursively() }
    }

    @Test fun historicalGraphicCacheIsUsableWithoutAFileOrResolvingItsRemotePreview() = runBlocking {
        val fileId = "graphic-cache-test-${System.nanoTime()}"
        val bitmap = Bitmap.createBitmap(16, 16, Bitmap.Config.ARGB_8888).apply { eraseColor(Color.BLUE) }
        val loader = AvatarLoader.getImageLoader(context)
        val key = MemoryCache.Key(fileId)
        loader.memoryCache!![key] = MemoryCache.Value(bitmap)
        try {
            withContext(Dispatchers.Main) {
                val view = ImageView(context)
                val attachment = Shared.MessageAttachment.newBuilder().setFileId(fileId)
                    .setPreviewUrl("https://unused.invalid/preview").setType(Shared.MessageAttachmentType.IMAGE).build()
                assertTrue(ImageLoadHelper.loadCached(view, attachment))
                assertEquals(Color.BLUE, view.drawable.toBitmap(16, 16).getPixel(8, 8))
            }
        } finally { loader.memoryCache!!.remove(key) }
    }
}
