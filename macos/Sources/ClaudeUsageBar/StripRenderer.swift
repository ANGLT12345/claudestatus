import AppKit

/// Draws the menu bar item: two rows (5h, 7d) in one of four sizes, sized to the menu bar's height.
enum StripRenderer {
    static func image(size: StripSize, provider: Provider, data: UsageSnapshot?, status: String, stale: Bool, dark: Bool, height: CGFloat, now: Date) -> NSImage {
        let width: CGFloat
        switch size {
        case .full: width = 122
        case .compact: width = 72
        case .mini: width = 46
        case .micro: width = 18
        }
        let fg: NSColor = dark ? .white : .black

        let image = NSImage(size: NSSize(width: width, height: height), flipped: true) { _ in
            let cy = height / 2, gap: CGFloat = 5.2
            if size == .micro {
                micro(data, fg: fg, dark: dark, stale: stale, height: height)
                return true
            }
            guard let data else {
                // No numbers yet: say why instead of showing empty meters.
                let lines = size == .full ? ("\(provider.name) usage", status) : (provider.name, status.components(separatedBy: " ").first ?? status)
                text(lines.0, x: 2, cy: cy - gap, color: fg, weight: .semibold)
                text(lines.1, x: 2, cy: cy + gap, color: fg.withAlphaComponent(0.6), weight: .regular)
                return true
            }
            // Row labels come from the windows' lengths, so they stay right if a provider's windows differ.
            row(Fmt.label(data.fiveHour, fallback: "5h"), data.fiveHour, cy: cy - gap, size: size, fg: fg, dark: dark, stale: stale, now: now)
            row(Fmt.label(data.sevenDay, fallback: "7d"), data.sevenDay, cy: cy + gap, size: size, fg: fg, dark: dark, stale: stale, now: now)
            return true
        }
        image.isTemplate = false
        return image
    }

    private static func font(_ weight: NSFont.Weight) -> NSFont {
        NSFont.monospacedDigitSystemFont(ofSize: 9, weight: weight)
    }

    @discardableResult
    private static func text(_ s: String, x: CGFloat, cy: CGFloat, color: NSColor, weight: NSFont.Weight, alignRight: Bool = false) -> CGFloat {
        let attrs: [NSAttributedString.Key: Any] = [.font: font(weight), .foregroundColor: color]
        let size = (s as NSString).size(withAttributes: attrs)
        let px = alignRight ? x - size.width : x
        (s as NSString).draw(at: NSPoint(x: px, y: cy - size.height / 2), withAttributes: attrs)
        return size.width
    }

    private static func color(_ w: UsageWindow?, fg: NSColor, dark: Bool, stale: Bool) -> NSColor {
        guard let w else { return fg.withAlphaComponent(0.4) }
        let c = Status.color(w.percent, dark: dark)
        return stale ? c.blended(withFraction: 0.6, of: fg.withAlphaComponent(0.5)) ?? c : c
    }

    private static func pct(_ w: UsageWindow?) -> String {
        w.map { "\(Int($0.percent.rounded()))%" } ?? "–"
    }

    private static func row(_ label: String, _ w: UsageWindow?, cy: CGFloat, size: StripSize, fg: NSColor, dark: Bool, stale: Bool, now: Date) {
        let dim = fg.withAlphaComponent(0.6)
        let off = fg.withAlphaComponent(0.18)
        let on = color(w, fg: fg, dark: dark, stale: stale)
        text(label, x: 2, cy: cy, color: dim, weight: .semibold)
        var x: CGFloat = 15

        switch size {
        case .full:
            // Segmented bar, like the Windows version.
            let segs = 8, sw: CGFloat = 3.5, sg: CGFloat = 1.5, sh: CGFloat = 6
            let filled = (w?.percent ?? 0) / 100 * Double(segs)
            for i in 0..<segs {
                let r = NSRect(x: x + CGFloat(i) * (sw + sg), y: cy - sh / 2, width: sw, height: sh)
                off.setFill()
                NSBezierPath(roundedRect: r, xRadius: 1, yRadius: 1).fill()
                let f = min(max(filled - Double(i), 0), 1)
                if f > 0.02 {
                    let fh = sh * CGFloat(f)
                    on.setFill()
                    NSBezierPath(roundedRect: NSRect(x: r.minX, y: r.maxY - fh, width: sw, height: fh), xRadius: 1, yRadius: 1).fill()
                }
            }
            x += CGFloat(segs) * (sw + sg) - sg + 4
            text(pct(w), x: x + 24, cy: cy, color: stale ? dim : fg, weight: .semibold, alignRight: true)
            x += 28
            if let reset = w?.resetsAt {
                text(Fmt.short(reset.timeIntervalSince(now)), x: x, cy: cy, color: dim, weight: .regular)
            }
        case .compact:
            let track = NSRect(x: x, y: cy - 2, width: 24, height: 4)
            off.setFill()
            NSBezierPath(roundedRect: track, xRadius: 2, yRadius: 2).fill()
            if let w, w.percent > 0 {
                on.setFill()
                let fw = max(track.height, track.width * CGFloat(w.percent / 100))
                NSBezierPath(roundedRect: NSRect(x: track.minX, y: track.minY, width: fw, height: track.height), xRadius: 2, yRadius: 2).fill()
            }
            text(pct(w), x: x + 24 + 30, cy: cy, color: stale ? dim : fg, weight: .semibold, alignRight: true)
        case .mini:
            // Only a number, so it carries the status colour.
            text(pct(w), x: x + 29, cy: cy, color: w == nil ? dim : on, weight: .bold, alignRight: true)
        case .micro:
            break
        }
    }

    private static func micro(_ d: UsageSnapshot?, fg: NSColor, dark: Bool, stale: Bool, height: CGFloat) {
        let h = min(15, height - 6), top = (height - h) / 2
        var x: CGFloat = 3
        for w in [d?.fiveHour, d?.sevenDay] {
            let track = NSRect(x: x, y: top, width: 4.5, height: h)
            fg.withAlphaComponent(0.18).setFill()
            NSBezierPath(roundedRect: track, xRadius: 2.25, yRadius: 2.25).fill()
            if let w, w.percent > 0 {
                let fh = max(track.width, h * CGFloat(w.percent / 100))
                color(w, fg: fg, dark: dark, stale: stale).setFill()
                NSBezierPath(roundedRect: NSRect(x: x, y: track.maxY - fh, width: track.width, height: fh), xRadius: 2.25, yRadius: 2.25).fill()
            }
            x += 7.5
        }
    }
}
