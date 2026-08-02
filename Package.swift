// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "NoticeSender",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "NoticeSender", targets: ["NoticeSender"])
    ],
    dependencies: [
        .package(url: "https://github.com/CoreOffice/CoreXLSX.git", exact: "0.14.2")
    ],
    targets: [
        .target(
            name: "KmsgSafeCore",
            path: "Vendor/kmsg/Sources/kmsg",
            exclude: [
                "Auth",
                "Commands",
                "kmsg.swift",
                "KakaoTalk/MessageContextResolver.swift",
                "KakaoTalk/TranscriptReader.swift",
            ]
        ),
        .executableTarget(
            name: "NoticeSender",
            dependencies: [
                "KmsgSafeCore",
                .product(name: "CoreXLSX", package: "CoreXLSX"),
            ],
            path: "Sources/NoticeSender"
        )
    ]
)
