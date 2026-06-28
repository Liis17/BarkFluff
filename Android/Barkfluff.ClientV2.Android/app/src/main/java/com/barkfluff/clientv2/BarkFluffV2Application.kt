package com.barkfluff.clientv2

import android.app.Application
import coil.ImageLoader
import coil.ImageLoaderFactory
import coil.decode.ImageDecoderDecoder
import com.barkfluff.clientv2.di.AppContainer

/**
 * Application V2. Создаёт ручной DI-контейнер ([AppContainer]) с не-UI слоем из :core.
 * Реализует [ImageLoaderFactory], чтобы Coil анимировал GIF/WebP во всём приложении
 * (minSdk 35 ≥ API 28 → [ImageDecoderDecoder]).
 */
class BarkFluffV2Application : Application(), ImageLoaderFactory {

    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        container = AppContainer(this)
    }

    override fun newImageLoader(): ImageLoader =
        ImageLoader.Builder(this)
            .components { add(ImageDecoderDecoder.Factory()) }
            .build()
}
