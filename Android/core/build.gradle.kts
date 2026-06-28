plugins {
    alias(libs.plugins.android.library)
    alias(libs.plugins.kotlin.android)
    id("com.google.protobuf") version "0.9.4"
}

android {
    namespace = "com.barkfluff.client.core"
    compileSdk = 36

    defaultConfig {
        // Берём минимальный из двух приложений (V1 = 31), чтобы ядро годилось обоим.
        minSdk = 31
        consumerProguardFiles("consumer-rules.pro")
    }

    compileOptions {
        // libsignal-android 0.86+ использует Java records (Java 16+) → 17 + desugaring.
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
        isCoreLibraryDesugaringEnabled = true
    }
    kotlinOptions {
        jvmTarget = "17"
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

    // gRPC — типы stub'ов и proto-сообщений присутствуют в публичном API ядра → api(...)
    api("io.grpc:grpc-okhttp:1.60.0")
    api("io.grpc:grpc-protobuf-lite:1.60.0")
    api("io.grpc:grpc-stub:1.60.0")
    api("io.grpc:grpc-kotlin-stub:1.4.1")
    api("com.google.protobuf:protobuf-javalite:3.25.1")

    // Coroutines — Flow/SharedFlow в публичном API RealtimeService → api(...)
    api("org.jetbrains.kotlinx:kotlinx-coroutines-core:1.7.3")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.7.3")

    // gRPC Kotlin stub codegen
    compileOnly("org.apache.tomcat:annotations-api:6.0.53")

    // Зашифрованное хранилище токенов (GlobalParam)
    implementation("androidx.security:security-crypto:1.1.0-alpha06")

    // E2E: Signal Double Ratchet (секретные чаты) + Argon2id (приватные чаты).
    // libsignal — api(): app-слой (E2EBootstrap) напрямую конструирует IdentityKeyPair/PreKeyRecord.
    api(libs.libsignal.android)
    implementation(libs.argon2kt)

    coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")
}
