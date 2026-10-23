import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    alias(libs.plugins.android.test)
    alias(libs.plugins.kotlin.android)
}

android {
    namespace = "com.barkfluff.benchmark"
    compileSdk = 36

    defaultConfig {
        minSdk = 31
        targetSdk = 36
        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        missingDimensionStrategy("channel", "nightly")
    }

    targetProjectPath = ":app-v1"
    experimentalProperties["android.experimental.self-instrumenting"] = true

    buildTypes {
        create("benchmark") {
            isDebuggable = true
            signingConfig = signingConfigs.getByName("debug")
            matchingFallbacks += listOf("release")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    compilerOptions {
        jvmTarget = JvmTarget.JVM_17
    }
}

dependencies {
    implementation(libs.androidx.benchmark.macro.junit4)
    implementation(libs.androidx.test.uiautomator)
    implementation(libs.androidx.junit)
}

tasks.configureEach {
    if (name.startsWith("connected") || name.startsWith("install") || name.startsWith("uninstall")) {
        doFirst {
            check(!System.getenv("ANDROID_SERIAL").isNullOrBlank()) {
                "Refusing to run $name without ANDROID_SERIAL. Use a verified benchmark device."
            }
        }
    }
}
