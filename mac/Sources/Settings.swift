import Foundation
import SwiftUI
import ServiceManagement

final class AppSettings: ObservableObject {
    private let d = UserDefaults.standard
    var onChange: (() -> Void)?

    @Published var intensity: Double { didSet { save("intensity", intensity) } }
    @Published var wind: Double { didSet { save("wind", wind) } }
    @Published var flakeSize: Double { didSet { save("flakeSize", flakeSize) } }
    @Published var maxPile: Double { didSet { save("maxPile", maxPile) } }
    @Published var accumulation: Double { didSet { save("accumulation", accumulation) } }
    @Published var pileOnWindows: Bool { didSet { save("pileOnWindows", pileOnWindows) } }
    @Published var pileOnDock: Bool { didSet { save("pileOnDock", pileOnDock) } }
    @Published var plow: Bool { didSet { save("plow", plow) } }
    @Published var plowInterval: Double { didSet { save("plowInterval", plowInterval) } }
    @Published var fps60: Bool { didSet { save("fps60", fps60) } }
    var firstRun: Bool {
        get { !d.bool(forKey: "launchedBefore") }
        set { d.set(!newValue, forKey: "launchedBefore") }
    }

    init() {
        d.register(defaults: [
            "intensity": 45.0, "wind": 8.0, "flakeSize": 1.0, "maxPile": 16.0, "accumulation": 1.0,
            "pileOnWindows": true, "pileOnDock": true, "plow": true, "plowInterval": 8.0, "fps60": false
        ])
        intensity = d.double(forKey: "intensity")
        wind = d.double(forKey: "wind")
        flakeSize = d.double(forKey: "flakeSize")
        maxPile = d.double(forKey: "maxPile")
        accumulation = d.double(forKey: "accumulation")
        pileOnWindows = d.bool(forKey: "pileOnWindows")
        pileOnDock = d.bool(forKey: "pileOnDock")
        plow = d.bool(forKey: "plow")
        plowInterval = d.double(forKey: "plowInterval")
        fps60 = d.bool(forKey: "fps60")
    }

    private func save(_ key: String, _ value: Any) {
        d.set(value, forKey: key)
        onChange?()
    }
}

enum Autostart {
    static var isOn: Bool { SMAppService.mainApp.status == .enabled }
    static func set(_ on: Bool) {
        do {
            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
        } catch {
            NSLog("Sugrob autostart: \(error)")
        }
    }
}

struct LabeledSlider: View {
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    let fmt: (Double) -> String

    var body: some View {
        HStack {
            Text(title).frame(width: 160, alignment: .leading)
            Slider(value: $value, in: range)
            Text(fmt(value)).monospacedDigit().frame(width: 64, alignment: .trailing)
        }
    }
}

struct SettingsView: View {
    @ObservedObject var s: AppSettings
    @State private var autostart = Autostart.isOn

    var body: some View {
        Form {
            Section("Снегопад") {
                LabeledSlider(title: "Интенсивность", value: $s.intensity, range: 0...100) { "\(Int($0.rounded()))%" }
                LabeledSlider(title: "Ветер", value: $s.wind, range: -100...100) { v in
                    let i = Int(v.rounded())
                    return i == 0 ? "штиль" : (i < 0 ? "← \(-i)" : "\(i) →")
                }
                LabeledSlider(title: "Размер снежинок", value: $s.flakeSize, range: 0.5...2) { String(format: "×%.1f", $0) }
            }
            Section("Сугробы") {
                Toggle("Снег копится на окнах", isOn: $s.pileOnWindows)
                Toggle("Снег копится на Dock", isOn: $s.pileOnDock)
                LabeledSlider(title: "Максимальная высота", value: $s.maxPile, range: 6...40) { "\(Int($0.rounded())) pt" }
                LabeledSlider(title: "Скорость накопления", value: $s.accumulation, range: 0.2...3) { String(format: "×%.1f", $0) }
            }
            Section("Снегоуборщик") {
                Toggle("Сам чистит Dock", isOn: $s.plow)
                LabeledSlider(title: "Раз в", value: $s.plowInterval, range: 1...60) { "\(Int($0.rounded())) мин" }
            }
            Section("Прочее") {
                Toggle("Плавная анимация 60 FPS (больше нагрузка)", isOn: $s.fps60)
                Toggle("Открывать при входе в систему", isOn: Binding(
                    get: { autostart },
                    set: { v in Autostart.set(v); autostart = Autostart.isOn }))
            }
            Section {
                Text("⌃⌥S — режим лепки: кликай по сугробу, три кома — снеговик. Правый клик по снеговику — разрушить. Esc — выход.\nСнег на Dock и лепка требуют разрешения «Универсальный доступ».")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .formStyle(.grouped)
        .frame(width: 480)
    }
}
