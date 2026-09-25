import AppKit

// `--self-test …` runs one CI check without any UI and exits (see SelfTest.swift).
let arguments = CommandLine.arguments
if let i = arguments.firstIndex(of: "--self-test") {
    SelfTest.run(Array(arguments[(i + 1)...]))
}
LaunchOptions.current = LaunchOptions(arguments)
FluentApp.main()
