// swift-tools-version: 6.0
// Fluent for Mac.
//
//   FluentCore    the shared Swift from the iPhone app (Gemini client, WritingStyle formatter, WAV,
//                 settings, history), copied here so the iOS copy is never touched.
//   FluentMacKit  Mac rules with no UI: text-field detection, insertion spacing, per-app style
//                 categories, shortcuts, bubble placement and Mac settings. Builds on Linux too, so
//                 its tests run without a Mac.
//   Fluent        the app itself (AppKit + SwiftUI). macOS only.
//   fluent-cli    a small command-line tool that sends a WAV to Gemini through FluentCore; CI
//                 uses it against a mock server and, when a key is configured, against Gemini.
import PackageDescription

var targets: [Target] = [
    .target(name: "FluentCore", path: "FluentCore/Sources/FluentCore"),
    .testTarget(name: "FluentCoreTests", dependencies: ["FluentCore"], path: "FluentCore/Tests/FluentCoreTests"),
    .target(name: "FluentMacKit", dependencies: ["FluentCore"], path: "Sources/FluentMacKit"),
    .testTarget(name: "FluentMacKitTests", dependencies: ["FluentMacKit", "FluentCore"], path: "Tests/FluentMacKitTests"),
    .executableTarget(name: "fluent-cli", dependencies: ["FluentCore", "FluentMacKit"], path: "Sources/fluent-cli"),
]
var products: [Product] = [.executable(name: "fluent-cli", targets: ["fluent-cli"])]

#if os(macOS)
targets.append(.executableTarget(
    name: "Fluent",
    dependencies: ["FluentCore", "FluentMacKit"],
    path: "Sources/Fluent",
    linkerSettings: [
        .linkedFramework("AppKit"), .linkedFramework("ApplicationServices"),
        .linkedFramework("AVFoundation"), .linkedFramework("Carbon"),
        .linkedFramework("ServiceManagement"),
    ]
))
products.append(.executable(name: "Fluent", targets: ["Fluent"]))
// Test-only: a text box that refuses Accessibility writes, for the paste-fallback test in CI.
targets.append(.executableTarget(name: "PasteOnlyHost", path: "Sources/PasteOnlyHost"))
#endif

let package = Package(
    name: "FluentMac",
    platforms: [.macOS(.v14)],
    products: products,
    targets: targets,
    swiftLanguageModes: [.v5]
)
