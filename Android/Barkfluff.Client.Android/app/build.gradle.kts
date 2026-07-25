import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.ksp)
    id("com.google.gms.google-services")
}

fun getSigningProp(envKey: String, propKey: String): String? {
    return System.getenv(envKey) ?: run {
        val f = rootProject.file("local.properties")
        if (f.exists()) {
            Properties().apply { load(f.inputStream()) }[propKey] as? String
        } else null
    }
}

/**
 * Копирует локализованные markdown-версии юридических документов из WebServer в assets/legal.
 * Источник — единственный: Backend/Barkfluff.WebServer/html/legal, тот же, что отдаёт сайт.
 * Пустой результат — ошибка сборки: APK без актуальных соглашений выпускать нельзя.
 */
abstract class CopyLegalDocsTask : DefaultTask() {

    @get:InputFiles
    @get:PathSensitive(PathSensitivity.RELATIVE)
    abstract val sourceFiles: ConfigurableFileCollection

    @get:Input
    abstract val sourceDescription: Property<String>

    @get:OutputDirectory
    abstract val outputDirectory: DirectoryProperty

    @TaskAction
    fun run() {
        val docs = sourceFiles.files.filter { it.isFile }
        if (docs.isEmpty()) {
            throw GradleException(
                "Не найдены legal-документы в ${sourceDescription.get()}. " +
                    "Сборка остановлена: APK не должен уходить без актуальных соглашений."
            )
        }

        val target = outputDirectory.get().asFile.resolve("legal")
        target.deleteRecursively()
        target.mkdirs()
        docs.forEach { it.copyTo(target.resolve(it.name), overwrite = true) }
        logger.lifecycle("legal: скопировано ${docs.size} документов в assets/legal")
    }
}

val legalSourceDir = rootProject.layout.projectDirectory.dir("../Backend/Barkfluff.WebServer/html/legal")

val copyLegalDocs = tasks.register<CopyLegalDocsTask>("copyLegalDocs") {
    description = "Копирует локализованные legal-markdown из WebServer в assets"
    sourceFiles.from(legalSourceDir.asFileTree.matching {
        include("TERMS_OF_SERVICE.*.md", "PRIVACY_POLICY.*.md")
    })
    sourceDescription.set(legalSourceDir.asFile.path)
    // outputDirectory назначает сам AGP через addGeneratedSourceDirectory ниже.
}

android {
    namespace = "com.barkfluff.client"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.barkfluff.client"
        minSdk = 31
        targetSdk = 36
        versionCode = 1
        versionName = "0.0.1"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"

        ndk {
            // Только arm64-v8a. minSdk = 31 (Android 12, 2021+) — все такие устройства уже 64-bit ARM.
            // armeabi-v7a добавил бы ~70 МБ libsignal_jni.so без какого-либо охвата реальных пользователей.
            abiFilters += "arm64-v8a"
        }
    }

    signingConfigs {
        create("release") {
            val storeFilePath = getSigningProp("RELEASE_STORE_FILE", "RELEASE_STORE_FILE")
            if (storeFilePath != null) {
                storeFile = file(storeFilePath)
                storePassword = getSigningProp("RELEASE_STORE_PASSWORD", "RELEASE_STORE_PASSWORD")
                keyAlias = getSigningProp("RELEASE_KEY_ALIAS", "RELEASE_KEY_ALIAS")
                keyPassword = getSigningProp("RELEASE_KEY_PASSWORD", "RELEASE_KEY_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            // R8 включён: shrink + optimize + obfuscate (см. proguard-rules.pro + core/consumer-rules.pro).
            isMinifyEnabled = true
            isShrinkResources = true
            signingConfig = signingConfigs.getByName("release")
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }
    compileOptions {
        // libsignal-android 0.86+ использует Java records (Java 16+) и требует
        // sourceCompatibility = 17 + core library desugaring.
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
        isCoreLibraryDesugaringEnabled = true
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        viewBinding = true
    }
    packaging {
        resources {
            // libsignal-client desktop JAR кладёт desktop-нативки в корень JAR; выпиливаем их из APK.
            excludes += setOf(
                "libsignal_jni*.dylib",
                "libsignal_jni*.so",
                "signal_jni*.dll",
                "libsignal_jni_testing*.so",
                "libsignal_jni_testing*.dylib",
                "signal_jni_testing*.dll"
            )
        }
        jniLibs {
            // libsignal-android.aar кладёт в jni/<abi>/ libsignal_jni_testing.so (~75 МБ/ABI) — не нужен в production.
            excludes += "**/libsignal_jni_testing.so"
        }
    }
}

androidComponents {
    onVariants { variant ->
        variant.sources.assets?.addGeneratedSourceDirectory(
            copyLegalDocs,
            CopyLegalDocsTask::outputDirectory
        )
    }
}

ksp {
    arg("room.schemaLocation", "$projectDir/schemas")
}

dependencies {
    // Общий не-UI слой (gRPC, репозитории, крипто, proto, хранилище). Транзитивно отдаёт
    // gRPC/protobuf/coroutines-core (api), а также libsignal/argon2 native в APK.
    implementation(project(":core"))

    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.appcompat)
    implementation(libs.material)
    implementation("androidx.constraintlayout:constraintlayout:2.2.1")
    implementation("androidx.cardview:cardview:1.0.0")
    implementation(libs.androidx.recyclerview)
    implementation(libs.androidx.fragment)


    // Offline-first chat cache: Room with SQLCipher encryption.
    implementation(libs.androidx.room.runtime)
    implementation(libs.androidx.room.ktx)
    ksp(libs.androidx.room.compiler)
    implementation(libs.androidx.sqlite)
    implementation("net.zetetic:sqlcipher-android:4.15.0@aar")
    implementation("androidx.security:security-crypto:1.1.0-alpha06")
    // ProcessLifecycleOwner — foreground/background tracking + RealtimeService resume/pause.
    implementation("androidx.lifecycle:lifecycle-process:2.8.7")

    // WorkManager — периодическое обновление App Widget'ов.
    implementation("androidx.work:work-runtime-ktx:2.9.1")

    // Coroutines for UI (Dispatchers.Main / lifecycleScope). coroutines-core приходит через :core api.
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.7.3")

    // Image cropping for avatar
    implementation("com.github.yalantis:ucrop:2.2.8")

    // Image loading and caching
    implementation("io.coil-kt:coil:2.7.0")
    implementation("io.coil-kt:coil-video:2.7.0")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")

    // Image viewer: pinch-to-zoom + swipe between images
    implementation("com.github.chrisbanes:PhotoView:2.3.0")
    implementation("androidx.viewpager2:viewpager2:1.1.0")

    // ExoPlayer for video/audio playback
    implementation("androidx.media3:media3-exoplayer:1.3.1")
    implementation("androidx.media3:media3-ui:1.3.1")

    // Media3 Transformer + Effects for video transcoding (480p compress + trim)
    implementation("androidx.media3:media3-transformer:1.3.1")
    implementation("androidx.media3:media3-effect:1.3.1")
    implementation("androidx.media3:media3-common:1.3.1")

    // CameraX
    implementation("androidx.camera:camera-core:1.4.2")
    implementation("androidx.camera:camera-camera2:1.4.2")
    implementation("androidx.camera:camera-lifecycle:1.4.2")
    implementation("androidx.camera:camera-view:1.4.2")

    // LiveKit media engine for calls.
    implementation("io.livekit:livekit-android:2.26.0")
    implementation("io.livekit:livekit-android-camerax:2.26.0")

    // ML Kit Barcode Scanning
    implementation("com.google.mlkit:barcode-scanning:17.3.0")

    // firebase
    implementation(platform("com.google.firebase:firebase-bom:34.10.0"))
    implementation("com.google.firebase:firebase-analytics")
    implementation("com.google.firebase:firebase-messaging")

    // androidx.dynamicanimation — spring physics (SpringAnimation) для M3 Expressive motion (SpringPress).
    implementation(libs.androidx.dynamic.animation)

    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")
}
