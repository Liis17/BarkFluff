// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "BFCore",
    platforms: [.macOS(.v26), .iOS(.v26)],
    products: [
        .library(name: "BFCore", targets: ["BFCore"]),
    ],
    dependencies: [
        .package(path: "../BFNetworking"),
    ],
    targets: [
        .target(
            name: "BFCore",
            dependencies: ["BFNetworking"]
        ),
    ]
)
