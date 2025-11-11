// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "FITSwiftSDK",
    platforms: [
        .macOS(.v12),
        .iOS(.v14)
    ],
    products: [
        .library(
            name: "FITSwiftSDK",
            targets: ["FITSwiftSDK"]),
    ],
    targets: [
        .target(
            name: "FITSwiftSDK"),
        .testTarget(
            name: "FITSwiftSDKTests",
            dependencies: ["FITSwiftSDK"]),
    ]
)

