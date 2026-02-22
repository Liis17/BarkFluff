// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "BFNetworking",
    platforms: [.macOS(.v26), .iOS(.v26)],
    products: [
        .library(name: "BFNetworking", targets: ["BFNetworking"]),
    ],
    dependencies: [
        .package(path: "../BFProto"),
        .package(url: "https://github.com/grpc/grpc-swift.git", from: "2.0.0"),
        .package(url: "https://github.com/grpc/grpc-swift-nio-transport.git", from: "1.0.0"),
        .package(url: "https://github.com/grpc/grpc-swift-protobuf.git", from: "1.0.0"),
        .package(url: "https://github.com/kishikawakatsumi/KeychainAccess.git", from: "4.2.2"),
    ],
    targets: [
        .target(
            name: "BFNetworking",
            dependencies: [
                "BFProto",
                .product(name: "GRPCCore", package: "grpc-swift"),
                .product(name: "GRPCNIOTransportHTTP2", package: "grpc-swift-nio-transport"),
                .product(name: "GRPCProtobuf", package: "grpc-swift-protobuf"),
                "KeychainAccess",
            ]
        ),
    ]
)
