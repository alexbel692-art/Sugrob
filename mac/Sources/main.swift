import AppKit
import SwiftUI
import Carbon

final class AppDelegate: NSObject, NSApplicationDelegate {
    let settings = AppSettings()
    lazy var world = World(settings: settings)
    var overlays: [OverlayWindow] = []
    var statusItem: NSStatusItem!
    var pauseItem: NSMenuItem!
    var sculptItem: NSMenuItem!
    var hotKey: HotKey?
    var tap: SculptTap?
    var timer: Timer?
    var settingsWindow: NSWindow?
    var last = CACurrentMediaTime()
    var scanAcc = 0.0, dockAcc = 10.0, sculptIdle = 0.0
    let pid = Int(ProcessInfo.processInfo.processIdentifier)

    func applicationDidFinishLaunching(_ note: Notification) {
        settings.onChange = { [weak self] in self?.settingsChanged() }
        buildOverlays()
        buildMenu()
        hotKey = HotKey(keyCode: UInt32(kVK_ANSI_S), modifiers: UInt32(controlKey | optionKey)) { [weak self] in
            self?.toggleSculpt()
        }
        NotificationCenter.default.addObserver(self, selector: #selector(screensChanged),
                                               name: NSApplication.didChangeScreenParametersNotification, object: nil)
        startTimer()

        if settings.firstRun {
            settings.firstRun = false
            if !Access.trusted { Access.prompt() }   // для снега на Dock и лепки снеговиков
        }
    }

    // MARK: экраны

    @objc func screensChanged() {
        DispatchQueue.main.async { self.buildOverlays() }
    }

    func buildOverlays() {
        overlays.forEach { $0.orderOut(nil) }
        overlays.removeAll()
        guard let primary = NSScreen.screens.first else { return }
        let ph = primary.frame.maxY
        var mons: [Monitor] = []
        for scr in NSScreen.screens {
            let f = scr.frame
            let cg = CGRect(x: f.minX, y: ph - f.maxY, width: f.width, height: f.height)
            mons.append(Monitor(frame: cg, menuBar: Double(f.maxY - scr.visibleFrame.maxY)))
        }
        world.setMonitors(mons)
        for (i, scr) in NSScreen.screens.enumerated() {
            let w = OverlayWindow(screen: scr, world: world, index: i)
            w.orderFrontRegardless()
            overlays.append(w)
        }
        world.scan(ownPID: pid)
    }

    // MARK: меню в строке меню

    func buildMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let b = statusItem.button {
            let img = NSImage(systemSymbolName: "snowflake", accessibilityDescription: "Сугроб")
            img?.isTemplate = true
            b.image = img
            b.toolTip = "Сугроб — снег на рабочем столе"
        }
        let menu = NSMenu()
        sculptItem = NSMenuItem(title: "Слепить снеговика", action: #selector(toggleSculpt), keyEquivalent: "s")
        sculptItem.keyEquivalentModifierMask = [.control, .option]
        menu.addItem(sculptItem)
        menu.addItem(NSMenuItem(title: "Вызвать снегоуборщик", action: #selector(callPlow), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Стряхнуть весь снег", action: #selector(shakeAll), keyEquivalent: ""))
        menu.addItem(.separator())
        pauseItem = NSMenuItem(title: "Пауза", action: #selector(togglePause), keyEquivalent: "")
        menu.addItem(pauseItem)
        menu.addItem(NSMenuItem(title: "Настройки…", action: #selector(showSettings), keyEquivalent: ","))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Выйти из Сугроба", action: #selector(quit), keyEquivalent: "q"))
        for item in menu.items { item.target = self }
        statusItem.menu = menu
    }

    // MARK: кадр

    func startTimer() {
        timer?.invalidate()
        let t = Timer(timeInterval: settings.fps60 ? 1.0 / 60 : 1.0 / 30, repeats: true) { [weak self] _ in self?.frame() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
        last = CACurrentMediaTime()
    }

    func frame() {
        let now = CACurrentMediaTime()
        let dt = min(0.1, now - last)
        last = now

        dockAcc += dt
        if dockAcc >= 1.5 { dockAcc = 0; world.dockRect = Access.dockFrame() }
        scanAcc += dt
        if scanAcc >= 0.15 { scanAcc = 0; world.scan(ownPID: pid) }

        if world.sculptMode {
            let p = NSEvent.mouseLocation
            let ph = NSScreen.screens.first?.frame.maxY ?? 0
            world.cursor = CGPoint(x: p.x, y: ph - p.y)
            world.cursorOnSnow = world.hitAnything(Double(world.cursor.x), Double(world.cursor.y))
            sculptIdle += dt
            if sculptIdle > 120 { setSculpt(false) }
        }

        world.update(dt)
        for o in overlays { o.view.needsDisplay = true }
    }

    func settingsChanged() {
        world.ensureFlakeCount()
        let want = settings.fps60 ? 1.0 / 60 : 1.0 / 30
        if let t = timer, abs(t.timeInterval - want) > 0.001, !world.paused { startTimer() }
    }

    // MARK: команды

    @objc func toggleSculpt() { setSculpt(!world.sculptMode) }

    func setSculpt(_ on: Bool) {
        if on == world.sculptMode { return }
        if on {
            if world.paused { togglePause() }
            let t = SculptTap(onDown: { [weak self] p, right in
                guard let self, self.world.sculptMode else { return false }
                let mine = self.world.sculpt(Double(p.x), Double(p.y), right: right)
                if mine { self.sculptIdle = 0 }
                return mine
            }, onEsc: { [weak self] in self?.setSculpt(false) })
            guard t.start() else {
                Access.prompt()
                alert("Нужно разрешение «Универсальный доступ»",
                      "Системные настройки → Конфиденциальность и безопасность → Универсальный доступ → включи «Сугроб». Потом снова нажми ⌃⌥S.")
                return
            }
            tap = t
            sculptIdle = 0
            world.sculptMode = true
        } else {
            tap?.stop()
            tap = nil
            world.sculptMode = false
            world.cursorOnSnow = false
        }
        sculptItem.state = on ? .on : .off
    }

    @objc func callPlow() {
        if world.paused { togglePause() }
        if world.dockRect == nil { world.dockRect = Access.dockFrame(); world.scan(ownPID: pid) }
        if !world.startPlow() {
            if !Access.trusted {
                Access.prompt()
                alert("Нужно разрешение «Универсальный доступ»",
                      "Без него Сугроб не видит Dock. Включи «Сугроб» в Системных настройках → Конфиденциальность и безопасность → Универсальный доступ.")
            } else {
                alert("Dock не найден", "Снегоуборщик ездит по Dock внизу экрана. Если Dock скрывается автоматически или стоит сбоку — чистить нечего.")
            }
        }
    }

    @objc func shakeAll() { world.shakeAll() }

    @objc func togglePause() {
        world.paused.toggle()
        pauseItem.state = world.paused ? .on : .off
        if world.paused {
            if world.sculptMode { setSculpt(false) }
            timer?.invalidate()
            timer = nil
            for o in overlays { o.view.needsDisplay = true }
        } else {
            startTimer()
        }
    }

    @objc func showSettings() {
        if settingsWindow == nil {
            let w = NSWindow(contentViewController: NSHostingController(rootView: SettingsView(s: settings)))
            w.title = "Сугроб — настройки"
            w.styleMask = [.titled, .closable]
            w.isReleasedWhenClosed = false
            w.center()
            settingsWindow = w
        }
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow?.makeKeyAndOrderFront(nil)
    }

    @objc func quit() {
        setSculpt(false)
        NSApp.terminate(nil)
    }

    func alert(_ title: String, _ text: String) {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        a.runModal()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
