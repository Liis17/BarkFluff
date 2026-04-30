// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "BFProto",
    platforms: [.macOS(.v26), .iOS(.v26)],
    products: [
        .library(name: "BFProto", targets: ["BFProto"]),
    ],
    dependencies: [
        .package(url: "https://github.com/grpc/grpc-swift-2.git", from: "2.3.0"),
        .package(url: "https://github.com/grpc/grpc-swift-protobuf.git", from: "2.0.0"),
        .package(url: "https://github.com/apple/swift-protobuf.git", from: "1.28.0"),
    ],
    targets: [
        .target(
            name: "BFProto",
            dependencies: [
                .product(name: "GRPCProtobuf", package: "grpc-swift-protobuf"),
                .product(name: "SwiftProtobuf", package: "swift-protobuf"),
            ]
        ),
    ]
)
