import AppKit
import Combine
import ServiceManagement
import SwiftUI

@main
enum Main {
    @MainActor
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory) // menu bar only, no Dock icon
        withExtendedLifetime(delegate) { app.run() }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private let model = Model()
    private var client = UsageClient(provider: .current)
    private var statusItem: NSStatusItem!
    private let popover = NSPopover()
    private var timer: Timer?
    private var nextPoll = Date()
    private var lastAttempt = Date.distantPast
    private var failures = 0
    private var lastMinute = -1
    private var bag = Set<AnyCancellable>()
    private var appearanceObservation: NSKeyValueObservation?

    func applicationDidFinishLaunching(_ notification: Notification) {
        loadProvider()
        LoginItem.applyDefault()

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            button.target = self
            button.action = #selector(statusItemClicked(_:))
            button.sendAction(on: [.leftMouseUp, .rightMouseUp])
            button.imagePosition = .imageOnly
            button.setAccessibilityLabel("\(model.provider.name) usage")
            appearanceObservation = button.observe(\.effectiveAppearance) { [weak self] _, _ in
                Task { @MainActor in self?.render() }
            }
        }

        popover.behavior = .transient
        popover.animates = true
        let host = NSHostingController(rootView: PopupView(model: model, actions: PopupActions(
            refresh: { [weak self] in self?.refresh(manual: true) },
            setup: { [weak self] in self?.openSetup() }
        )))
        host.sizingOptions = .preferredContentSize
        popover.contentViewController = host

        model.objectWillChange
            .receive(on: RunLoop.main)
            .sink { [weak self] _ in self?.render() }
            .store(in: &bag)

        timer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        render()
        tick()
    }

    // MARK: Provider

    /// Resets the model to the current provider, starting from its last cached numbers.
    private func loadProvider() {
        let cached = Cache.load(client.provider)
        model.provider = client.provider
        model.data = cached?.data
        model.plan = cached?.plan
        model.status = .none
        model.message = nil
        model.nextAttempt = nil
        model.fetching = false
        model.cliInstalled = true
        failures = 0
        nextPoll = Date()
        if let cached, Date().timeIntervalSince(cached.data.fetchedAt) < model.pollInterval {
            nextPoll = cached.data.fetchedAt.addingTimeInterval(model.pollInterval)
        }
    }

    private func switchProvider(_ provider: Provider) {
        guard provider != client.provider else { return }
        Provider.current = provider
        client = UsageClient(provider: provider)
        loadProvider()
        statusItem.button?.setAccessibilityLabel("\(provider.name) usage")
        render()
        tick()
    }

    // MARK: Polling

    private func tick() {
        let now = Date()
        // A denied Keychain prompt isn't retried automatically — that would nag; the user retries from the popover.
        if now >= nextPoll && !model.fetching && model.status != .keychainDenied {
            refresh(manual: false)
        }
        let minute = Calendar.current.component(.minute, from: now)
        if minute != lastMinute || popover.isShown {
            lastMinute = minute
            model.now = now
        }
    }

    private func refresh(manual: Bool) {
        let now = Date()
        if model.fetching { return }
        if manual && now.timeIntervalSince(lastAttempt) < 15 { return }
        lastAttempt = now
        model.fetching = true
        if model.status == .keychainDenied { client.reset() }

        let current = client
        Task { @MainActor in
            let r = await current.fetch()
            // The provider was switched while this was in flight: its answer is for the other one.
            guard current === self.client else { return }
            let now = Date()
            model.fetching = false
            model.status = r.status
            model.message = r.message
            if let p = r.plan { model.plan = p }

            switch r.status {
            case .ok:
                model.data = r.data
                failures = 0
                nextPoll = now.addingTimeInterval(model.pollInterval)
            case .rateLimited:
                failures += 1
                let wait = r.retryAfter ?? 300 * pow(2, Double(min(failures - 1, 2)))
                nextPoll = now.addingTimeInterval(min(max(wait + 5, 60), 1800))
            case .noCredentials, .unauthorized:
                // Only re-reads local credentials until they change, so checking often is cheap.
                model.cliInstalled = CLI.path(for: current.provider) != nil
                nextPoll = now.addingTimeInterval(10)
            case .keychainDenied:
                nextPoll = .distantFuture
            default:
                failures += 1
                nextPoll = now.addingTimeInterval(min(900, 60 * pow(2, Double(min(failures, 4)))))
            }
            model.nextAttempt = nextPoll
            model.now = now
        }
    }

    // MARK: Menu bar item

    private func render() {
        guard let button = statusItem?.button else { return }
        let dark = button.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
        let now = model.now
        let stale = model.data.map { model.status != .ok && model.status != .none && now.timeIntervalSince($0.fetchedAt) > 1200 } ?? false
        button.image = StripRenderer.image(
            size: StripSize.current, provider: model.provider, data: model.data, status: stripStatus(now: now), stale: stale, dark: dark,
            height: NSStatusBar.system.thickness, now: now)
        button.toolTip = model.data.map {
            "Session \(Int(($0.fiveHour?.percent ?? 0).rounded()))% · Weekly \(Int(($0.sevenDay?.percent ?? 0).rounded()))%"
        } ?? "\(model.provider.name) usage"
    }

    private func stripStatus(now: Date) -> String {
        if model.fetching || model.status == .none { return "Loading…" }
        switch model.status {
        case .rateLimited:
            if let n = model.nextAttempt, n > now { return "Retry \(Fmt.inTime(n.timeIntervalSince(now)))" }
            return "Retrying"
        case .noCredentials: return "Set up"
        case .unauthorized: return "Sign in"
        case .keychainDenied: return "Allow access"
        default: return "Offline"
        }
    }

    @objc private func statusItemClicked(_ sender: NSStatusBarButton) {
        let event = NSApp.currentEvent
        if event?.type == .rightMouseUp || event?.modifierFlags.contains(.control) == true {
            showMenu()
        } else {
            togglePopover()
        }
    }

    private func togglePopover() {
        guard let button = statusItem.button else { return }
        if popover.isShown {
            popover.performClose(nil)
            return
        }
        model.now = Date()
        NSApp.activate(ignoringOtherApps: true)
        popover.show(relativeTo: button.bounds, of: button, preferredEdge: .minY)
        popover.contentViewController?.view.window?.makeKey()
        if model.data.map({ Date().timeIntervalSince($0.fetchedAt) > model.pollInterval }) ?? true, model.status != .keychainDenied {
            refresh(manual: false)
        }
    }

    // MARK: Right-click menu

    private func showMenu() {
        let menu = NSMenu()
        menu.addItem(withTitle: "Show Details", action: #selector(menuShowDetails), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Refresh Now", action: #selector(menuRefresh), keyEquivalent: "r").target = self
        menu.addItem(.separator())

        let providerItem = NSMenuItem(title: "Show Usage For", action: nil, keyEquivalent: "")
        let providers = NSMenu()
        for provider in Provider.allCases {
            let item = NSMenuItem(title: provider.name, action: #selector(menuProvider(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = provider.rawValue
            item.state = provider == client.provider ? .on : .off
            providers.addItem(item)
        }
        providerItem.submenu = providers
        menu.addItem(providerItem)

        let sizeItem = NSMenuItem(title: "Size", action: nil, keyEquivalent: "")
        let sizes = NSMenu()
        for size in StripSize.allCases {
            let item = NSMenuItem(title: size.title, action: #selector(menuSize(_:)), keyEquivalent: "")
            item.target = self
            item.representedObject = size.rawValue
            item.state = size == StripSize.current ? .on : .off
            sizes.addItem(item)
        }
        sizeItem.submenu = sizes
        menu.addItem(sizeItem)

        let login = NSMenuItem(title: "Open at Login", action: #selector(menuLogin), keyEquivalent: "")
        login.target = self
        login.state = LoginItem.isEnabled ? .on : .off
        menu.addItem(login)
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit Claude Usage Bar", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

        // Attach temporarily so the menu drops down from the status item like a native one.
        statusItem.menu = menu
        statusItem.button?.performClick(nil)
        statusItem.menu = nil
    }

    @objc private func menuShowDetails() { togglePopover() }
    @objc private func menuRefresh() { refresh(manual: true) }
    @objc private func menuLogin() { LoginItem.setEnabled(!LoginItem.isEnabled) }

    @objc private func menuProvider(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String, let provider = Provider(rawValue: raw) else { return }
        switchProvider(provider)
    }

    @objc private func menuSize(_ sender: NSMenuItem) {
        guard let raw = sender.representedObject as? String, let size = StripSize(rawValue: raw) else { return }
        StripSize.current = size
        render()
    }

    // MARK: Setup

    /// Opens Terminal to sign in (Claude Code for /login, or `codex login` for ChatGPT),
    /// retries the Keychain, or opens the CLI's install guide.
    private func openSetup() {
        popover.performClose(nil)
        if model.status == .keychainDenied {
            model.status = .none
            refresh(manual: false)
            return
        }
        let chatgpt = client.provider == .chatgpt
        guard let cli = CLI.path(for: client.provider) else {
            model.cliInstalled = false
            NSWorkspace.shared.open(URL(string: chatgpt ? "https://developers.openai.com/codex/cli"
                                                        : "https://docs.claude.com/en/docs/claude-code/setup")!)
            return
        }
        CLI.openInTerminal(cli, arguments: chatgpt ? ["login"] : [])
    }
}

// MARK: - Helpers

enum CLI {
    /// Common install locations of Claude Code or Codex; GUI apps don't inherit the shell's PATH, so we look directly.
    static func path(for provider: Provider) -> String? {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        let name = provider == .chatgpt ? "codex" : "claude"
        var candidates = [
            "\(home)/.local/bin/\(name)",
            "/opt/homebrew/bin/\(name)",
            "/usr/local/bin/\(name)",
            "\(home)/.npm-global/bin/\(name)",
        ]
        if provider == .claude { candidates.insert("\(home)/.claude/local/claude", at: 1) }
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    /// Writes a tiny .command script and opens it, which runs it in Terminal (no Automation permission needed).
    static func openInTerminal(_ cli: String, arguments: [String] = []) {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ClaudeUsageBar", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let script = dir.appendingPathComponent("login.command")
        let quoted = ([cli] + arguments).map { "'" + $0.replacingOccurrences(of: "'", with: "'\\''") + "'" }
        let body = "#!/bin/zsh -l\ncd ~\n\(quoted.joined(separator: " "))\n"
        do {
            try body.write(to: script, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: script.path)
            NSWorkspace.shared.open(script)
        } catch {
            NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/Utilities/Terminal.app"))
        }
    }
}

/// Open at Login via SMAppService (macOS 13+). On by default the first time, and a user's "off" sticks.
enum LoginItem {
    static var isEnabled: Bool { SMAppService.mainApp.status == .enabled }

    static func setEnabled(_ on: Bool) {
        UserDefaults.standard.set(true, forKey: "loginItemInitialised")
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            NSLog("ClaudeUsageBar: couldn't change login item: \(error.localizedDescription)")
        }
    }

    static func applyDefault() {
        // Only for a real .app bundle (not `swift run`).
        guard Bundle.main.bundleURL.pathExtension == "app",
              !UserDefaults.standard.bool(forKey: "loginItemInitialised") else { return }
        setEnabled(true)
    }
}
