import Foundation
import CoreGraphics

// Все координаты — глобальные точки CoreGraphics: (0,0) в левом верхнем углу главного экрана, y вниз.

final class Monitor {
    var frame: CGRect
    var menuBar: Double
    init(frame: CGRect, menuBar: Double) { self.frame = frame; self.menuBar = menuBar }
    func contains(_ x: Double, _ y: Double) -> Bool {
        x >= Double(frame.minX) && x < Double(frame.maxX) && y >= Double(frame.minY) && y < Double(frame.maxY)
    }
}

final class Flake {
    var x = 0.0, y = 0.0, prevY = 0.0, vx = 0.0, vy = 0.0, r = 0.0
    var depth = 0.0, phase = 0.0, alpha = 0.0, life = 0.0
    var mon = 0
}

/// Верхняя кромка окна или Dock, на которой копится снег.
final class Surface {
    let key: Int
    var rect: CGRect
    let isDock: Bool
    let cell = 3.0
    var h: [Double] = []
    var vis: [Bool] = []
    var spillL = 0.0, spillR = 0.0

    init(key: Int, rect: CGRect, isDock: Bool) {
        self.key = key; self.rect = rect; self.isDock = isDock
        alloc()
    }

    var cols: Int { h.count }
    var top: Double { Double(rect.minY) }
    var left: Double { Double(rect.minX) }
    var right: Double { Double(rect.maxX) }
    func colX(_ c: Int) -> Double { left + (Double(c) + 0.5) * cell }
    func colAt(_ x: Double) -> Int { Int(floor((x - left) / cell)) }
    var total: Double { h.reduce(0, +) }

    func alloc() {
        let n = max(1, Int(ceil(Double(rect.width) / cell)))
        h = Array(repeating: 0, count: n)
        vis = Array(repeating: false, count: n)
    }

    func resample() {
        let old = h
        let n = max(1, Int(ceil(Double(rect.width) / cell)))
        h = Array(repeating: 0, count: n)
        vis = Array(repeating: false, count: n)
        if old.isEmpty { return }
        for i in 0..<n { h[i] = old[min(old.count - 1, i * old.count / n)] }
    }
}

final class Snowman {
    let key: Int
    var offX: Double
    var balls = 1
    var grow: [Double] = [0, 0, 0]
    var decor = 0.0
    var wobble = 0.0
    init(key: Int, offX: Double) { self.key = key; self.offX = offX }
}

final class Plow {
    var active = false, dumped = false
    var key = 0
    var x = 0.0
    var pushed = 0.0
    var nextCol = 0
}

struct WinItem {
    var key: Int
    var rect: CGRect
    var normal: Bool
    var dock: Bool
}

final class World {
    let s: AppSettings
    var monitors: [Monitor] = []
    var flakes: [Flake] = []
    var debris: [Flake] = []
    var surfaces: [Surface] = []
    var byKey: [Int: Surface] = [:]
    var snowmen: [Snowman] = []
    let plow = Plow()
    var time = 0.0
    var plowTimer = 0.0
    var paused = false
    var sculptMode = false
    var cursor = CGPoint.zero
    var cursorOnSnow = false
    var dockRect: CGRect?

    static let R0 = 10.0, R1 = 7.4, R2 = 5.4
    static let dockKey = -1

    init(settings: AppSettings) { s = settings }

    func rnd() -> Double { Double.random(in: 0..<1) }

    // MARK: снежинки

    func setMonitors(_ m: [Monitor]) {
        monitors = m
        flakes.removeAll()
        debris.removeAll()
        ensureFlakeCount()
    }

    func targetCount(_ m: Monitor) -> Int {
        let area = Double(m.frame.width * m.frame.height) / (1440.0 * 900.0)
        return Int(min(1400, max(0, s.intensity / 100 * 380 * area)))
    }

    func ensureFlakeCount() {
        flakes.removeAll { $0.mon >= monitors.count }
        for (i, m) in monitors.enumerated() {
            let have = flakes.filter { $0.mon == i }.count
            let target = targetCount(m)
            if have < target {
                for _ in have..<target {
                    let f = Flake()
                    spawn(f, i, anywhere: true)
                    flakes.append(f)
                }
            } else if have > target {
                var remove = have - target
                flakes.removeAll { f in
                    if remove > 0 && f.mon == i { remove -= 1; return true }
                    return false
                }
            }
        }
    }

    func spawn(_ f: Flake, _ m: Int, anywhere: Bool) {
        let fr = monitors[m].frame
        f.mon = m
        f.depth = rnd()
        f.r = (0.9 + rnd() * 1.6) * (0.55 + f.depth * 0.9) * s.flakeSize
        f.x = Double(fr.minX) + rnd() * Double(fr.width)
        f.y = anywhere ? Double(fr.minY) + rnd() * Double(fr.height) : Double(fr.minY) - f.r - rnd() * 30
        f.prevY = f.y
        f.vy = (28 + 62 * f.depth) * (0.8 + 0.4 * rnd())
        f.phase = rnd() * .pi * 2
        f.alpha = 0.35 + 0.6 * f.depth
    }

    func addDebris(_ x: Double, _ y: Double, _ vx: Double, _ vy: Double, _ r: Double) {
        if debris.count > 1500 { return }
        let d = Flake()
        d.x = x; d.y = y; d.prevY = y; d.vx = vx; d.vy = vy; d.r = r; d.alpha = 0.95; d.depth = 1; d.life = 8
        debris.append(d)
    }

    // MARK: шаг

    func update(_ dt: Double) {
        time += dt
        let wind = s.wind / 100 * 90
        let snowing = s.intensity > 0

        for f in flakes {
            guard f.mon < monitors.count else { continue }
            let fr = monitors[f.mon].frame
            let minX = Double(fr.minX), maxX = Double(fr.maxX), w = Double(fr.width), maxY = Double(fr.maxY)
            f.prevY = f.y
            f.y += f.vy * dt
            let sway = sin(time * (0.7 + f.depth * 0.8) + f.phase) * 14 * (0.4 + f.depth)
            f.x += (wind * (0.4 + f.depth) + sway) * dt
            if f.x < minX - 10 { f.x += w + 20 } else if f.x > maxX + 10 { f.x -= w + 20 }
            if f.depth >= 0.45 && tryLand(f.x, f.prevY, f.y, f.r, 1) { spawn(f, f.mon, anywhere: false) }
            else if f.y - f.r > maxY { spawn(f, f.mon, anywhere: false) }
        }

        let vb = monitors.map { Double($0.frame.maxY) }.max() ?? 2000
        var keep: [Flake] = []
        keep.reserveCapacity(debris.count)
        let old = debris
        debris = []
        for d in old {
            d.vy = min(d.vy + 650 * dt, 520)
            d.vx *= 1 - min(1, 1.2 * dt)
            d.prevY = d.y
            d.x += d.vx * dt
            d.y += d.vy * dt
            d.life -= dt
            if d.vy > 0 && tryLand(d.x, d.prevY, d.y, d.r, 0.7) { continue }
            if d.life <= 0 || d.y > vb + 50 { continue }
            keep.append(d)
        }
        debris = keep + debris   // + то, что добавилось во время цикла

        for sf in surfaces { relax(sf, dt, snowing) }
        updatePlow(dt)
        updateSnowmen(dt)
    }

    func isEnabled(_ sf: Surface) -> Bool { sf.isDock ? s.pileOnDock : s.pileOnWindows }
    var maxH: Double { s.maxPile }

    func tryLand(_ x: Double, _ y0: Double, _ y1: Double, _ r: Double, _ mul: Double) -> Bool {
        for sf in surfaces {
            if !isEnabled(sf) || x < sf.left || x >= sf.right { continue }
            let c = sf.colAt(x)
            if c < 0 || c >= sf.cols || !sf.vis[c] { continue }
            let line = sf.top - sf.h[c]
            if y0 < line && y1 >= line {
                deposit(sf, c, r, mul)
                return true
            }
        }
        return false
    }

    func deposit(_ sf: Surface, _ c: Int, _ r: Double, _ mul: Double) {
        let amt = 3.2 * s.accumulation * mul * min(2.2, max(0.5, r / 1.6))
        addH(sf, c, amt * 0.5)
        addH(sf, c - 1, amt * 0.25)
        addH(sf, c + 1, amt * 0.25)
    }

    func addH(_ sf: Surface, _ i: Int, _ a: Double) {
        if i < 0 || i >= sf.cols { return }
        sf.h[i] = min(maxH, sf.h[i] + a)
    }

    /// Снег не держит крутой склон и падает с краёв.
    func relax(_ sf: Surface, _ dt: Double, _ snowing: Bool) {
        let n = sf.cols
        if n < 2 { return }
        if plow.active && plow.key == sf.key { return }
        let slope = sf.cell * 0.85
        var h = sf.h
        for i in 0..<(n - 1) { settle(&h, i, slope) }
        for i in stride(from: n - 2, through: 0, by: -1) { settle(&h, i, slope) }
        for i in 0..<n where h[i] > maxH { h[i] = maxH }

        let edge = slope * 1.3
        if h[0] > edge { sf.spillL += h[0] - edge; h[0] = edge }
        if h[n - 1] > edge { sf.spillR += h[n - 1] - edge; h[n - 1] = edge }
        if !snowing {
            let melt = 0.25 * dt
            for i in 0..<n { h[i] = max(0, h[i] - melt) }
        }
        sf.h = h

        if sf.spillL > 5 {
            if sf.vis[0] { addDebris(sf.left - 1.5, sf.top - edge, -(10 + rnd() * 25), 0, 1.1 + rnd()) }
            sf.spillL = 0
        }
        if sf.spillR > 5 {
            if sf.vis[n - 1] { addDebris(sf.right + 1.5, sf.top - edge, 10 + rnd() * 25, 0, 1.1 + rnd()) }
            sf.spillR = 0
        }
    }

    func settle(_ h: inout [Double], _ a: Int, _ slope: Double) {
        let d = h[a] - h[a + 1]
        if d > slope { let t = (d - slope) * 0.5; h[a] -= t; h[a + 1] += t }
        else if d < -slope { let t = (-d - slope) * 0.5; h[a] += t; h[a + 1] -= t }
    }

    // MARK: окна

    func monitorAt(_ x: Double, _ y: Double) -> Monitor? { monitors.first { $0.contains(x, y) } }

    func coversScreen(_ r: CGRect) -> Bool {
        monitors.contains { r.minX <= $0.frame.minX && r.minY <= $0.frame.minY && r.maxX >= $0.frame.maxX && r.maxY >= $0.frame.maxY }
    }

    /// Перечитать окна: где кромки, что чем перекрыто, кто сдвинулся.
    func scan(ownPID: Int) {
        if monitors.isEmpty { return }
        var items: [WinItem] = []
        if let d = dockRect { items.append(WinItem(key: World.dockKey, rect: d, normal: false, dock: true)) }

        if let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] {
            for w in list {
                let pid = (w[kCGWindowOwnerPID as String] as? NSNumber)?.intValue ?? 0
                if pid == ownPID { continue }
                let owner = w[kCGWindowOwnerName as String] as? String ?? ""
                if owner == "Dock" { continue }    // невидимое окно Dock на весь экран
                let alpha = (w[kCGWindowAlpha as String] as? NSNumber)?.doubleValue ?? 1
                if alpha < 0.05 { continue }
                let layer = (w[kCGWindowLayer as String] as? NSNumber)?.intValue ?? 0
                guard let bAny = w[kCGWindowBounds as String],
                      let r = CGRect(dictionaryRepresentation: bAny as! CFDictionary) else { continue }
                if r.width < 2 || r.height < 2 || coversScreen(r) { continue }
                let num = (w[kCGWindowNumber as String] as? NSNumber)?.intValue ?? 0
                items.append(WinItem(key: num, rect: r, normal: layer == 0, dock: false))
            }
        }

        var next: [Surface] = []
        var present = Set<Int>()
        for it in items { present.insert(it.key) }

        for (i, it) in items.enumerated() {
            let r = it.rect
            guard let mon = monitorAt(Double(r.midX), Double(r.minY) + 1) else { continue }
            let holds: Bool
            if it.dock {
                holds = r.width > r.height
            } else {
                holds = it.normal && r.width >= 120 && r.height >= 60
                    && Double(r.minY) > Double(mon.frame.minY) + mon.menuBar + 4
            }
            if !holds { continue }

            // видимые участки кромки
            var segs: [(Double, Double)] = [(Double(r.minX), Double(r.maxX))]
            let y = Double(r.minY) - 2
            for j in 0..<i where !segs.isEmpty {
                let o = items[j].rect
                if Double(o.minY) <= y && Double(o.maxY) > y { segs = subtract(segs, Double(o.minX), Double(o.maxX)) }
            }

            let sf: Surface
            if let ex = byKey[it.key] {
                sf = ex
                if ex.rect.minX != r.minX || ex.rect.minY != r.minY {
                    shake(ex)                 // окно сдвинули — снег осыпается
                    ex.rect = r
                    ex.alloc()
                } else if ex.rect.maxX != r.maxX {
                    ex.rect = r
                    ex.resample()
                } else {
                    ex.rect = r
                }
            } else {
                sf = Surface(key: it.key, rect: r, isDock: it.dock)
                byKey[it.key] = sf
            }
            for c in 0..<sf.cols {
                let x = sf.colX(c)
                sf.vis[c] = segs.contains { x >= $0.0 && x < $0.1 }
            }
            next.append(sf)
        }

        let keepKeys = Set(next.map { $0.key })
        for (k, sf) in byKey where !keepKeys.contains(k) {
            if present.contains(k) { shake(sf) } else { snowmen.removeAll { $0.key == k } }
            byKey[k] = nil
        }
        surfaces = next
        if plow.active && byKey[plow.key] == nil { plow.active = false }
    }

    func subtract(_ segs: [(Double, Double)], _ a: Double, _ b: Double) -> [(Double, Double)] {
        var out: [(Double, Double)] = []
        for (s0, e0) in segs {
            if b <= s0 || a >= e0 { out.append((s0, e0)); continue }
            if a > s0 { out.append((s0, a)) }
            if b < e0 { out.append((b, e0)) }
        }
        return out
    }

    func shake(_ sf: Surface) {
        var total = 0.0
        for c in 0..<sf.cols where sf.vis[c] && sf.h[c] > 1.2 { total += sf.h[c] / 4 }
        let p = total > 0 ? min(1, 380 / total) : 0
        for c in 0..<sf.cols where sf.vis[c] && sf.h[c] > 1.2 {
            let k = Int(ceil(sf.h[c] / 4))
            for _ in 0..<k where rnd() <= p {
                addDebris(sf.colX(c) + (rnd() - 0.5) * sf.cell, sf.top - rnd() * sf.h[c],
                          (rnd() - 0.5) * 60, -rnd() * 90, 1 + rnd() * 1.5)
            }
        }
        for i in 0..<sf.cols { sf.h[i] = 0 }
        for m in snowmen where m.key == sf.key { crumble(m, sf) }
    }

    func shakeAll() {
        for sf in surfaces { shake(sf) }
        plow.active = false
    }

    // MARK: снегоуборщик

    @discardableResult
    func startPlow() -> Bool {
        if plow.active { return true }
        guard let d = surfaces.first(where: { $0.isDock }) else { return false }
        plow.active = true
        plow.dumped = false
        plow.key = d.key
        plow.x = d.right + 2
        plow.nextCol = d.cols - 1
        plow.pushed = 0
        return true
    }

    var plowSurface: Surface? { plow.active ? byKey[plow.key] : nil }

    func wedgeHeight(_ sf: Surface) -> Double { min(sqrt(plow.pushed * sf.cell / 1.3), 26) }

    func updatePlow(_ dt: Double) {
        plowTimer += dt
        if !plow.active {
            if s.plow && plowTimer >= s.plowInterval * 60 {
                plowTimer = 0
                if let d = surfaces.first(where: { $0.isDock }), d.total / Double(max(1, d.cols)) >= 0.3 * maxH { startPlow() }
            }
            return
        }
        guard let d = plowSurface else { plow.active = false; return }
        plow.x -= 95 * dt
        while plow.nextCol >= 0 && plow.nextCol < d.cols && d.colX(plow.nextCol) >= plow.x {
            plow.pushed += d.h[plow.nextCol]
            d.h[plow.nextCol] = 0
            plow.nextCol -= 1
        }
        if !plow.dumped && plow.x <= d.left {
            let wh = wedgeHeight(d)
            let n = Int(min(160, plow.pushed / 1.5))
            for _ in 0..<max(0, n) {
                addDebris(d.left - rnd() * wh * 2.6, d.top - rnd() * wh, -(20 + rnd() * 70), -rnd() * 50, 1.1 + rnd() * 1.6)
            }
            plow.pushed = 0
            plow.dumped = true
        }
        if plow.x + 44 < d.left { plow.active = false; plowTimer = 0 }
    }

    // MARK: снеговики

    func updateSnowmen(_ dt: Double) {
        snowmen.removeAll { byKey[$0.key] == nil }
        for m in snowmen {
            for b in 0..<m.balls { m.grow[b] = min(1, m.grow[b] + dt * 3.5) }
            if m.balls == 3 && m.grow[2] >= 1 { m.decor = min(1, m.decor + dt * 2) }
            m.wobble = max(0, m.wobble - dt * 1.5)
        }
    }

    static func snowmanHeight(_ m: Snowman) -> Double {
        var h = 2 * R0
        if m.balls > 1 { h += 1.6 * R1 }
        if m.balls > 2 { h += 1.7 * R2 + 7 }
        return h
    }

    func snowmanVisible(_ m: Snowman, _ sf: Surface) -> Bool {
        let c = sf.colAt(sf.left + m.offX)
        return c >= 0 && c < sf.cols && sf.vis[c]
    }

    func hitSnowman(_ x: Double, _ y: Double) -> Snowman? {
        for m in snowmen.reversed() {
            guard let sf = byKey[m.key], snowmanVisible(m, sf) else { continue }
            let cx = sf.left + m.offX, base = sf.top + 1
            if abs(x - cx) <= World.R0 + 3 && y <= base + 3 && y >= base - World.snowmanHeight(m) { return m }
        }
        return nil
    }

    func hitPile(_ x: Double, _ y: Double) -> (Surface, Int)? {
        for sf in surfaces {
            if !isEnabled(sf) || x < sf.left || x >= sf.right { continue }
            let c = sf.colAt(x)
            if c < 0 || c >= sf.cols || !sf.vis[c] { continue }
            if sf.h[c] > 0.8 && y >= sf.top - sf.h[c] - 6 && y <= sf.top + 5 { return (sf, c) }
        }
        return nil
    }

    func hitAnything(_ x: Double, _ y: Double) -> Bool { hitSnowman(x, y) != nil || hitPile(x, y) != nil }

    /// Клик в режиме лепки. true — клик наш, в окна не пропускаем.
    func sculpt(_ x: Double, _ y: Double, right: Bool) -> Bool {
        if let m = hitSnowman(x, y) {
            let sf = byKey[m.key]
            if right { crumble(m, sf) }
            else if m.balls < 3 { addBall(m, sf) }
            else { m.wobble = 1 }
            return true
        }
        if right { return false }
        guard let hit = hitPile(x, y) else { return false }
        let sf = hit.0, col = hit.1

        if let other = snowmen.first(where: { $0.key == sf.key && $0.balls < 3 && abs(sf.left + $0.offX - x) < World.R0 * 2.4 }) {
            addBall(other, sf)
            return true
        }
        if snowmen.count >= 40 { return true }
        let r0 = World.R0
        let c0 = max(0, sf.colAt(x - r0)), c1 = min(sf.cols - 1, sf.colAt(x + r0))
        var sum = 0.0, cnt = 0
        if c0 <= c1 { for c in c0...c1 { sum += sf.h[c]; cnt += 1 } }
        if cnt == 0 || sum / Double(cnt) < 2 {
            for _ in 0..<8 { addDebris(x, sf.top - sf.h[col], (rnd() - 0.5) * 80, -(40 + rnd() * 60), 0.8 + rnd()) }
            return true
        }
        takeSnow(sf, x, r0 * 1.5, 0.3)
        if Double(sf.rect.width) < 2 * r0 { return true }
        snowmen.append(Snowman(key: sf.key, offX: min(max(x - sf.left, r0), Double(sf.rect.width) - r0)))
        return true
    }

    func addBall(_ m: Snowman, _ sf: Surface?) {
        if m.balls >= 3 { return }
        m.grow[m.balls] = 0
        m.balls += 1
        if let sf { takeSnow(sf, sf.left + m.offX, World.R0 * 2.5, 0.6) }
    }

    func takeSnow(_ sf: Surface, _ x: Double, _ radius: Double, _ keep: Double) {
        let c0 = max(0, sf.colAt(x - radius)), c1 = min(sf.cols - 1, sf.colAt(x + radius))
        if c0 > c1 { return }
        for c in c0...c1 { sf.h[c] *= keep }
    }

    func crumble(_ m: Snowman, _ sf: Surface?) {
        snowmen.removeAll { $0 === m }
        guard let sf else { return }
        let cx = sf.left + m.offX, h = World.snowmanHeight(m)
        for _ in 0..<70 {
            addDebris(cx + (rnd() - 0.5) * 2 * World.R0, sf.top - rnd() * h, (rnd() - 0.5) * 120, -rnd() * 120, 1.1 + rnd() * 1.8)
        }
    }
}
