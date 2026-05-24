plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.compose)
}

android {
    namespace = "com.barkfluff.clientv2"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.barkfluff.clientv2"
        minSdk = 35
        targetSdk = 36
        versionCode = 1
        versionName = "1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"

        ndk {
            // libsignal native (через :core) — только arm64-v8a.
            abiFilters += "arm64-v8a"
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }
    compileOptions {
        // libsignal-android 0.86+ (через :core) требует Java 17 + desugaring.
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
        isCoreLibraryDesugaringEnabled = true
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        compose = true
    }
    packaging {
        resources {
            excludes += setOf(
                "libsignal_jni*.dylib",
                "libsignal_jni*.so",
                "signal_jni*.dll",
                "libsignal_jni_testing*.so",
                "libsignal_jni_testing*.dylib",
                "signal_jni_testing*.dll",
                "/META-INF/{AL2.0,LGPL2.1}"
            )
        }
        jniLibs {
            excludes += "**/libsignal_jni_testing.so"
        }
    }
}

dependencies {
    // Общий не-UI слой (gRPC, репозитории, крипто, proto, хранилище).
    implementation(project(":core"))

    implementation(libs.androidx.core.ktx)
    // MDC material — только для XML launch-темы (Theme.Material3.*); тянет appcompat транзитивно.
    implementation(libs.material)

    // Jetpack Compose (Material 3 Expressive)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.ui)
    implementation(libs.androidx.ui.graphics)
    implementation(libs.androidx.ui.tooling.preview)
    implementation(libs.androidx.material3)
    implementation(libs.androidx.compose.material.icons.core)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.navigation.compose)
    implementation(libs.coil.compose)

    debugImplementation(libs.androidx.ui.tooling)

    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")

    testImplementation(libs.junit)
    androidTestImplementation(libs.androidx.espresso.core)
    androidTestImplementation(libs.androidx.junit)
}
