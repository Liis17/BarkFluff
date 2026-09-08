// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "BFMarkdown",
    platforms: [.macOS(.v26), .iOS(.v26)],
    products: [
        .library(name: "BFMarkdown", targets: ["BFMarkdown"]),
    ],
    dependencies: [
        .package(path: "../BFCore"),
    ],
    targets: [
        .target(
            name: "BFMarkdown",
            dependencies: ["BFCore"]
        ),
    ]
)
