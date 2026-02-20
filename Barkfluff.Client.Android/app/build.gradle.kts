plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    id("com.google.protobuf") version "0.9.4"
}

android {
    namespace = "com.barkfluff.client"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.barkfluff.client"
        minSdk = 31
        targetSdk = 36
        versionCode = 1
        versionName = "1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
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
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
    kotlinOptions {
        jvmTarget = "11"
    }
    buildFeatures {
        viewBinding = true
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
    implementation(libs.androidx.constraintlayout)
    implementation("androidx.cardview:cardview:1.0.0")
    implementation(libs.androidx.recyclerview)
    implementation(libs.androidx.fragment)

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
}
