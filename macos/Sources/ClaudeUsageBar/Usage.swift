import Foundation
import Security

// MARK: - Model

/// Whose plan usage the app shows. Picked from the right-click menu; Claude is the default.
enum Provider: String, CaseIterable {
    case claude, chatgpt, gemini

    var name: String {
        switch self {
        case .claude: return "Claude"
        case .chatgpt: return "ChatGPT"
        case .gemini: return "Gemini"
        }
    }

    /// The CLI whose login the usage comes from.
    var cliName: String {
        switch self {
        case .claude: return "Claude Code"
        case .chatgpt: return "Codex"
        case .gemini: return "Gemini CLI"
        }
    }

    static var current: Provider {
        get { UserDefaults.standard.string(forKey: "provider").flatMap(Provider.init(rawValue:)) ?? .claude }
        set { UserDefaults.standard.set(newValue.rawValue, forKey: "provider") }
    }
}

struct UsageWindow: Equatable {
    let percent: Double
    let resetsAt: Date?
    /// The window's duration (5 hours, 7 days), used for labels and the pace marker.
    var length: TimeInterval? = nil
    /// Replaces the length-based label in the menu bar when set (Gemini's "P" and "F").
    var label: String? = nil
}

struct ExtraUsage: Equatable {
    let enabled: Bool
    let limitUSD: Double?
    let usedUSD: Double?
    let percent: Double?
}

struct UsageSnapshot: Equatable {
    var fiveHour: UsageWindow?
    var sevenDay: UsageWindow?
    var sevenDayOpus: UsageWindow?
    var sevenDaySonnet: UsageWindow?
    var extra: ExtraUsage?
    var fetchedAt: Date

    static func parse(_ root: [String: Any], fetchedAt: Date) -> UsageSnapshot {
        UsageSnapshot(
            fiveHour: window(root["five_hour"], length: Fmt.fiveHours),
            sevenDay: window(root["seven_day"], length: Fmt.sevenDays),
            sevenDayOpus: window(root["seven_day_opus"], length: Fmt.sevenDays),
            sevenDaySonnet: window(root["seven_day_sonnet"], length: Fmt.sevenDays),
            extra: parseExtra(root["extra_usage"]),
            fetchedAt: fetchedAt
        )
    }

    /// Codex's usage response: rate_limit.primary_window (the short one, 5 hours) goes in the session slot and
    /// secondary_window (weekly) in the weekly slot. Percent is used_percent; reset_at is Unix seconds.
    static func parseChatGPT(_ root: [String: Any], fetchedAt: Date) -> UsageSnapshot {
        let rl = root["rate_limit"] as? [String: Any]
        return UsageSnapshot(
            fiveHour: codexWindow(rl?["primary_window"], fetchedAt: fetchedAt),
            sevenDay: codexWindow(rl?["secondary_window"], fetchedAt: fetchedAt),
            fetchedAt: fetchedAt
        )
    }

    /// Gemini's quota response: a daily request bucket per model, with remainingFraction and resetTime.
    /// The busiest Pro model goes in the session slot and the busiest Flash model in the weekly slot.
    static func parseGemini(_ root: [String: Any], fetchedAt: Date) -> UsageSnapshot {
        var pro: UsageWindow?, flash: UsageWindow?
        for b in (root["buckets"] as? [[String: Any]]) ?? [] {
            guard let left = number(b["remainingFraction"]) else { continue }
            let model = (b["modelId"] as? String)?.lowercased() ?? ""
            let used = min(max((1 - left) * 100, 0), 100)
            let reset = (b["resetTime"] as? String).flatMap(parseDate)
            if model.contains("pro"), used > (pro?.percent ?? -1) {
                pro = UsageWindow(percent: used, resetsAt: reset, length: 86400, label: "P")
            } else if model.contains("flash"), used > (flash?.percent ?? -1) {
                flash = UsageWindow(percent: used, resetsAt: reset, length: 86400, label: "F")
            }
        }
        return UsageSnapshot(fiveHour: pro, sevenDay: flash, fetchedAt: fetchedAt)
    }

    static func parse(_ root: [String: Any], provider: Provider, fetchedAt: Date) -> UsageSnapshot {
        switch provider {
        case .claude: return parse(root, fetchedAt: fetchedAt)
        case .chatgpt: return parseChatGPT(root, fetchedAt: fetchedAt)
        case .gemini: return parseGemini(root, fetchedAt: fetchedAt)
        }
    }

    private static func codexWindow(_ value: Any?, fetchedAt: Date) -> UsageWindow? {
        guard let o = value as? [String: Any], let pct = number(o["used_percent"]) else { return nil }
        var reset: Date?
        if let at = number(o["reset_at"]), at > 0 { reset = Date(timeIntervalSince1970: at) }
        else if let after = number(o["reset_after_seconds"]) { reset = fetchedAt.addingTimeInterval(after) }
        let length = number(o["limit_window_seconds"]).flatMap { $0 > 0 ? $0 : nil }
        return UsageWindow(percent: min(max(pct, 0), 100), resetsAt: reset, length: length)
    }

    private static func window(_ value: Any?, length: TimeInterval) -> UsageWindow? {
        guard let o = value as? [String: Any], let pct = number(o["utilization"]) else { return nil }
        return UsageWindow(percent: min(max(pct, 0), 100), resetsAt: (o["resets_at"] as? String).flatMap(parseDate), length: length)
    }

    private static func parseExtra(_ value: Any?) -> ExtraUsage? {
        guard let o = value as? [String: Any] else { return nil }
        // Credits are reported in cents.
        let limit = number(o["monthly_limit"]).map { $0 / 100 }
        let used = number(o["used_credits"]).map { $0 / 100 }
        var pct = number(o["utilization"])
        if pct == nil, let l = limit, l > 0, let u = used { pct = u / l * 100 }
        return ExtraUsage(enabled: (o["is_enabled"] as? Bool) ?? false, limitUSD: limit, usedUSD: used,
                          percent: pct.map { min(max($0, 0), 100) })
    }

    private static func number(_ v: Any?) -> Double? {
        // JSON booleans also arrive as NSNumber; tell them apart by CF type rather than `is Bool`,
        // which is also true for the numbers 0 and 1.
        guard let n = v as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID() else { return nil }
        return n.doubleValue
    }

    /// ISO-8601 with or without (possibly 6-digit) fractional seconds.
    static func parseDate(_ s: String) -> Date? {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        let trimmed = s.replacingOccurrences(of: #"\.\d+"#, with: "", options: .regularExpression)
        return f.date(from: trimmed)
    }
}

enum FetchStatus: Equatable {
    case none, ok, noCredentials, keychainDenied, unauthorized, rateLimited, error
}

struct FetchResult {
    var status: FetchStatus
    var data: UsageSnapshot?
    var plan: String?
    var message: String?
    var retryAfter: TimeInterval?
}

// MARK: - Credentials

/// Claude Code keeps its login in the macOS Keychain ("Claude Code-credentials"); older installs used a file.
enum Credentials {
    enum Result {
        case found(token: String, plan: String?)
        case missing(plan: String?)
        case denied
    }

    static var configDir: URL {
        if let d = ProcessInfo.processInfo.environment["CLAUDE_CONFIG_DIR"], !d.isEmpty {
            return URL(fileURLWithPath: d)
        }
        return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".claude")
    }

    /// Reading the Keychain item shows a macOS permission prompt the first time; "Always Allow" makes it silent.
    static func read() -> Result {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: "Claude Code-credentials",
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var out: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &out)
        switch status {
        case errSecSuccess:
            if let data = out as? Data, let r = parse(data), case .found = r { return r }
        case errSecUserCanceled, errSecAuthFailed, errSecInteractionNotAllowed:
            return .denied
        default:
            break
        }

        let file = configDir.appendingPathComponent(".credentials.json")
        if let data = try? Data(contentsOf: file), let r = parse(data) { return r }
        return .missing(plan: nil)
    }

    private static func parse(_ data: Data) -> Result? {
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let o = root["claudeAiOauth"] as? [String: Any] else { return nil }
        let plan = o["subscriptionType"] as? String
        if let token = o["accessToken"] as? String, !token.isEmpty { return .found(token: token, plan: plan) }
        return .missing(plan: plan)
    }
}

/// Codex keeps its ChatGPT login in ~/.codex/auth.json (or $CODEX_HOME/auth.json).
enum CodexCredentials {
    static var home: URL {
        if let d = ProcessInfo.processInfo.environment["CODEX_HOME"], !d.isEmpty {
            return URL(fileURLWithPath: d)
        }
        return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".codex")
    }

    static func read() -> (token: String, account: String?)? {
        guard let data = try? Data(contentsOf: home.appendingPathComponent("auth.json")),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tokens = root["tokens"] as? [String: Any],
              let token = tokens["access_token"] as? String, !token.isEmpty else { return nil }
        return (token, tokens["account_id"] as? String)
    }
}

/// Gemini CLI keeps its Google login in ~/.gemini/oauth_creds.json; expiry_date is in milliseconds.
enum GeminiCredentials {
    static func read() -> (token: String, expiry: Date?)? {
        let file = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".gemini/oauth_creds.json")
        guard let data = try? Data(contentsOf: file),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let token = root["access_token"] as? String, !token.isEmpty else { return nil }
        let expiry = (root["expiry_date"] as? NSNumber).map { Date(timeIntervalSince1970: $0.doubleValue / 1000) }
        return (token, expiry)
    }
}

// MARK: - API client

/// Refuses redirects so the bearer token can only ever be sent to the provider's own host.
private final class NoRedirect: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

/// Fetches plan usage with a CLI's own login: Claude Code's for Claude, Codex's for ChatGPT, Gemini CLI's for Gemini.
final class UsageClient {
    let provider: Provider
    private let session: URLSession
    private var token: String?
    private var account: String?
    private var plan: String?
    private var rejected: String?
    /// Gemini only: the Code Assist project the quota belongs to, looked up once.
    private var project: String?

    private static let geminiBase = "https://cloudcode-pa.googleapis.com/v1internal:"

    private var host: String {
        switch provider {
        case .claude: return "api.anthropic.com"
        case .chatgpt: return "chatgpt.com"
        case .gemini: return "cloudcode-pa.googleapis.com"
        }
    }
    private var company: String {
        switch provider {
        case .claude: return "Anthropic"
        case .chatgpt: return "OpenAI"
        case .gemini: return "Google"
        }
    }

    init(provider: Provider) {
        self.provider = provider
        let c = URLSessionConfiguration.ephemeral
        c.timeoutIntervalForRequest = 20
        c.httpCookieStorage = nil
        c.urlCache = nil
        session = URLSession(configuration: c, delegate: NoRedirect(), delegateQueue: nil)
    }

    /// Forget the cached token so the next fetch re-reads the Keychain (e.g. after the user allows access).
    func reset() { token = nil }

    func fetch() async -> FetchResult {
        switch provider {
        case .chatgpt:
            // Codex's login is a plain file, so it's re-read every time and a new login is picked up straight away.
            let creds = CodexCredentials.read()
            token = creds?.token
            account = creds?.account
        case .gemini:
            // Gemini CLI's token only lasts about an hour and only Gemini CLI renews it; don't send an expired one.
            let creds = GeminiCredentials.read()
            token = creds?.token
            if let expiry = creds?.expiry, expiry < Date().addingTimeInterval(60), token != nil {
                return FetchResult(status: .unauthorized, plan: plan, message: "Login expired")
            }
        case .claude:
            guard token == nil || token == rejected else { break }
            // The token is kept in memory so the Keychain is only read at start-up and after a rejection.
            switch Credentials.read() {
            case .found(let t, let p): token = t; plan = p
            case .missing(let p): token = nil; plan = p ?? plan
            case .denied: token = nil
                return FetchResult(status: .keychainDenied, plan: plan, message: "Keychain access was denied")
            }
        }
        guard let token else {
            return FetchResult(status: .noCredentials, plan: plan, message: "Not signed in")
        }
        if token == rejected {
            return FetchResult(status: .unauthorized, plan: plan, message: "Login expired")
        }

        do {
            if provider == .gemini && project == nil {
                let (data, http) = try await send(geminiRequest("loadCodeAssist", token: token, body: loadCodeAssistBody()))
                guard (200..<300).contains(http.statusCode) else { return failure(http, token: token) }
                let root = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
                project = (root?["cloudaicompanionProject"] as? String) ?? Self.envProject
                // "free-tier", "standard-tier"; Fmt.plan names them.
                let tier = (root?["currentTier"] as? [String: Any])?["id"] as? String
                if let tier { plan = tier }
                guard project != nil else {
                    // Say why, as specifically as Google lets us (same wording as the Windows app).
                    let reason = (root?["ineligibleTiers"] as? [[String: Any]])?
                        .compactMap { $0["reasonMessage"] as? String }.first { !$0.isEmpty }
                    let message = reason.map { "Google says: \($0.count > 160 ? String($0.prefix(157)) + "…" : $0)" }
                        ?? (tier != nil ? "This account needs a Google Cloud project: set GOOGLE_CLOUD_PROJECT"
                                        : "Gemini CLI hasn't finished setting up — run gemini and send one message")
                    return FetchResult(status: .error, plan: plan, message: message)
                }
            }

            let (data, http) = try await send(request(token: token))
            guard (200..<300).contains(http.statusCode) else { return failure(http, token: token) }
            guard data.count < 256 * 1024,
                  let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                return FetchResult(status: .error, plan: plan, message: "Unexpected response")
            }
            let now = Date()
            if let p = root["plan_type"] as? String, !p.isEmpty { plan = p }
            Cache.save(raw: root, plan: plan, at: now, provider: provider)
            return FetchResult(status: .ok, data: UsageSnapshot.parse(root, provider: provider, fetchedAt: now), plan: plan)
        } catch let e as URLError where e.code == .timedOut {
            return FetchResult(status: .error, plan: plan, message: "Request timed out")
        } catch is URLError {
            return FetchResult(status: .error, plan: plan, message: "Can't reach \(host)")
        } catch {
            return FetchResult(status: .error, plan: plan, message: "Couldn't update")
        }
    }

    private func send(_ req: URLRequest) async throws -> (Data, HTTPURLResponse) {
        var req = req
        req.setValue("ClaudeUsageBar-macOS/1.0", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await session.data(for: req)
        guard let http = response as? HTTPURLResponse else { throw URLError(.badServerResponse) }
        return (data, http)
    }

    private func failure(_ http: HTTPURLResponse, token: String) -> FetchResult {
        switch http.statusCode {
        case 429:
            let wait = http.value(forHTTPHeaderField: "Retry-After").flatMap { TimeInterval($0) }
            return FetchResult(status: .rateLimited, plan: plan, message: "Rate limited by \(company)", retryAfter: wait)
        case 401, 403:
            rejected = token
            return FetchResult(status: .unauthorized, plan: plan, message: "Login expired")
        default:
            return FetchResult(status: .error, plan: plan, message: "Server error (\(http.statusCode))")
        }
    }

    private func request(token: String) -> URLRequest {
        switch provider {
        case .gemini:
            return geminiRequest("retrieveUserQuota", token: token, body: ["project": project ?? ""])
        case .chatgpt:
            var req = URLRequest(url: URL(string: "https://chatgpt.com/backend-api/wham/usage")!)
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            if let account, !account.isEmpty { req.setValue(account, forHTTPHeaderField: "ChatGPT-Account-Id") }
            return req
        case .claude:
            var req = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
            req.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
            return req
        }
    }

    private static var envProject: String? {
        ProcessInfo.processInfo.environment["GOOGLE_CLOUD_PROJECT"].flatMap { $0.isEmpty ? nil : $0 }
    }

    private func loadCodeAssistBody() -> [String: Any] {
        var metadata: [String: Any] = ["ideType": "IDE_UNSPECIFIED", "platform": "PLATFORM_UNSPECIFIED", "pluginType": "GEMINI"]
        var body: [String: Any] = [:]
        if let p = Self.envProject { body["cloudaicompanionProject"] = p; metadata["duetProject"] = p }
        body["metadata"] = metadata
        return body
    }

    private func geminiRequest(_ method: String, token: String, body: [String: Any]) -> URLRequest {
        var req = URLRequest(url: URL(string: Self.geminiBase + method)!)
        req.httpMethod = "POST"
        req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.httpBody = try? JSONSerialization.data(withJSONObject: body)
        return req
    }
}

// MARK: - Cache (last usage numbers only — never the token)

enum Cache {
    static func url(_ provider: Provider) -> URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ClaudeUsageBar", isDirectory: true)
        switch provider {
        case .claude: return dir.appendingPathComponent("last.json")
        case .chatgpt: return dir.appendingPathComponent("last-chatgpt.json")
        case .gemini: return dir.appendingPathComponent("last-gemini.json")
        }
    }

    static func save(raw: [String: Any], plan: String?, at: Date, provider: Provider) {
        let file = url(provider)
        var obj: [String: Any] = ["fetchedAt": at.timeIntervalSince1970, "data": raw]
        if let plan { obj["plan"] = plan }
        guard let data = try? JSONSerialization.data(withJSONObject: obj) else { return }
        try? FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: file, options: .atomic)
    }

    static func load(_ provider: Provider) -> (data: UsageSnapshot, plan: String?)? {
        guard let data = try? Data(contentsOf: url(provider)),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let at = obj["fetchedAt"] as? Double,
              let raw = obj["data"] as? [String: Any] else { return nil }
        return (UsageSnapshot.parse(raw, provider: provider, fetchedAt: Date(timeIntervalSince1970: at)), obj["plan"] as? String)
    }
}

// MARK: - Formatting

enum Fmt {
    static let fiveHours: TimeInterval = 5 * 3600
    static let sevenDays: TimeInterval = 7 * 86400

    /// Compact countdown for the menu bar: 42m, 3h 5m, 4d 3h.
    static func short(_ t: TimeInterval) -> String {
        if t <= 0 { return "now" }
        let mins = Int((t / 60).rounded(.up))
        if t < 3600 { return "\(max(1, mins))m" }
        let h = Int(t / 3600), m = Int(t.truncatingRemainder(dividingBy: 3600) / 60)
        if t < 86400 { return m == 0 ? "\(h)h" : "\(h)h \(m)m" }
        let d = Int(t / 86400), dh = Int(t.truncatingRemainder(dividingBy: 86400) / 3600)
        return dh == 0 ? "\(d)d" : "\(d)d \(dh)h"
    }

    static func reset(_ date: Date?, now: Date) -> String {
        guard let date else { return "" }
        let left = date.timeIntervalSince(now)
        if left <= 0 { return "Resetting…" }
        if left < 3600 { return "Resets in \(max(1, Int((left / 60).rounded(.up))))m" }
        if left < 86400 {
            return String(format: "Resets in %dh %02dm", Int(left / 3600), Int(left.truncatingRemainder(dividingBy: 3600) / 60))
        }
        let f = DateFormatter()
        f.setLocalizedDateFormatFromTemplate("EEE j:mm")
        return "Resets " + f.string(from: date)
    }

    static func ago(_ t: TimeInterval) -> String {
        if t < 45 { return "just now" }
        if t < 3600 { return "\(max(1, Int((t / 60).rounded()))) min ago" }
        if t < 86400 { return "\(Int(t / 3600)) h ago" }
        return "\(Int(t / 86400)) d ago"
    }

    static func inTime(_ t: TimeInterval) -> String {
        t < 60 ? "\(max(1, Int(t.rounded(.up))))s" : "\(Int((t / 60).rounded(.up))) min"
    }

    static func money(_ v: Double) -> String {
        v >= 1000 ? String(format: "$%.0f", v) : String(format: "$%.2f", v)
    }

    static func plan(_ p: String?) -> String {
        switch p?.lowercased() {
        case nil, "": return ""
        case "pro": return "Pro"
        case "plus": return "Plus"
        case "free-tier": return "Free"
        case "standard-tier": return "Standard"
        case "legacy-tier": return "Legacy"
        case "max": return "Max"
        case "team": return "Team"
        case "enterprise": return "Enterprise"
        case let other?: return other.capitalized
        }
    }

    /// Short menu bar label for a window: "5h", "7d". Falls back when the length is unknown.
    static func label(_ w: UsageWindow?, fallback: String) -> String {
        if let label = w?.label { return label }
        guard let l = w?.length else { return fallback }
        if l >= 86400 { return "\(Int((l / 86400).rounded()))d" }
        if l >= 3600 { return "\(Int((l / 3600).rounded()))h" }
        return fallback
    }

    /// "5-hour rolling window", "Weekly window": describes a window by its length.
    static func describe(_ length: TimeInterval) -> String {
        if abs(length - sevenDays) < 43200 { return "Weekly window" }
        if length >= 86400 { return "\(Int((length / 86400).rounded()))-day window" }
        return "\(Int((length / 3600).rounded()))-hour rolling window"
    }

    /// How far through the window we are (0...1), for the pace marker.
    static func elapsed(_ reset: Date?, length: TimeInterval, now: Date) -> Double? {
        guard let reset else { return nil }
        return min(max(1 - reset.timeIntervalSince(now) / length, 0), 1)
    }
}
