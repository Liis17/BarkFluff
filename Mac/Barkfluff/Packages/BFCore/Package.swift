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
        .package(url: "https://github.com/groue/GRDB.swift.git", from: "7.0.0"),
    ],
    targets: [
        .target(
            name: "BFCore",
            dependencies: [
                "BFNetworking",
                .product(name: "GRDB", package: "GRDB.swift"),
            ]
        ),
    ]
)
