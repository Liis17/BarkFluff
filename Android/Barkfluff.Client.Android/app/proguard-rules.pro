# =============================================================================
# BarkFluff Client — ProGuard / R8 keep-правила
# =============================================================================
# Базовые правила (Kotlin metadata, native methods, Parcelable CREATOR, enum
# values()/valueOf()) уже подключены через proguard-android-optimize.txt.
# Здесь — только то, что специфично для нашего набора зависимостей.

# --- Стектрейсы остаются читаемыми после применения mapping.txt ---------------
# RuntimeVisibleAnnotations / RuntimeVisibleParameterAnnotations критичны для Kotlin
# metadata и для grpc-kotlin/coroutines рефлексии.
-keepattributes Signature,*Annotation*,EnclosingMethod,InnerClasses,SourceFile,LineNumberTable
-keepattributes RuntimeVisibleAnnotations,RuntimeVisibleTypeAnnotations,RuntimeVisibleParameterAnnotations
-keepattributes RuntimeInvisibleAnnotations,RuntimeInvisibleTypeAnnotations,RuntimeInvisibleParameterAnnotations
-renamesourcefileattribute SourceFile

# --- @Keep аннотация ---------------------------------------------------------
-keep,allowobfuscation @interface androidx.annotation.Keep
-keep @androidx.annotation.Keep class * { *; }
-keepclassmembers class * {
    @androidx.annotation.Keep *;
}

# =============================================================================
# Protobuf Lite — наша основная сериализация по gRPC
# =============================================================================
# protobuf-lite вызывает dynamicMethod(...) рефлексией; имена/поля сгенерированных
# классов должны сохраняться, иначе сериализация ломается на ровном месте.
-keep class com.google.protobuf.** { *; }
-keepclassmembers class * extends com.google.protobuf.GeneratedMessageLite {
    <fields>;
    <methods>;
}
-keep class * extends com.google.protobuf.GeneratedMessageLite { *; }
-keep class * extends com.google.protobuf.GeneratedMessageLite$Builder { *; }
# MessageLite/MessageLiteOrBuilder и Internal типы (RawMessageInfo, FieldInfo) —
# к ним обращается сгенерированный switch-статус в dynamicMethod().
-keep class * implements com.google.protobuf.MessageLite { *; }
-keep class * implements com.google.protobuf.MessageLiteOrBuilder { *; }
-keepclassmembers class * implements com.google.protobuf.MessageLiteOrBuilder { *; }
-keep class com.google.protobuf.Internal* { *; }
-keep class com.google.protobuf.RawMessageInfo { *; }
-dontwarn com.google.protobuf.**

# Все сгенерированные нами proto-классы — barkfluff.* и google.protobuf.*.
# Без `-keepclassmembernames` имена приватных полей `*_` могут переименоваться,
# а switch внутри dynamicMethod() ссылается на них через RawMessageInfo.objects[].
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

# OkHttp + Okio (используются grpc-okhttp + Coil)
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn org.codehaus.mojo.animal_sniffer.**
-dontwarn org.conscrypt.**

# =============================================================================
# libsignal (Signal Protocol — секретные чаты)
# =============================================================================
# Нативный Rust-код вызывает Java-методы по имени: класс/метод нельзя
# переименовывать. Также есть @CalledFromNative и подобные аннотации.
-keep class org.signal.libsignal.** { *; }
-keepclassmembers class org.signal.libsignal.** {
    native <methods>;
}
-dontwarn org.signal.libsignal.**

# =============================================================================
# Argon2kt (приватные чаты — passphrase KDF)
# =============================================================================
-keep class com.lambdapioneer.argon2kt.** { *; }
-keepclassmembers class com.lambdapioneer.argon2kt.** {
    native <methods>;
}

# =============================================================================
# Firebase (Analytics + Messaging) + Google Services
# =============================================================================
# FirebaseMessagingService — наследник держится по записи в манифесте,
# но добавим явно на случай рефлексии.
-keep class * extends com.google.firebase.messaging.FirebaseMessagingService { *; }
-keep class com.google.firebase.** { *; }
-keep class com.google.android.gms.** { *; }
-dontwarn com.google.firebase.**
-dontwarn com.google.android.gms.**

# =============================================================================
# Media3 (ExoPlayer + Transformer + Effect)
# =============================================================================
# ExoPlayer / Transformer тащит много рефлексии для extractor-ов и кодеков,
# часть консьюмерских правил уже в AAR. Подстрахуемся.
-keep class androidx.media3.** { *; }
-dontwarn androidx.media3.**

# =============================================================================
# CameraX + ML Kit Barcode
# =============================================================================
-keep class androidx.camera.** { *; }
-dontwarn androidx.camera.**
-keep class com.google.mlkit.** { *; }
-keep class com.google.android.gms.vision.** { *; }
-dontwarn com.google.mlkit.**

# =============================================================================
# UCrop (yalantis)
# =============================================================================
-keep class com.yalantis.ucrop.** { *; }
-dontwarn com.yalantis.ucrop.**

# =============================================================================
# PhotoView — без правил, но dontwarn для безопасности
# =============================================================================
-dontwarn com.github.chrisbanes.photoview.**

# =============================================================================
# Coil — содержит свои consumer-rules, но decoders дёргаются через ServiceLoader
# =============================================================================
-keep class coil.** { *; }
-dontwarn coil.**

# =============================================================================
# Kotlin Coroutines — обычно ок, но добавим dontwarn
# =============================================================================
-dontwarn kotlinx.coroutines.**
-keepclassmembernames class kotlinx.** {
    volatile <fields>;
}
# Coroutines DebugProbes
-keep class kotlinx.coroutines.debug.** { *; }

# =============================================================================
# =============================================================================
# SQLCipher — JNI ищет классы и native methods по имени
# =============================================================================
-keep,includedescriptorclasses class net.zetetic.database.sqlcipher.** { *; }
-keep,includedescriptorclasses interface net.zetetic.database.sqlcipher.** { *; }
-dontwarn net.zetetic.database.sqlcipher.**
# Наш код — оставляем всё под com.barkfluff.client (минимизация даст слишком
# мало по сравнению с рисками; основная экономия от R8 идёт по библиотекам).
# Если позже захотим обфусцировать собственный код — снять это правило.
# =============================================================================
-keep class com.barkfluff.client.** { *; }

# Активити/сервисы/ресиверы из манифеста — AGP сохраняет автоматически.

# =============================================================================
# Логирование — вырезаем всё, кроме Log.e, из release-сборки
# =============================================================================
# В логи попадали текст сообщений, content:// URI, presigned-URL и фрагменты
# FCM-токенов. logcat читается любым приложением с READ_LOGS и попадает в
# bugreport'ы, поэтому в production такие вызовы не должны существовать вообще.
#
# -assumenosideeffects действует на весь merged-DEX приложения, поэтому одного
# объявления здесь достаточно и для кода :core — дублировать правило в
# core/consumer-rules.pro НЕЛЬЗЯ: оттуда оно протекло бы и в app-v2.
#
# Log.e оставлен намеренно: диагностика прод-крашей. Его аргументы вычищены
# от PII вручную по месту вызова.
-assumenosideeffects class android.util.Log {
    public static int v(...);
    public static int d(...);
    public static int i(...);
    public static int w(...);
    public static int println(...);
}

# =============================================================================
# WebView с JS (если когда-нибудь добавится) — заглушка, сейчас не используется.
# =============================================================================
# -keepclassmembers class fqcn.of.javascript.interface.for.webview {
#    public *;
# }
