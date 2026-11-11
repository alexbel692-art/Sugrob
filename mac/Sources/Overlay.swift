import AppKit

/// Прозрачное окно на весь экран поверх всего, мышь проходит насквозь.
final class OverlayWindow: NSWindow {
    let view: SnowView

    init(screen: NSScreen, world: World, index: Int) {
        view = SnowView(world: world, index: index)
        super.init(contentRect: screen.frame, styleMask: .borderless, backing: .buffered, defer: false)
        setFrame(screen.frame, display: false)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        ignoresMouseEvents = true
        isReleasedWhenClosed = false
        level = .statusBar                       // выше Dock и обычных окон
        // без .fullScreenAuxiliary — в полноэкранных приложениях снега нет
        collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        contentView = view
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class SnowView: NSView {
    let world: World
    let index: Int

    init(world: World, index: Int) {
        self.world = world
        self.index = index
        super.init(frame: .zero)
        wantsLayer = true
        layerContentsRedrawPolicy = .onSetNeedsDisplay
    }

    required init?(coder: NSCoder) { fatalError() }

    override var isFlipped: Bool { true }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func draw(_ dirtyRect: NSRect) {
        guard !world.paused, index < world.monitors.count,
              let ctx = NSGraphicsContext.current?.cgContext else { return }
        let mon = world.monitors[index]
        ctx.clear(bounds)
        ctx.saveGState()
        ctx.translateBy(x: -mon.frame.minX, y: -mon.frame.minY)   // рисуем в глобальных координатах
        Renderer.draw(ctx, world, index)
        ctx.restoreGState()

        if world.sculptMode && mon.contains(Double(world.cursor.x), Double(world.cursor.y)) {
            Renderer.drawHint(in: bounds,
                "☃  Режим лепки:  клик по сугробу — снежный ком (3 кома = снеговик)  ·  правый клик по снеговику — разрушить  ·  Esc — выход")
        }
    }
}

enum Renderer {
    static let space = CGColorSpaceCreateDeviceRGB()
    static func rgb(_ r: Int, _ g: Int, _ b: Int, _ a: Double = 1) -> CGColor {
        CGColor(red: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: CGFloat(a))
    }

    static let pileGradient = CGGradient(colorsSpace: space,
        colors: [rgb(255, 255, 255), rgb(222, 233, 246)] as CFArray, locations: [0, 1])!
    static let ballGradient = CGGradient(colorsSpace: space,
        colors: [rgb(255, 255, 255), rgb(246, 249, 253), rgb(204, 218, 235)] as CFArray, locations: [0, 0.45, 1])!
    static let edge = rgb(160, 185, 215, 0.6)
    static let shadow = rgb(10, 20, 45, 0.24)
    static let ballEdge = rgb(150, 170, 195, 0.65)
    static let track = rgb(43, 47, 58), wheel = rgb(138, 147, 166)
    static let yellow = rgb(255, 197, 49), yellowDark = rgb(201, 143, 0)
    static let glass = rgb(110, 198, 255), beaconOn = rgb(255, 122, 26), beaconOff = rgb(150, 70, 20)
    static let blade = rgb(196, 205, 218), bladeDark = rgb(110, 122, 140), arm = rgb(90, 96, 110)
    static let coal = rgb(28, 28, 32), carrot = rgb(255, 138, 31), scarf = rgb(222, 38, 48), scarfDark = rgb(160, 20, 30)
    static let twig = rgb(107, 68, 35), hat = rgb(24, 24, 30), white = rgb(255, 255, 255)

    static func flake(_ ctx: CGContext, _ f: Flake) {
        ctx.setFillColor(gray: 1, alpha: CGFloat(f.alpha))
        let r = f.r
        ctx.fillEllipse(in: CGRect(x: f.x - r, y: f.y - r, width: r * 2, height: r * 2))
        if r > 2.3 {   // мягкий ореол у крупных
            ctx.setFillColor(gray: 1, alpha: CGFloat(f.alpha * 0.25))
            let r2 = r * 1.5
            ctx.fillEllipse(in: CGRect(x: f.x - r2, y: f.y - r2, width: r2 * 2, height: r2 * 2))
        }
    }

    static func draw(_ ctx: CGContext, _ w: World, _ idx: Int) {
        let mon = w.monitors[idx]
        let b = mon.frame

        for f in w.flakes where f.mon == idx && f.depth < 0.45 { flake(ctx, f) }

        for sf in w.surfaces where w.isEnabled(sf) {
            if sf.rect.maxX < b.minX || sf.rect.minX > b.maxX || sf.rect.minY < b.minY - 60 || sf.rect.minY > b.maxY + 60 { continue }
            pile(ctx, sf)
        }

        if let ps = w.plowSurface, ps.rect.minY >= b.minY - 60, ps.rect.minY <= b.maxY + 60 { plow(ctx, w, ps) }

        for m in w.snowmen {
            guard let sf = w.byKey[m.key], w.snowmanVisible(m, sf) else { continue }
            let x = sf.left + m.offX
            if x < Double(b.minX) - 60 || x > Double(b.maxX) + 60 { continue }
            snowman(ctx, w, m, x, sf.top + 1)
        }

        for d in w.debris where mon.contains(d.x, d.y) { flake(ctx, d) }
        for f in w.flakes where f.mon == idx && f.depth >= 0.45 { flake(ctx, f) }

        if w.sculptMode && w.cursorOnSnow && mon.contains(Double(w.cursor.x), Double(w.cursor.y)) {
            ctx.setStrokeColor(white)
            ctx.setLineWidth(1.6)
            ctx.strokeEllipse(in: CGRect(x: w.cursor.x - 10, y: w.cursor.y - 10, width: 20, height: 20))
        }
    }

    static func pile(_ ctx: CGContext, _ sf: Surface) {
        let n = sf.cols
        let top = sf.top, baseY = top + 1.2, minH = 0.35
        var i = 0
        while i < n {
            while i < n && !(sf.vis[i] && sf.h[i] > minH) { i += 1 }
            if i >= n { break }
            let start = i
            while i < n && sf.vis[i] && sf.h[i] > minH { i += 1 }
            let end = i - 1
            let x0 = sf.left + Double(start) * sf.cell
            let x1 = min(sf.right, sf.left + Double(end + 1) * sf.cell)

            var pts: [CGPoint] = [CGPoint(x: x0, y: top - sf.h[start] * 0.45)]
            for c in start...end { pts.append(CGPoint(x: sf.colX(c), y: top - sf.h[c])) }
            pts.append(CGPoint(x: x1, y: top - sf.h[end] * 0.45))

            ctx.setStrokeColor(shadow)
            ctx.setLineWidth(1.4)
            ctx.strokeLineSegments(between: [CGPoint(x: x0 + 1, y: baseY + 0.7), CGPoint(x: x1 - 1, y: baseY + 0.7)])

            let path = CGMutablePath()
            path.move(to: CGPoint(x: x0, y: baseY))
            path.addLines(between: pts)
            path.addLine(to: CGPoint(x: x1, y: baseY))
            path.closeSubpath()

            var hMax = 0.0
            for c in start...end { hMax = max(hMax, sf.h[c]) }
            ctx.saveGState()
            ctx.addPath(path)
            ctx.clip()
            ctx.drawLinearGradient(pileGradient, start: CGPoint(x: 0, y: top - hMax), end: CGPoint(x: 0, y: baseY), options: [])
            ctx.restoreGState()

            let line = CGMutablePath()
            line.addLines(between: pts)
            ctx.setStrokeColor(edge)
            ctx.setLineWidth(0.9)
            ctx.addPath(line)
            ctx.strokePath()
        }
    }

    static func rr(_ ctx: CGContext, _ r: CGRect, _ rad: Double, _ fill: CGColor, _ stroke: CGColor? = nil, _ lw: Double = 0.9) {
        let p = CGPath(roundedRect: r, cornerWidth: rad, cornerHeight: rad, transform: nil)
        ctx.setFillColor(fill)
        ctx.addPath(p)
        ctx.fillPath()
        if let stroke {
            ctx.setStrokeColor(stroke)
            ctx.setLineWidth(lw)
            ctx.addPath(p)
            ctx.strokePath()
        }
    }

    static func poly(_ ctx: CGContext, _ pts: [CGPoint], _ fill: CGColor, _ stroke: CGColor? = nil, _ lw: Double = 0.8) {
        let p = CGMutablePath()
        p.addLines(between: pts)
        p.closeSubpath()
        ctx.setFillColor(fill)
        ctx.addPath(p)
        ctx.fillPath()
        if let stroke {
            ctx.setStrokeColor(stroke)
            ctx.setLineWidth(lw)
            ctx.addPath(p)
            ctx.strokePath()
        }
    }

    static func line(_ ctx: CGContext, _ a: CGPoint, _ b: CGPoint, _ c: CGColor, _ lw: Double) {
        ctx.setStrokeColor(c)
        ctx.setLineWidth(lw)
        ctx.setLineCap(.round)
        ctx.strokeLineSegments(between: [a, b])
    }

    static func dot(_ ctx: CGContext, _ x: Double, _ y: Double, _ r: Double, _ c: CGColor) {
        ctx.setFillColor(c)
        ctx.fillEllipse(in: CGRect(x: x - r, y: y - r, width: r * 2, height: r * 2))
    }

    static func plow(_ ctx: CGContext, _ w: World, _ sf: Surface) {
        let X = w.plow.x
        let bse = sf.top + 0.5 + sin(w.time * 22) * 0.35

        let hW = w.wedgeHeight(sf)
        if hW > 1 {
            let lW = hW * 2.6
            let p = CGMutablePath()
            p.move(to: CGPoint(x: X + 1.5, y: bse + 0.8))
            p.addLine(to: CGPoint(x: X + 1.5, y: bse - hW))
            p.addCurve(to: CGPoint(x: X - lW, y: bse + 0.8),
                       control1: CGPoint(x: X - lW * 0.25, y: bse - hW * 1.05),
                       control2: CGPoint(x: X - lW * 0.7, y: bse - hW * 0.3))
            p.closeSubpath()
            ctx.saveGState()
            ctx.addPath(p)
            ctx.clip()
            ctx.drawLinearGradient(pileGradient, start: CGPoint(x: 0, y: bse - hW), end: CGPoint(x: 0, y: bse), options: [])
            ctx.restoreGState()
            ctx.setStrokeColor(edge)
            ctx.setLineWidth(0.9)
            ctx.addPath(p)
            ctx.strokePath()
        }

        rr(ctx, CGRect(x: X + 4, y: bse - 7, width: 30, height: 7), 3.5, track)
        for k in 0..<4 { dot(ctx, X + 8 + Double(k) * 7.3, bse - 3.5, 2.3, wheel) }
        rr(ctx, CGRect(x: X + 7, y: bse - 14, width: 26, height: 7.5), 1.5, yellow, yellowDark)
        rr(ctx, CGRect(x: X + 17, y: bse - 24, width: 13, height: 10.5), 1.5, yellow, yellowDark)
        rr(ctx, CGRect(x: X + 19, y: bse - 22, width: 9, height: 6), 1, glass)
        rr(ctx, CGRect(x: X + 21.5, y: bse - 27, width: 4, height: 3), 1, sin(w.time * 9) > 0 ? beaconOn : beaconOff)
        rr(ctx, CGRect(x: X + 16.5, y: bse - 25.4, width: 14, height: 1.8), 0.9, white)
        line(ctx, CGPoint(x: X + 2.5, y: bse - 7.5), CGPoint(x: X + 8, y: bse - 10), arm, 1.8)
        poly(ctx, [CGPoint(x: X - 0.5, y: bse), CGPoint(x: X + 3, y: bse),
                   CGPoint(x: X + 4.5, y: bse - 13), CGPoint(x: X + 0.5, y: bse - 14.5)], blade, bladeDark)
    }

    static func ease(_ t: Double) -> Double { 1 - pow(1 - min(1, max(0, t)), 3) }

    static func ball(_ ctx: CGContext, _ x: Double, _ y: Double, _ r: Double) {
        if r <= 0.1 { return }
        let rect = CGRect(x: x - r, y: y - r, width: r * 2, height: r * 2)
        ctx.saveGState()
        ctx.addEllipse(in: rect)
        ctx.clip()
        ctx.drawRadialGradient(ballGradient,
                               startCenter: CGPoint(x: x - r * 0.3, y: y - r * 0.4), startRadius: 0,
                               endCenter: CGPoint(x: x - r * 0.2, y: y - r * 0.3), endRadius: r * 1.5, options: [])
        ctx.restoreGState()
        ctx.setStrokeColor(ballEdge)
        ctx.setLineWidth(0.6)
        ctx.strokeEllipse(in: rect)
    }

    static func snowman(_ ctx: CGContext, _ w: World, _ m: Snowman, _ x: Double, _ bse: Double) {
        let r0 = World.R0 * ease(m.grow[0])
        let r1 = m.balls > 1 ? World.R1 * ease(m.grow[1]) : 0
        let r2 = m.balls > 2 ? World.R2 * ease(m.grow[2]) : 0
        let c0 = bse - r0 * 0.92
        let c1 = c0 - r0 * 0.8 - r1 * 0.82
        let c2 = c1 - r1 * 0.8 - r2 * 0.85

        ctx.saveGState()
        if m.wobble > 0 {
            ctx.translateBy(x: x, y: bse)
            ctx.rotate(by: sin(w.time * 25) * m.wobble * 7 * .pi / 180)
            ctx.translateBy(x: -x, y: -bse)
        }

        let a = m.decor
        if a > 0 {
            ctx.setAlpha(a)
            line(ctx, CGPoint(x: x - r1 * 0.7, y: c1 - r1 * 0.1), CGPoint(x: x - r1 - 7, y: c1 - 5), twig, 1.1)
            line(ctx, CGPoint(x: x - r1 - 4, y: c1 - 3.2), CGPoint(x: x - r1 - 5, y: c1 - 6.5), twig, 1.1)
            line(ctx, CGPoint(x: x + r1 * 0.7, y: c1 - r1 * 0.1), CGPoint(x: x + r1 + 7, y: c1 - 6), twig, 1.1)
            line(ctx, CGPoint(x: x + r1 + 4, y: c1 - 3.8), CGPoint(x: x + r1 + 6.5, y: c1 - 2.5), twig, 1.1)
            ctx.setAlpha(1)
        }

        ball(ctx, x, c0, r0)
        ball(ctx, x, c1, r1)
        ball(ctx, x, c2, r2)

        if a > 0 {
            ctx.setAlpha(a)
            dot(ctx, x, c1 - r1 * 0.2, 0.9, coal)
            dot(ctx, x, c1 + r1 * 0.3, 0.9, coal)
            dot(ctx, x, c0 - r0 * 0.25, 1.0, coal)
            let sy = c1 - r1 * 0.78
            rr(ctx, CGRect(x: x - r2 * 1.05, y: sy - 1.3, width: r2 * 2.1, height: 2.6), 1.2, scarf)
            poly(ctx, [CGPoint(x: x + r2 * 0.3, y: sy), CGPoint(x: x + r2 * 0.3 + 2.4, y: sy),
                       CGPoint(x: x + r2 * 0.9 + 2.6, y: sy + 6.5), CGPoint(x: x + r2 * 0.75, y: sy + 6.5)], scarf)
            line(ctx, CGPoint(x: x + r2 * 0.55, y: sy + 4.5), CGPoint(x: x + r2 * 0.55 + 2.8, y: sy + 4.5), scarfDark, 0.5)
            dot(ctx, x - 1.9, c2 - 1.3, 0.85, coal)
            dot(ctx, x + 1.9, c2 - 1.3, 0.85, coal)
            poly(ctx, [CGPoint(x: x + 0.3, y: c2 + 0.1), CGPoint(x: x + 6, y: c2 + 1.1), CGPoint(x: x + 0.3, y: c2 + 1.9)], carrot)
            for k in -1...1 { dot(ctx, x + Double(k) * 1.5, c2 + 3.1 - Double(abs(k)) * 0.5, 0.45, coal) }
            let hy = c2 - r2 * 0.8
            rr(ctx, CGRect(x: x - 5, y: hy - 1.2, width: 10, height: 1.6), 0.6, hat)
            ctx.setFillColor(hat)
            ctx.fill(CGRect(x: x - 3.4, y: hy - 7, width: 6.8, height: 6))
            ctx.setFillColor(scarf)
            ctx.fill(CGRect(x: x - 3.4, y: hy - 2.6, width: 6.8, height: 1.2))
            ctx.setAlpha(1)
        }
        ctx.restoreGState()
    }

    static func drawHint(in bounds: NSRect, _ text: String) {
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 13, weight: .semibold),
            .foregroundColor: NSColor.white
        ]
        let str = NSAttributedString(string: text, attributes: attrs)
        let maxW = bounds.width - 80
        let size = str.boundingRect(with: NSSize(width: maxW, height: 200), options: [.usesLineFragmentOrigin]).size
        let w = size.width + 28, h = size.height + 14
        let r = NSRect(x: (bounds.width - w) / 2, y: 44, width: w, height: h)
        NSColor(calibratedRed: 18 / 255, green: 28 / 255, blue: 48 / 255, alpha: 0.82).setFill()
        NSBezierPath(roundedRect: r, xRadius: 10, yRadius: 10).fill()
        str.draw(with: NSRect(x: r.minX + 14, y: r.minY + 7, width: size.width + 1, height: size.height + 1),
                 options: [.usesLineFragmentOrigin])
    }
}
