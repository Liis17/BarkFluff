# Add project specific ProGuard rules here.
# Keep gRPC and Protobuf classes
-keep class io.grpc.** { *; }
-keep class com.google.protobuf.** { *; }
-dontwarn com.google.protobuf.**
-dontwarn io.grpc.**

# Keep all proto generated classes
-keep class com.barkfluff.messenger.data.grpc.** { *; }
