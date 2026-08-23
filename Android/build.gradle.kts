// Корневой build-файл единой сборки BarkFluff (core + app-v1).
plugins {
    id("com.google.gms.google-services") version "4.4.4" apply false
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.android.library) apply false
    alias(libs.plugins.kotlin.android) apply false
    alias(libs.plugins.ksp) apply false
    alias(libs.plugins.hilt) apply false
}
