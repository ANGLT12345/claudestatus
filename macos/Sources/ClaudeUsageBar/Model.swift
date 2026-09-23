import AppKit
import SwiftUI

/// Everything the menu bar item and popover need to draw themselves.
@MainActor
final class Model: ObservableObject {
    @Published var provider: Provider = .current
    @Published var data: UsageSnapshot?
    @Published var plan: String?
    @Published var status: FetchStatus = .none
    @Published var message: String?
    @Published var nextAttempt: Date?
    @Published var fetching = false
    @Published var cliInstalled = true
    /// Ticks forward so countdowns and "updated x min ago" stay current.
    @Published var now = Date()

    let pollInterval: TimeInterval = 5 * 60

    var needsSetup: Bool { status == .noCredentials || status == .unauthorized || status == .keychainDenied }
}

/// Menu bar sizes, largest first. macOS can't tell us how much menu bar space is free, so the user picks.
enum StripSize: String, CaseIterable {
    case full, compact, mini, micro

    var title: String {
        switch self {
        case .full: return "Full"
        case .compact: return "Compact"
        case .mini: return "Mini"
        case .micro: return "Micro"
        }
    }

    static var current: StripSize {
        get { UserDefaults.standard.string(forKey: "stripSize").flatMap(StripSize.init(rawValue:)) ?? .compact }
        set { UserDefaults.standard.set(newValue.rawValue, forKey: "stripSize") }
    }
}

/// Status colours: green, then amber at 70%, then red at 90%.
enum Status {
    static func color(_ pct: Double, dark: Bool) -> NSColor {
        if pct >= 90 { return dark ? NSColor(srgbRed: 0.95, green: 0.35, blue: 0.35, alpha: 1) : NSColor(srgbRed: 0.86, green: 0.15, blue: 0.15, alpha: 1) }
        if pct >= 70 { return dark ? NSColor(srgbRed: 0.96, green: 0.69, blue: 0.24, alpha: 1) : NSColor(srgbRed: 0.85, green: 0.47, blue: 0.02, alpha: 1) }
        return dark ? NSColor(srgbRed: 0.20, green: 0.82, blue: 0.47, alpha: 1) : NSColor(srgbRed: 0.09, green: 0.64, blue: 0.29, alpha: 1)
    }

    static let claude = NSColor(srgbRed: 0.85, green: 0.47, blue: 0.34, alpha: 1)
    static let chatgpt = NSColor(srgbRed: 0.06, green: 0.64, blue: 0.50, alpha: 1)
    static let gemini = NSColor(srgbRed: 0.26, green: 0.52, blue: 0.96, alpha: 1)

    /// The provider's brand colour, for buttons, badges and step numbers.
    static func accent(_ p: Provider) -> NSColor {
        switch p {
        case .claude: return claude
        case .chatgpt: return chatgpt
        case .gemini: return gemini
        }
    }
}
