import AppKit
import Carbon
import ApplicationServices

/// Глобальная горячая клавиша ⌃⌥S через Carbon (разрешения не нужны).
final class HotKey {
    private var ref: EventHotKeyRef?
    private var handler: EventHandlerRef?
    private let action: () -> Void

    init(keyCode: UInt32, modifiers: UInt32, action: @escaping () -> Void) {
        self.action = action
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, userData in
            guard let userData else { return noErr }
            let me = Unmanaged<HotKey>.fromOpaque(userData).takeUnretainedValue()
            DispatchQueue.main.async { me.action() }
            return noErr
        }, 1, &spec, Unmanaged.passUnretained(self).toOpaque(), &handler)
        let id = EventHotKeyID(signature: OSType(0x53475242), id: 1)   // 'SGRB'
        RegisterEventHotKey(keyCode, modifiers, id, GetApplicationEventTarget(), 0, &ref)
    }

    deinit {
        if let ref { UnregisterEventHotKey(ref) }
        if let handler { RemoveEventHandler(handler) }
    }
}

/// Перехват кликов в режиме лепки: клик по снегу забираем себе, остальные идут в окна.
final class SculptTap {
    private var tap: CFMachPort?
    private var src: CFRunLoopSource?
    private var swallowUpL = false, swallowUpR = false
    let onDown: (CGPoint, Bool) -> Bool
    let onEsc: () -> Void

    init(onDown: @escaping (CGPoint, Bool) -> Bool, onEsc: @escaping () -> Void) {
        self.onDown = onDown
        self.onEsc = onEsc
    }

    func start() -> Bool {
        var mask = CGEventMask(0)
        for t in [CGEventType.leftMouseDown, .leftMouseUp, .rightMouseDown, .rightMouseUp, .keyDown] {
            mask |= CGEventMask(1) << CGEventMask(t.rawValue)
        }
        guard let t = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap, options: .defaultTap,
                                        eventsOfInterest: mask,
                                        callback: { _, type, event, refcon in
                                            guard let refcon else { return Unmanaged.passUnretained(event) }
                                            let me = Unmanaged<SculptTap>.fromOpaque(refcon).takeUnretainedValue()
                                            return me.handle(type, event)
                                        },
                                        userInfo: Unmanaged.passUnretained(self).toOpaque()) else { return false }
        tap = t
        src = CFMachPortCreateRunLoopSource(nil, t, 0)
        CFRunLoopAddSource(CFRunLoopGetMain(), src, .commonModes)
        CGEvent.tapEnable(tap: t, enable: true)
        return true
    }

    private func handle(_ type: CGEventType, _ e: CGEvent) -> Unmanaged<CGEvent>? {
        switch type {
        case .tapDisabledByTimeout, .tapDisabledByUserInput:
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
        case .leftMouseDown, .rightMouseDown:
            let right = type == .rightMouseDown
            if onDown(e.location, right) {
                if right { swallowUpR = true } else { swallowUpL = true }
                return nil
            }
        case .leftMouseUp:
            if swallowUpL { swallowUpL = false; return nil }
        case .rightMouseUp:
            if swallowUpR { swallowUpR = false; return nil }
        case .keyDown:
            if e.getIntegerValueField(.keyboardEventKeycode) == 53 {   // Esc
                DispatchQueue.main.async { self.onEsc() }
                return nil
            }
        default:
            break
        }
        return Unmanaged.passUnretained(e)
    }

    func stop() {
        if let tap {
            CGEvent.tapEnable(tap: tap, enable: false)
            CFMachPortInvalidate(tap)
        }
        if let src { CFRunLoopRemoveSource(CFRunLoopGetMain(), src, .commonModes) }
        tap = nil
        src = nil
    }
}

enum Access {
    static var trusted: Bool { AXIsProcessTrusted() }

    /// Показывает системный запрос «Универсальный доступ».
    static func prompt() {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        _ = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    }

    /// Рамка значков Dock через Accessibility (глобальные координаты, y вниз).
    static func dockFrame() -> CGRect? {
        guard trusted,
              let dock = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.dock").first else { return nil }
        let app = AXUIElementCreateApplication(dock.processIdentifier)
        var children: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXChildrenAttribute as CFString, &children) == .success,
              let arr = children as? [AXUIElement] else { return nil }
        for el in arr {
            var role: CFTypeRef?
            AXUIElementCopyAttributeValue(el, kAXRoleAttribute as CFString, &role)
            guard (role as? String) == (kAXListRole as String) else { continue }
            var posV: CFTypeRef?, sizeV: CFTypeRef?
            guard AXUIElementCopyAttributeValue(el, kAXPositionAttribute as CFString, &posV) == .success,
                  AXUIElementCopyAttributeValue(el, kAXSizeAttribute as CFString, &sizeV) == .success,
                  let posV, let sizeV else { return nil }
            var p = CGPoint.zero, s = CGSize.zero
            AXValueGetValue(posV as! AXValue, .cgPoint, &p)
            AXValueGetValue(sizeV as! AXValue, .cgSize, &s)
            return CGRect(origin: p, size: s)
        }
        return nil
    }
}
