import SwiftUI

struct PopupActions {
    let refresh: () -> Void
    let setup: () -> Void
}

private enum RowKind: String { case session, weekly, sonnet, opus, extra }

private struct RowModel: Identifiable {
    let kind: RowKind
    let title: String
    let subtitle: String
    let percent: Double?
    let detail: String
    let pace: Double?
    var id: String { kind.rawValue }

    var symbol: String {
        switch kind {
        case .session: return "stopwatch"
        case .weekly: return "chart.bar.fill"
        case .sonnet: return "sparkles"
        case .opus: return "diamond"
        case .extra: return "creditcard"
        }
    }
}

/// The details popover: same content and layout as the Windows popup.
struct PopupView: View {
    @ObservedObject var model: Model
    let actions: PopupActions
    @Environment(\.colorScheme) private var scheme

    private var dark: Bool { scheme == .dark }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            if model.data != nil || !model.needsSetup {
                VStack(spacing: 0) {
                    ForEach(rows) { RowView(row: $0, dark: dark) }
                }
                .padding(.top, 6)
            }
            if model.needsSetup {
                SetupCard(model: model, action: actions.setup)
                    .padding(.top, 10)
            }
            if model.status == .rateLimited || model.status == .error {
                banner.padding(.top, 10)
            }
            Divider().padding(.top, 12)
            footer.padding(.top, 10)
        }
        .padding(.horizontal, 18)
        .padding(.top, 16)
        .padding(.bottom, 12)
        .frame(width: 330)
    }

    private var header: some View {
        HStack(spacing: 8) {
            Text("Claude")
                .font(.system(size: 17, weight: .bold))
            let plan = Fmt.plan(model.plan)
            if !plan.isEmpty {
                Text(plan)
                    .font(.system(size: 10.5, weight: .semibold))
                    .foregroundStyle(Color(nsColor: Status.claude))
                    .padding(.horizontal, 7)
                    .padding(.vertical, 2)
                    .background(Capsule().fill(Color(nsColor: Status.claude).opacity(dark ? 0.22 : 0.14)))
            }
            Spacer()
            Button(action: actions.refresh) {
                ZStack {
                    if model.fetching {
                        ProgressView().controlSize(.small)
                    } else {
                        Image(systemName: "arrow.clockwise")
                            .font(.system(size: 13, weight: .medium))
                            .foregroundStyle(.secondary)
                    }
                }
                .frame(width: 26, height: 26)
                .contentShape(Rectangle())
            }
            .buttonStyle(.borderless)
            .keyboardShortcut("r", modifiers: .command)
            .help("Refresh (⌘R)")
        }
    }

    private var rows: [RowModel] {
        let d = model.data, now = model.now
        func window(_ kind: RowKind, _ title: String, _ sub: String, _ w: UsageWindow?, _ length: TimeInterval) -> RowModel {
            RowModel(kind: kind, title: title, subtitle: sub, percent: w?.percent,
                     detail: w.map { Fmt.reset($0.resetsAt, now: now) } ?? "",
                     pace: w.flatMap { Fmt.elapsed($0.resetsAt, length: length, now: now) })
        }
        var rows = [
            window(.session, "Session", "5-hour rolling window", d?.fiveHour, Fmt.fiveHours),
            window(.weekly, "Weekly", "All models", d?.sevenDay, Fmt.sevenDays),
        ]
        if let s = d?.sevenDaySonnet { rows.append(window(.sonnet, "Sonnet", "Weekly · Sonnet only", s, Fmt.sevenDays)) }
        if let o = d?.sevenDayOpus { rows.append(window(.opus, "Opus", "Weekly · Opus only", o, Fmt.sevenDays)) }
        if let x = d?.extra, x.enabled {
            let sub = (x.usedUSD != nil && x.limitUSD != nil) ? "\(Fmt.money(x.usedUSD!)) of \(Fmt.money(x.limitUSD!))" : "Pay-as-you-go credits"
            rows.append(RowModel(kind: .extra, title: "Extra usage", subtitle: sub, percent: x.percent, detail: "This month", pace: nil))
        }
        return rows
    }

    private var banner: some View {
        let color = Color(nsColor: Status.color(75, dark: dark))
        var msg = model.message ?? "Couldn't update"
        if let next = model.nextAttempt, next > model.now { msg += " · retry in \(Fmt.inTime(next.timeIntervalSince(model.now)))" }
        return HStack(spacing: 8) {
            Image(systemName: "exclamationmark.triangle.fill")
            Text(msg).font(.system(size: 12))
            Spacer(minLength: 0)
        }
        .foregroundStyle(color)
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(RoundedRectangle(cornerRadius: 8).fill(color.opacity(0.14)))
    }

    private var footer: some View {
        HStack(spacing: 6) {
            Text(footerText)
            Spacer()
            if !model.needsSetup {
                Circle()
                    .fill(Color(nsColor: Status.color(model.status == .ok || model.status == .none ? 0 : 75, dark: dark)))
                    .frame(width: 6, height: 6)
                Text("Every \(Int(model.pollInterval / 60)) min")
            }
        }
        .font(.system(size: 11.5))
        .foregroundStyle(.secondary)
    }

    private var footerText: String {
        if model.fetching { return "Refreshing…" }
        if let d = model.data { return "Updated " + Fmt.ago(model.now.timeIntervalSince(d.fetchedAt)) }
        return model.needsSetup ? "Waiting for sign-in…" : "Waiting for first update"
    }
}

private struct RowView: View {
    let row: RowModel
    let dark: Bool

    private var color: Color {
        row.percent.map { Color(nsColor: Status.color($0, dark: dark)) } ?? .secondary
    }

    var body: some View {
        VStack(spacing: 9) {
            HStack(spacing: 12) {
                ZStack {
                    RoundedRectangle(cornerRadius: 9).fill(color.opacity(dark ? 0.2 : 0.14))
                    Image(systemName: row.symbol)
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundStyle(color)
                }
                .frame(width: 32, height: 32)

                VStack(alignment: .leading, spacing: 2) {
                    Text(row.title).font(.system(size: 13.5, weight: .semibold))
                    Text(row.subtitle).font(.system(size: 11.5)).foregroundStyle(.secondary)
                }
                Spacer(minLength: 8)
                VStack(alignment: .trailing, spacing: 2) {
                    Text(row.percent.map { "\(Int($0.rounded()))%" } ?? "—")
                        .font(.system(size: 20, weight: .bold, design: .rounded))
                        .monospacedDigit()
                        .foregroundStyle(row.percent == nil ? Color.secondary : color)
                    if !row.detail.isEmpty {
                        Text(row.detail).font(.system(size: 11)).foregroundStyle(.tertiary)
                    }
                }
            }
            UsageBar(percent: row.percent, pace: row.pace, color: color)
        }
        .padding(.vertical, 8)
    }
}

/// Progress bar with a pace marker: where usage would be if spread evenly across the window.
private struct UsageBar: View {
    let percent: Double?
    let pace: Double?
    let color: Color

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.primary.opacity(0.1))
                if let p = percent, p > 0 {
                    Capsule()
                        .fill(LinearGradient(colors: [color.opacity(0.75), color], startPoint: .leading, endPoint: .trailing))
                        .frame(width: max(6, geo.size.width * p / 100))
                }
                if let pace, pace > 0.01, pace < 0.99 {
                    RoundedRectangle(cornerRadius: 1)
                        .fill(Color.primary.opacity(0.55))
                        .frame(width: 2, height: 12)
                        .offset(x: geo.size.width * pace - 1)
                }
            }
        }
        .frame(height: 6)
    }
}

/// Walks the user through signing in to Claude Code (or allowing Keychain access).
private struct SetupCard: View {
    @ObservedObject var model: Model
    let action: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title).font(.system(size: 14, weight: .semibold))
            Text(subtitle).font(.system(size: 12)).foregroundStyle(.secondary).padding(.top, 3)
                .fixedSize(horizontal: false, vertical: true)

            VStack(alignment: .leading, spacing: 9) {
                ForEach(Array(steps.enumerated()), id: \.offset) { item in
                    HStack(spacing: 10) {
                        Text("\(item.offset + 1)")
                            .font(.system(size: 10.5, weight: .semibold))
                            .foregroundStyle(Color(nsColor: Status.claude))
                            .frame(width: 18, height: 18)
                            .background(Circle().fill(Color(nsColor: Status.claude).opacity(0.2)))
                        item.element
                            .font(.system(size: 12.5))
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .padding(.top, 12)

            Button(action: action) {
                Label(buttonTitle, systemImage: buttonSymbol)
                    .font(.system(size: 13, weight: .semibold))
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 3)
            }
            .buttonStyle(.borderedProminent)
            .tint(Color(nsColor: Status.claude))
            .controlSize(.large)
            .padding(.top, 14)
        }
        .padding(16)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.primary.opacity(0.04)))
        .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color.primary.opacity(0.08)))
    }

    private var title: String {
        switch model.status {
        case .keychainDenied: return "Allow Keychain access"
        case .unauthorized: return "Sign in again"
        default: return "Connect Claude Code"
        }
    }

    private var subtitle: String {
        switch model.status {
        case .keychainDenied: return "Claude Code keeps your login in the macOS Keychain. The app needs permission to read it."
        case .unauthorized: return "Your Claude Code login has expired."
        default: return "Usage comes from your Claude Code login."
        }
    }

    private func code(_ s: String) -> Text {
        Text(s).font(.system(size: 11.5, design: .monospaced)).foregroundColor(.primary)
    }

    private var steps: [Text] {
        if model.status == .keychainDenied {
            return [
                Text("Click ") + Text("Try again").bold() + Text(" below"),
                Text("Enter your Mac password if asked"),
                Text("Choose ") + Text("Always Allow").bold(),
            ]
        }
        if !model.cliInstalled {
            return [
                Text("Install Claude Code (button below)"),
                Text("Run ") + code("claude") + Text(", then type ") + code("/login"),
                Text("Finish in the browser — this updates itself"),
            ]
        }
        return [
            Text("Run ") + code("claude") + Text(" in Terminal"),
            Text("Type ") + code("/login") + Text(" and choose your plan"),
            Text("Finish in the browser — this updates itself"),
        ]
    }

    private var buttonTitle: String {
        if model.status == .keychainDenied { return "Try again" }
        if !model.cliInstalled { return "Install Claude Code" }
        return model.status == .unauthorized ? "Open Terminal to sign in" : "Open Terminal"
    }

    private var buttonSymbol: String {
        if model.status == .keychainDenied { return "key.fill" }
        return model.cliInstalled ? "terminal" : "arrow.down.circle"
    }
}
