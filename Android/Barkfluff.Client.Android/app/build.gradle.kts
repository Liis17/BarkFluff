import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    id("com.google.protobuf") version "0.9.4"
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
            abiFilters += listOf("arm64-v8a", "armeabi-v7a")
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
        debug {
            ndk {
                abiFilters.clear()
                abiFilters += "arm64-v8a"
            }
        }
        release {
            isMinifyEnabled = false
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
        compose = true
    }
    composeOptions {
        kotlinCompilerExtensionVersion = "1.5.14"
    }
    packaging {
        resources {
            // libsignal-client desktop JAR кладёт desktop-нативки в корень JAR;
            // даже если зависимость случайно транзитивно протечёт — выпиливаем их из APK.
            excludes += setOf(
                "libsignal_jni*.dylib",
                "libsignal_jni*.so",
                "signal_jni*.dll",
                "libsignal_jni_testing*.so",
                "libsignal_jni_testing*.dylib",
                "signal_jni_testing*.dll"
            )
        }
    }
}

protobuf {
    protoc {
        artifact = "com.google.protobuf:protoc:3.25.1"
    }
    plugins {
        create("grpc") {
            artifact = "io.grpc:protoc-gen-grpc-java:1.60.0"
        }
        create("grpckt") {
            artifact = "io.grpc:protoc-gen-grpc-kotlin:1.4.1:jdk8@jar"
        }
    }
    generateProtoTasks {
        all().forEach { task ->
            task.builtins {
                create("java") {
                    option("lite")
                }
            }
            task.plugins {
                create("grpc") {
                    option("lite")
                }
                create("grpckt") {
                    option("lite")
                }
            }
        }
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.appcompat)
    implementation(libs.material)
    implementation("androidx.constraintlayout:constraintlayout:2.2.1")
    implementation("androidx.cardview:cardview:1.0.0")
    implementation(libs.androidx.recyclerview)
    implementation(libs.androidx.fragment)

    // Jetpack Compose for Material 3 Expressive
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.compose.material3:material3-window-size-class")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("androidx.lifecycle:lifecycle-process:2.8.7")
    implementation("androidx.navigation:navigation-compose:2.8.5")

    // gRPC dependencies
    implementation("io.grpc:grpc-okhttp:1.60.0")
    implementation("io.grpc:grpc-protobuf-lite:1.60.0")
    implementation("io.grpc:grpc-stub:1.60.0")
    implementation("io.grpc:grpc-kotlin-stub:1.4.1")
    implementation("com.google.protobuf:protobuf-javalite:3.25.1")

    // Coroutines for gRPC
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.7.3")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.7.3")

    // Encrypted storage for tokens
    implementation("androidx.security:security-crypto:1.1.0-alpha06")

    // Required for gRPC Kotlin stub
    compileOnly("org.apache.tomcat:annotations-api:6.0.53")

    // Image cropping for avatar
    implementation("com.github.yalantis:ucrop:2.2.8")

    // Image loading and caching
    implementation("io.coil-kt:coil:2.7.0")
    implementation("io.coil-kt:coil-compose:2.7.0")
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

    // ML Kit Barcode Scanning
    implementation("com.google.mlkit:barcode-scanning:17.3.0")

    //firebase
    implementation(platform("com.google.firebase:firebase-bom:34.10.0"))
    implementation("com.google.firebase:firebase-analytics")
    implementation("com.google.firebase:firebase-messaging")

    // E2E-шифрование: Signal Double Ratchet (секретные чаты) + Argon2id (приватные чаты).
    // ВНИМАНИЕ: libsignal-android уже включает libsignal-client транзитивно с правильными
    // Android-нативками. Прямое подключение libsignal-client (desktop JAR) тащит ~280 МБ
    // ненужных native libs (Linux x86_64, macOS, Windows) — НЕ добавлять.
    implementation(libs.libsignal.android)
    implementation(libs.argon2kt)
    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")
}
