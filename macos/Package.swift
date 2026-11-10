// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "HaiView",
    platforms: [.macOS(.v15)],
    targets: [
        .executableTarget(name: "HaiView", path: "Sources",
                          swiftSettings: [.swiftLanguageMode(.v5)])
    ]
)
