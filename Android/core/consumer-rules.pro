# =============================================================================
# :core — consumer R8 keep-правила (применяются в каждом app, который зависит от core)
# Только то, что специфично для зависимостей ядра: protobuf-lite, gRPC, libsignal,
# argon2, coroutines, okhttp/okio. UI-зависимости (firebase, camerax, mlkit, ucrop,
# photoview, media3, coil) держатся в proguard-rules.pro конкретного app.
# =============================================================================

-keepattributes Signature,*Annotation*,EnclosingMethod,InnerClasses,SourceFile,LineNumberTable
-keepattributes RuntimeVisibleAnnotations,RuntimeVisibleTypeAnnotations,RuntimeVisibleParameterAnnotations
-keepattributes RuntimeInvisibleAnnotations,RuntimeInvisibleTypeAnnotations,RuntimeInvisibleParameterAnnotations

# --- @Keep ---
-keep,allowobfuscation @interface androidx.annotation.Keep
-keep @androidx.annotation.Keep class * { *; }
-keepclassmembers class * {
    @androidx.annotation.Keep *;
}

# =============================================================================
# Protobuf Lite
# =============================================================================
-keep class com.google.protobuf.** { *; }
-keepclassmembers class * extends com.google.protobuf.GeneratedMessageLite {
    <fields>;
    <methods>;
}
-keep class * extends com.google.protobuf.GeneratedMessageLite { *; }
-keep class * extends com.google.protobuf.GeneratedMessageLite$Builder { *; }
-keep class * implements com.google.protobuf.MessageLite { *; }
-keep class * implements com.google.protobuf.MessageLiteOrBuilder { *; }
-keepclassmembers class * implements com.google.protobuf.MessageLiteOrBuilder { *; }
-keep class com.google.protobuf.Internal* { *; }
-keep class com.google.protobuf.RawMessageInfo { *; }
-dontwarn com.google.protobuf.**

# Сгенерированные proto-классы
-keep class barkfluff.** { *; }
-keepclassmembernames class barkfluff.** { *; }
-keep class google.protobuf.** { *; }
-keepclassmembernames class google.protobuf.** { *; }

# =============================================================================
# gRPC (java + kotlin stubs)
# =============================================================================
-keep class io.grpc.** { *; }
-keep class * extends io.grpc.stub.AbstractStub { *; }
-keep class * extends io.grpc.kotlin.AbstractCoroutineStub { *; }
-keep class io.grpc.kotlin.** { *; }
-dontwarn io.grpc.**
-dontwarn javax.annotation.**

# OkHttp + Okio (grpc-okhttp transport)
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn org.codehaus.mojo.animal_sniffer.**
-dontwarn org.conscrypt.**

# =============================================================================
# libsignal (секретные чаты)
# =============================================================================
-keep class org.signal.libsignal.** { *; }
-keepclassmembers class org.signal.libsignal.** {
    native <methods>;
}
-dontwarn org.signal.libsignal.**

# =============================================================================
# Argon2kt (приватные чаты)
# =============================================================================
-keep class com.lambdapioneer.argon2kt.** { *; }
-keepclassmembers class com.lambdapioneer.argon2kt.** {
    native <methods>;
}

# =============================================================================
# Kotlin Coroutines
# =============================================================================
-dontwarn kotlinx.coroutines.**
-keepclassmembernames class kotlinx.** {
    volatile <fields>;
}
-keep class kotlinx.coroutines.debug.** { *; }

# =============================================================================
# Код ядра
# =============================================================================
-keep class com.barkfluff.client.** { *; }
