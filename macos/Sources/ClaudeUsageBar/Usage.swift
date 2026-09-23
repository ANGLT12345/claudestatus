import Foundation
import Security

// MARK: - Model

struct UsageWindow: Equatable {
    let percent: Double
    let resetsAt: Date?
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
            fiveHour: window(root["five_hour"]),
            sevenDay: window(root["seven_day"]),
            sevenDayOpus: window(root["seven_day_opus"]),
            sevenDaySonnet: window(root["seven_day_sonnet"]),
            extra: parseExtra(root["extra_usage"]),
            fetchedAt: fetchedAt
        )
    }

    private static func window(_ value: Any?) -> UsageWindow? {
        guard let o = value as? [String: Any], let pct = number(o["utilization"]) else { return nil }
        return UsageWindow(percent: min(max(pct, 0), 100), resetsAt: (o["resets_at"] as? String).flatMap(parseDate))
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

// MARK: - API client

/// Refuses redirects so the bearer token can only ever be sent to api.anthropic.com.
private final class NoRedirect: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

final class UsageClient {
    private static let endpoint = URL(string: "https://api.anthropic.com/api/oauth/usage")!
    private let session: URLSession
    private var token: String?
    private var plan: String?
    private var rejected: String?

    init() {
        let c = URLSessionConfiguration.ephemeral
        c.timeoutIntervalForRequest = 20
        c.httpCookieStorage = nil
        c.urlCache = nil
        session = URLSession(configuration: c, delegate: NoRedirect(), delegateQueue: nil)
    }

    /// Forget the cached token so the next fetch re-reads the Keychain (e.g. after the user allows access).
    func reset() { token = nil }

    func fetch() async -> FetchResult {
        // The token is kept in memory so the Keychain is only read at start-up and after a rejection.
        if token == nil || token == rejected {
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

        var req = URLRequest(url: Self.endpoint)
        req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        req.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        req.setValue("ClaudeUsageBar-macOS/1.0", forHTTPHeaderField: "User-Agent")

        do {
            let (data, response) = try await session.data(for: req)
            guard let http = response as? HTTPURLResponse else {
                return FetchResult(status: .error, plan: plan, message: "Couldn't update")
            }
            switch http.statusCode {
            case 200..<300:
                guard data.count < 256 * 1024,
                      let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                    return FetchResult(status: .error, plan: plan, message: "Unexpected response")
                }
                let now = Date()
                Cache.save(raw: root, plan: plan, at: now)
                return FetchResult(status: .ok, data: UsageSnapshot.parse(root, fetchedAt: now), plan: plan)
            case 429:
                let wait = http.value(forHTTPHeaderField: "Retry-After").flatMap { TimeInterval($0) }
                return FetchResult(status: .rateLimited, plan: plan, message: "Rate limited by Anthropic", retryAfter: wait)
            case 401, 403:
                rejected = token
                return FetchResult(status: .unauthorized, plan: plan, message: "Login expired")
            default:
                return FetchResult(status: .error, plan: plan, message: "Server error (\(http.statusCode))")
            }
        } catch let e as URLError where e.code == .timedOut {
            return FetchResult(status: .error, plan: plan, message: "Request timed out")
        } catch is URLError {
            return FetchResult(status: .error, plan: plan, message: "Can't reach api.anthropic.com")
        } catch {
            return FetchResult(status: .error, plan: plan, message: "Couldn't update")
        }
    }
}

// MARK: - Cache (last usage numbers only — never the token)

enum Cache {
    static var url: URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ClaudeUsageBar", isDirectory: true)
        return dir.appendingPathComponent("last.json")
    }

    static func save(raw: [String: Any], plan: String?, at: Date) {
        var obj: [String: Any] = ["fetchedAt": at.timeIntervalSince1970, "data": raw]
        if let plan { obj["plan"] = plan }
        guard let data = try? JSONSerialization.data(withJSONObject: obj) else { return }
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? data.write(to: url, options: .atomic)
    }

    static func load() -> (data: UsageSnapshot, plan: String?)? {
        guard let data = try? Data(contentsOf: url),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let at = obj["fetchedAt"] as? Double,
              let raw = obj["data"] as? [String: Any] else { return nil }
        return (UsageSnapshot.parse(raw, fetchedAt: Date(timeIntervalSince1970: at)), obj["plan"] as? String)
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
        case "max": return "Max"
        case "team": return "Team"
        case "enterprise": return "Enterprise"
        case let other?: return other.capitalized
        }
    }

    /// How far through the window we are (0...1), for the pace marker.
    static func elapsed(_ reset: Date?, length: TimeInterval, now: Date) -> Double? {
        guard let reset else { return nil }
        return min(max(1 - reset.timeIntervalSince(now) / length, 0), 1)
    }
}
