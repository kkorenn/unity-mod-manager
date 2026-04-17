// swift-tools-version: 6.3
// The swift-tools-version declares the minimum version of Swift required to build this package.

import PackageDescription

let package = Package(
    name: "NativeMacUMM",
    platforms: [
        .macOS(.v13),
    ],
    targets: [
        .executableTarget(
            name: "NativeMacUMM"
        ),
        .testTarget(
            name: "NativeMacUMMTests",
            dependencies: ["NativeMacUMM"]
        ),
    ],
    swiftLanguageModes: [.v6]
)
