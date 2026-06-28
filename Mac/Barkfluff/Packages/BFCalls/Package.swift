// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "BFCalls",
    platforms: [.macOS(.v26), .iOS(.v26)],
    products: [
        .library(name: "BFCalls", targets: ["BFCalls"]),
    ],
    dependencies: [
        .package(path: "../BFNetworking"),
        .package(url: "https://github.com/livekit/client-sdk-swift.git", from: "2.9.0"),
    ],
    targets: [
        .target(
            name: "BFCalls",
            dependencies: [
                "BFNetworking",
                .product(name: "LiveKit", package: "client-sdk-swift"),
            ]
        ),
    ]
)
