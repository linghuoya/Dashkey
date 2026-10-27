import Cocoa
import Carbon.HIToolbox
import ServiceManagement
import CoreGraphics
import Darwin

// MARK: - 偏好键

enum Settings {
    static let holdDuration = "holdDuration"
    static let launchAtLogin = "launchAtLogin"
    static let holdOptionEnabled = "holdOptionEnabled"
    static let hotkeyEnabled = "hotkeyEnabled"
    /// 图标清晰度：0 = 省内存，1 = 标准，2 = 高清（原图）
    static let iconQuality = "iconQuality"
    static let defaultHoldDuration: TimeInterval = 2.0

    /// 读取开关，未设置过（首次运行）时返回默认值，默认 true。
    static func flag(_ key: String, default def: Bool = true) -> Bool {
        UserDefaults.standard.object(forKey: key) == nil ? def : UserDefaults.standard.bool(forKey: key)
    }

    /// 读取整数偏好，未设置过时返回默认值。
    static func intValue(_ key: String, default def: Int) -> Int {
        UserDefaults.standard.object(forKey: key) == nil ? def : UserDefaults.standard.integer(forKey: key)
    }
}

// MARK: - 数据模型

struct DockApp {
    let name: String
    let url: URL
    let bundleID: String?
    /// 启动时解码一次，HUD 复用，避免每次长按重新解码图标（内存优化 A）
    let icon: NSImage
}

// MARK: - HUD 单个图标

final class HUDItemView: NSView {

    init(app: DockApp, shortcut: String?, scale: CGFloat = 1) {
        super.init(frame: .zero)

        let iconSize: CGFloat = 36 * scale
        let iconView = NSImageView()
        iconView.image = app.icon
        iconView.imageScaling = .scaleProportionallyUpOrDown
        iconView.translatesAutoresizingMaskIntoConstraints = false
        addSubview(iconView)

        NSLayoutConstraint.activate([
            iconView.centerXAnchor.constraint(equalTo: centerXAnchor),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor, constant: -2 * scale),
            iconView.widthAnchor.constraint(equalToConstant: iconSize),
            iconView.heightAnchor.constraint(equalToConstant: iconSize)
        ])

        guard let shortcut else { return }

        let badge = NSTextField(labelWithString: shortcut)
        badge.font = .monospacedDigitSystemFont(ofSize: 10 * scale, weight: .semibold)
        badge.textColor = .white
        badge.alignment = .center
        badge.wantsLayer = true
        badge.layer?.backgroundColor = NSColor(white: 0.34, alpha: 0.95).cgColor
        badge.layer?.borderWidth = 1
        badge.layer?.borderColor = NSColor(white: 1, alpha: 0.18).cgColor
        badge.layer?.cornerRadius = 3.5 * scale
        badge.translatesAutoresizingMaskIntoConstraints = false
        addSubview(badge)

        // 角标叠在图标自身的右下角上（略微外探），而不是格子的右下角
        NSLayoutConstraint.activate([
            badge.trailingAnchor.constraint(equalTo: iconView.trailingAnchor, constant: 3 * scale),
            badge.bottomAnchor.constraint(equalTo: iconView.bottomAnchor, constant: 3 * scale),
            badge.widthAnchor.constraint(equalToConstant: 15 * scale),
            badge.heightAnchor.constraint(equalToConstant: 13 * scale)
        ])
    }

    required init?(coder: NSCoder) { nil }
}

// MARK: - HUD 面板

final class HUDWindow: NSPanel {

    /// 面板容器：透明纯色底（80% 透明），只有图标悬浮
    private let container = NSView()
    private var currentStack: NSStackView?

    private let itemSize: CGFloat = 52
    private let spacing: CGFloat = 8
    private let padding: CGFloat = 10

    /// 面板最多占用的屏幕宽度比例，留出边距避免贴边
    private let maxScreenUsage: CGFloat = 0.9

    init() {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 400, height: 160),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )

        isOpaque = false
        backgroundColor = .clear
        level = .screenSaver
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        hasShadow = true
        ignoresMouseEvents = true
        isMovable = false
        hidesOnDeactivate = false
        isReleasedWhenClosed = false

        // 透明底（黑色 20% 不透明 = 80% 透明）+ 细边框
        container.wantsLayer = true
        container.layer?.backgroundColor = NSColor(white: 0.0, alpha: 0.2).cgColor
        container.layer?.cornerRadius = 16
        container.layer?.borderWidth = 1
        container.layer?.borderColor = NSColor(white: 1.0, alpha: 0.10).cgColor
        container.layer?.masksToBounds = true
        contentView = container
    }

    func present(apps: [DockApp], slots: [(label: String, code: UInt32)]) {
        buildContent(apps: apps, slots: slots)
        centerOnActiveScreen()
        orderFrontRegardless()
    }

    private struct Layout {
        let perRow: Int
        let rows: Int
        let scale: CGFloat
    }

    /// 单行布局：按屏幕宽度算出能放几个；放不下就逐步缩小图标，
    /// 尽量把全部（最多 36 个）塞进一行，仍放不下则截断。
    private func computeLayout(count: Int) -> Layout {
        let visible = screenContainingMouse()?.visibleFrame
            ?? NSScreen.main?.visibleFrame
            ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let maxWidth = visible.width * maxScreenUsage

        var scale: CGFloat = 1
        var perRow = fittedCount(scale: scale, maxWidth: maxWidth)
        while perRow < count && scale > 0.45 {
            let next = max(0.45, scale * 0.9)
            if next == scale { break }
            scale = next
            perRow = fittedCount(scale: scale, maxWidth: maxWidth)
        }
        return Layout(perRow: perRow, rows: 1, scale: scale)
    }

    private func fittedCount(scale: CGFloat, maxWidth: CGFloat) -> Int {
        let unit = itemSize * scale
        let fitted = Int(floor((maxWidth - padding * 2 + spacing) / (unit + spacing)))
        return max(1, fitted)
    }

    private func buildContent(apps: [DockApp], slots: [(label: String, code: UInt32)]) {
        currentStack?.removeFromSuperview()

        // 最多显示 36 个（与快捷键槽位一一对应）
        let count = min(apps.count, slots.count)
        guard count > 0 else {
            setContentSize(.zero)
            return
        }

        let layout = computeLayout(count: count)
        let unit = itemSize * layout.scale
        // 单行居中：只取一行放得下的数量
        let columns = min(layout.perRow, count)

        let width = CGFloat(columns) * unit
            + CGFloat(max(0, columns - 1)) * spacing
            + padding * 2
        let height = unit + padding * 2

        let hStack = NSStackView()
        hStack.orientation = .horizontal
        hStack.spacing = spacing
        hStack.alignment = .centerY
        hStack.translatesAutoresizingMaskIntoConstraints = false

        for index in 0..<columns {
            let shortcut = index < slots.count ? slots[index].label : nil
            let item = HUDItemView(app: apps[index], shortcut: shortcut, scale: layout.scale)
            item.translatesAutoresizingMaskIntoConstraints = false
            item.widthAnchor.constraint(equalToConstant: unit).isActive = true
            item.heightAnchor.constraint(equalToConstant: unit).isActive = true
            hStack.addArrangedSubview(item)
        }

        container.addSubview(hStack)
        NSLayoutConstraint.activate([
            hStack.centerXAnchor.constraint(equalTo: container.centerXAnchor),
            hStack.centerYAnchor.constraint(equalTo: container.centerYAnchor)
        ])

        currentStack = hStack
        setContentSize(NSSize(width: width, height: height))
    }

    /// 精确取「鼠标所在屏幕」，避免后台 App 用 NSScreen.main 定位到错误屏幕
    private func screenContainingMouse() -> NSScreen? {
        let location = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(location, $0.frame, false) } ?? NSScreen.main
    }

    private func centerOnActiveScreen() {
        guard let screen = screenContainingMouse() else {
            center()
            return
        }
        let visible = screen.visibleFrame
        let size = frame.size
        let x = visible.minX + (visible.width - size.width) / 2
        let y = visible.minY + (visible.height - size.height) / 2
        setFrameOrigin(NSPoint(x: x, y: y))
    }
}

// MARK: - 主逻辑

final class AppDelegate: NSObject, NSApplicationDelegate {

    static var shared: AppDelegate?

    private var statusItem: NSStatusItem!
    private var hotKeyRefs: [EventHotKeyRef] = []
    private var apps: [DockApp] = []

    private var hudWindow: HUDWindow?
    private var holdWorkItem: DispatchWorkItem?
    private var settingsWindowController: NSWindowController?

    /// ⌥Space 热键（开关面板）的引用与唯一 ID（与 App 热键 id 0..n 不冲突）
    private var hudHotKeyRef: EventHotKeyRef?
    static let hudHotKeyID = UInt32(0xFFFF)

    /// 长按 Option 轮询（免辅助功能）相关
    private var optionPollTimer: Timer?
    private var optionWasDown = false

    /// 热键槽位：按键盘物理顺序排列 —— 先数字行 1-0，再字母三行 QWERTY / ASDF / ZXCV。
    /// 值为 Carbon 物理键码，与键盘布局、输入法无关。
    private let hotKeySlots: [(label: String, code: UInt32)] = [
        // 数字行：1 2 3 4 5 6 7 8 9 0
        ("1", 0x12), ("2", 0x13), ("3", 0x14), ("4", 0x15), ("5", 0x17),
        ("6", 0x16), ("7", 0x1A), ("8", 0x1C), ("9", 0x19), ("0", 0x1D),
        // 第一行：Q W E R T Y U I O P
        ("Q", 0x0C), ("W", 0x0D), ("E", 0x0E), ("R", 0x0F), ("T", 0x11),
        ("Y", 0x10), ("U", 0x20), ("I", 0x22), ("O", 0x1F), ("P", 0x23),
        // 第二行：A S D F G H J K L
        ("A", 0x00), ("S", 0x01), ("D", 0x02), ("F", 0x03), ("G", 0x05),
        ("H", 0x04), ("J", 0x26), ("K", 0x28), ("L", 0x25),
        // 第三行：Z X C V B N M
        ("Z", 0x06), ("X", 0x07), ("C", 0x08), ("V", 0x09), ("B", 0x0B),
        ("N", 0x2D), ("M", 0x2E)
    ]

    /// 长按 Option 多久呼出面板（秒），可在设置页调整，存 UserDefaults
    private var holdDuration: TimeInterval {
        let v = UserDefaults.standard.double(forKey: Settings.holdDuration)
        return v > 0 ? v : Settings.defaultHoldDuration
    }

    /// 是否把 Finder 计入列表。
    /// Finder 常驻 Dock 最左侧但不写入 persistent-apps，开启后热键编号与视觉顺序一致。
    private let includeFinderInList = true

    // MARK: 生命周期

    func applicationDidFinishLaunching(_ notification: Notification) {
        AppDelegate.shared = self
        setupStatusItem()
        installHotKeyHandler()
        applyTriggerSettings()
        reloadDockApps()
        // 开机启动：若上次已开启，重新登记（LSSharedFileList 在系统更新后可能失效）
        if UserDefaults.standard.bool(forKey: Settings.launchAtLogin) {
            setLaunchAtLogin(true)
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        unregisterHotKeys()
        unregisterHUDHotKey()
        stopOptionPolling()
    }

    // MARK: 菜单栏

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "\u{2325}"          // ⌥
        statusItem.button?.toolTip = "DashKey — Option + 键切换 App"
    }

    // MARK: 读取 Dock

    private func loadDockApps() -> [DockApp] {
        guard let raw = CFPreferencesCopyAppValue(
            "persistent-apps" as CFString,
            "com.apple.dock" as CFString
        ) as? [[String: Any]] else {
            return []
        }

        // HUD 与热键最多用 36 个，给 Finder 预留 1 个位置，其余不加载（省内存）
        let capacity = hotKeySlots.count

        var result: [DockApp] = []
        for item in raw {
            if result.count >= capacity - (includeFinderInList ? 1 : 0) { break }
            guard
                let tileData = item["tile-data"] as? [String: Any],
                let fileData = tileData["file-data"] as? [String: Any],
                let urlString = fileData["_CFURLString"] as? String,
                let url = URL(string: urlString),
                url.pathExtension.lowercased() == "app"
            else { continue }

            let name = (tileData["file-label"] as? String)
                ?? url.deletingPathExtension().lastPathComponent
            let bundleID = (tileData["bundle-identifier"] as? String)
                ?? Bundle(url: url)?.bundleIdentifier

            let icon = downsampleIcon(at: url)
            result.append(DockApp(name: name, url: url, bundleID: bundleID, icon: icon))
        }

        // Finder 固定占用 Dock 最左侧，但不在 persistent-apps 里，手动补上，
        // 保证 Option+键 的编号与 Dock 上看到的顺序完全对齐。
        if includeFinderInList {
            result.removeAll { $0.bundleID == "com.apple.finder" }
            let finderIcon = downsampleIcon(at: URL(fileURLWithPath: "/System/Library/CoreServices/Finder.app"))
            let finder = DockApp(
                name: "Finder",
                url: URL(fileURLWithPath: "/System/Library/CoreServices/Finder.app"),
                bundleID: "com.apple.finder",
                icon: finderIcon
            )
            result.insert(finder, at: 0)
        }

        return Array(result.prefix(capacity))
    }

    /// 把系统图标降采样成小位图，避免每个 App 都常驻一张 512×512 大图。
    /// 显示最大约 36pt，按 2 倍屏留 64pt（128px）足够清晰。
    private func downsampleIcon(at url: URL) -> NSImage {
        let source = NSWorkspace.shared.icon(forFile: url.path)
        // 0 = 省内存(48pt)，1 = 标准(64pt)，2 = 高清(直接用原图)
        let quality = Settings.intValue(Settings.iconQuality, default: 1)
        if quality >= 2 { return source }
        let pointSize: CGFloat = quality == 0 ? 48 : 64
        let px = Int(pointSize * 2)
        guard let rep = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: px, pixelsHigh: px,
            bitsPerSample: 8, samplesPerPixel: 4,
            hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0, bitsPerPixel: 0
        ) else { return source }

        rep.size = NSSize(width: pointSize, height: pointSize)
        NSGraphicsContext.saveGraphicsState()
        if let ctx = NSGraphicsContext(bitmapImageRep: rep) {
            NSGraphicsContext.current = ctx
            ctx.imageInterpolation = .high
            source.draw(
                in: NSRect(x: 0, y: 0, width: pointSize, height: pointSize),
                from: .zero, operation: .copy, fraction: 1.0
            )
        }
        NSGraphicsContext.restoreGraphicsState()

        let image = NSImage(size: NSSize(width: pointSize, height: pointSize))
        image.addRepresentation(rep)
        return image
    }

    func reloadDockApps() {
        apps = loadDockApps()
        registerHotKeys()
        rebuildMenu()
        let preview = apps.prefix(5).map { $0.name }.joined(separator: ", ")
        NSLog("DashKey: 读取到 %d 个 Dock 应用，热键注册成功 %d 个 → 前 5: %@",
              apps.count, hotKeyRefs.count, preview)
    }

    // MARK: 全局热键

    private func installHotKeyHandler() {
        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )

        InstallEventHandler(
            GetApplicationEventTarget(),
            { _, event, _ -> OSStatus in
                var hotKeyID = EventHotKeyID()
                let status = GetEventParameter(
                    event,
                    EventParamName(kEventParamDirectObject),
                    EventParamType(typeEventHotKeyID),
                    nil,
                    MemoryLayout<EventHotKeyID>.size,
                    nil,
                    &hotKeyID
                )
                guard status == noErr else { return status }

                if hotKeyID.id == AppDelegate.hudHotKeyID {
                    DispatchQueue.main.async {
                        AppDelegate.shared?.toggleHUD()
                    }
                } else {
                    let index = Int(hotKeyID.id)
                    DispatchQueue.main.async {
                        AppDelegate.shared?.activate(index: index)
                    }
                }
                return noErr
            },
            1,
            &eventType,
            nil,
            nil
        )
    }

    private func registerHotKeys() {
        unregisterHotKeys()

        for (index, _) in apps.prefix(hotKeySlots.count).enumerated() {
            var ref: EventHotKeyRef?
            let hotKeyID = EventHotKeyID(signature: OSType(0x44534B48), id: UInt32(index)) // 'DSHK'
            let status = RegisterEventHotKey(
                hotKeySlots[index].code,
                UInt32(optionKey),
                hotKeyID,
                GetApplicationEventTarget(),
                0,
                &ref
            )
            if status == noErr, let ref {
                hotKeyRefs.append(ref)
            }
        }
    }

    private func unregisterHotKeys() {
        for ref in hotKeyRefs {
            UnregisterEventHotKey(ref)
        }
        hotKeyRefs.removeAll()
    }

    // MARK: 面板触发（免辅助功能）

    /// 根据设置开关应用两种触发方式：长按 Option 轮询 + ⌥Space 热键。
    func applyTriggerSettings() {
        if Settings.flag(Settings.holdOptionEnabled) {
            startOptionPolling()
        } else {
            stopOptionPolling()
        }
        if Settings.flag(Settings.hotkeyEnabled) {
            registerHUDHotKey()
        } else {
            unregisterHUDHotKey()
        }
    }

    /// 长按 Option：轮询系统修饰键状态（无需辅助功能），做边沿检测后走长按计时逻辑。
    private func startOptionPolling() {
        guard optionPollTimer == nil else { return }
        let timer = Timer(timeInterval: 0.03, repeats: true) { [weak self] _ in
            guard let self else { return }
            let down = CGEventSource.flagsState(.hidSystemState).contains(.maskAlternate)
            if down != optionWasDown {
                optionWasDown = down
                if down {
                    startHoldTimer()
                } else {
                    cancelHoldTimer()
                    hideHUD()
                }
            }
        }
        optionPollTimer = timer
        RunLoop.main.add(timer, forMode: .common)
        NSLog("DashKey: 已开启长按 Option 轮询")
    }

    private func stopOptionPolling() {
        optionPollTimer?.invalidate()
        optionPollTimer = nil
        optionWasDown = false
        cancelHoldTimer()
    }

    /// ⌥Space 热键开关面板（无需辅助功能）。
    private func registerHUDHotKey() {
        guard hudHotKeyRef == nil else { return }
        var ref: EventHotKeyRef?
        let hotKeyID = EventHotKeyID(signature: OSType(0x44534B48), id: AppDelegate.hudHotKeyID) // 'DSHK'
        let status = RegisterEventHotKey(
            UInt32(kVK_Space),
            UInt32(optionKey),
            hotKeyID,
            GetApplicationEventTarget(),
            0,
            &ref
        )
        if status == noErr, let ref {
            hudHotKeyRef = ref
            NSLog("DashKey: 已注册 ⌥Space 热键")
        }
    }

    private func unregisterHUDHotKey() {
        if let ref = hudHotKeyRef {
            UnregisterEventHotKey(ref)
            hudHotKeyRef = nil
        }
    }

    /// ⌥Space 切换面板显隐。
    func toggleHUD() {
        if hudWindow != nil {
            hideHUD()
        } else {
            showHUD()
        }
    }

    private func startHoldTimer() {
        guard holdWorkItem == nil else { return }
        let work = DispatchWorkItem { [weak self] in
            self?.showHUD()
        }
        holdWorkItem = work
        DispatchQueue.main.asyncAfter(deadline: .now() + holdDuration, execute: work)
    }

    private func cancelHoldTimer() {
        holdWorkItem?.cancel()
        holdWorkItem = nil
    }

    private func showHUD() {
        if hudWindow == nil {
            hudWindow = HUDWindow()
        }
        hudWindow?.present(apps: apps, slots: hotKeySlots)
    }

    private func hideHUD() {
        hudWindow?.orderOut(nil)
        // 内存优化 B：隐藏即释放面板与毛玻璃 backdrop，空闲时不再常驻
        hudWindow = nil
    }

    // MARK: 启动 / 切换

    func activate(index: Int) {
        guard index >= 0, index < apps.count else { return }
        cancelHoldTimer()
        hideHUD()
        activateApp(apps[index])
    }

    private func activateApp(_ app: DockApp) {
        guard let bundleID = app.bundleID,
              let running = NSRunningApplication
                  .runningApplications(withBundleIdentifier: bundleID).first else {
            launch(app)
            return
        }

        let pid = running.processIdentifier
        let hasWindow = hasVisibleWindow(pid: pid)

        // 已在前台且有窗口 → 再按一次隐藏它（Finder 系统禁止隐藏，会静默失败）
        if running.isActive && hasWindow {
            running.hide()
            return
        }

        // macOS 14+ 要求调用方显式声明身份，否则后台进程的 activate 会被系统忽略
        if #available(macOS 14.0, *) {
            _ = running.activate(from: .current, options: [.activateAllWindows])
        } else {
            running.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
        }

        // 关键：再走一次 LaunchServices 的 open（等同 `open -a`，带 reopen 语义）。
        // 不依赖 Apple Event，不会触发「自动化」授权；窗口被关光时会新建一个。
        launch(app)
    }

    /// 目标进程是否存在「屏幕上可见」的普通窗口（layer 0 且 onscreen）。
    /// 只看可见窗口：微信等关窗后仍前台、只剩隐藏窗口的 App 会被判定为「无窗口」，
    /// 从而走唤起/重开逻辑，而不是被误当成"已有窗口"而执行隐藏。
    private func hasVisibleWindow(pid: pid_t) -> Bool {
        let options: CGWindowListOption = [.optionAll, .excludeDesktopElements]
        guard let infos = CGWindowListCopyWindowInfo(options, kCGNullWindowID) as? [[String: Any]] else {
            return false
        }
        for info in infos {
            guard let owner = info[kCGWindowOwnerPID as String] as? Int,
                  owner == Int(pid) else { continue }
            let layer = info[kCGWindowLayer as String] as? Int ?? 1
            guard layer == 0 else { continue }
            let onscreen = info[kCGWindowIsOnscreen as String] as? Bool ?? false
            if onscreen { return true }
        }
        return false
    }

    private func launch(_ app: DockApp) {
        let config = NSWorkspace.OpenConfiguration()
        config.activates = true
        NSWorkspace.shared.openApplication(at: app.url, configuration: config) { _, error in
            if let error {
                NSLog("DashKey: 打开 \(app.url.path) 失败 - \(error.localizedDescription)")
            }
        }
    }

    // MARK: 菜单

    private func rebuildMenu() {
        let menu = NSMenu()

        menu.addItem(.separator())

        let settings = NSMenuItem(title: "设置…", action: #selector(settingsAction), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)

        let reload = NSMenuItem(title: "重新读取 Dock", action: #selector(reloadAction), keyEquivalent: "r")
        reload.target = self
        menu.addItem(reload)

        let quit = NSMenuItem(title: "退出", action: #selector(quitAction), keyEquivalent: "q")
        quit.target = self
        menu.addItem(quit)

        statusItem.menu = menu
    }

    @objc private func menuActivate(_ sender: NSMenuItem) {
        activate(index: sender.tag)
    }

    @objc private func reloadAction() {
        reloadDockApps()
    }

    @objc private func settingsAction() {
        showSettings()
    }

    private func showSettings() {
        if settingsWindowController == nil {
            settingsWindowController = SettingsWindowController()
        }
        settingsWindowController?.showWindow(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    @objc private func quitAction() {
        NSApp.terminate(nil)
    }
}

// MARK: - 设置窗口

final class SettingsViewController: NSViewController {
    private let holdValueLabel = NSTextField(labelWithString: "")
    private let holdSlider = NSSlider()
    private let loginSwitch = NSSwitch()
    private let holdOptionSwitch = NSSwitch()
    private let hotkeySwitch = NSSwitch()
    private let iconQualityPopup = NSPopUpButton()

    override func loadView() {
        view = NSView(frame: NSRect(x: 0, y: 0, width: 440, height: 240))
        view.wantsLayer = true

        let holdTitle = NSTextField(labelWithString: "长按触发时长")
        holdSlider.minValue = 0.5
        holdSlider.maxValue = 3.0
        holdSlider.doubleValue = currentHold()
        holdSlider.numberOfTickMarks = 26
        holdSlider.setContentHuggingPriority(.defaultLow, for: .horizontal)
        holdSlider.target = self
        holdSlider.action = #selector(holdChanged(_:))
        updateHoldLabel()
        let holdRow = makeRow(title: holdTitle, control: holdSlider, trailing: holdValueLabel)

        // 两个触发开关合并到一行
        let triggersTitle = NSTextField(labelWithString: "调出面板")
        let holdOptionLabel = NSTextField(labelWithString: "长按 Option")
        holdOptionLabel.font = .systemFont(ofSize: 11)
        holdOptionLabel.textColor = .secondaryLabelColor
        holdOptionSwitch.state = Settings.flag(Settings.holdOptionEnabled) ? .on : .off
        holdOptionSwitch.controlSize = .small
        holdOptionSwitch.target = self
        holdOptionSwitch.action = #selector(holdOptionChanged(_:))
        let hotkeyLabel = NSTextField(labelWithString: "⌥Space")
        hotkeyLabel.font = .systemFont(ofSize: 11)
        hotkeyLabel.textColor = .secondaryLabelColor
        hotkeySwitch.state = Settings.flag(Settings.hotkeyEnabled) ? .on : .off
        hotkeySwitch.controlSize = .small
        hotkeySwitch.target = self
        hotkeySwitch.action = #selector(hotkeyChanged(_:))
        let triggersStack = NSStackView(views: [holdOptionLabel, holdOptionSwitch, hotkeyLabel, hotkeySwitch])
        triggersStack.orientation = .horizontal
        triggersStack.spacing = 6
        triggersStack.alignment = .centerY
        let triggersRow = makeRow(title: triggersTitle, control: triggersStack, trailing: nil)

        let qualityTitle = NSTextField(labelWithString: "图标清晰度")
        iconQualityPopup.addItems(withTitles: ["省内存", "标准", "高清"])
        iconQualityPopup.selectItem(at: Settings.intValue(Settings.iconQuality, default: 1))
        iconQualityPopup.controlSize = .small
        iconQualityPopup.target = self
        iconQualityPopup.action = #selector(iconQualityChanged(_:))
        let qualityHelp = makeHelpBadge("高清更清晰，但更占内存")
        // 下拉与问号放一起，宽度按内容自适应，不再拉满整行
        let qualityControl = NSStackView(views: [iconQualityPopup, qualityHelp])
        qualityControl.orientation = .horizontal
        qualityControl.spacing = 6
        qualityControl.alignment = .centerY
        let qualityRow = makeRow(title: qualityTitle, control: qualityControl, trailing: nil)

        let loginTitle = NSTextField(labelWithString: "登录时启动")
        loginSwitch.state = isLaunchAtLogin() ? .on : .off
        loginSwitch.target = self
        loginSwitch.action = #selector(loginChanged(_:))
        let loginRow = makeRow(title: loginTitle, control: loginSwitch, trailing: nil)

        let stack = NSStackView(views: [holdRow, triggersRow, qualityRow, loginRow])
        stack.orientation = .vertical
        stack.spacing = 16
        stack.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -24),
            stack.topAnchor.constraint(equalTo: view.topAnchor, constant: 20),
            stack.bottomAnchor.constraint(lessThanOrEqualTo: view.bottomAnchor, constant: -20)
        ])
        for row in stack.arrangedSubviews {
            row.leadingAnchor.constraint(equalTo: stack.leadingAnchor).isActive = true
            row.trailingAnchor.constraint(equalTo: stack.trailingAnchor).isActive = true
        }
    }

    private func makeRow(title: NSTextField, control: NSView, trailing: NSView?) -> NSView {
        let row = NSView()
        title.setContentHuggingPriority(.defaultHigh, for: .horizontal)
        row.addSubview(title)
        row.addSubview(control)
        title.translatesAutoresizingMaskIntoConstraints = false
        control.translatesAutoresizingMaskIntoConstraints = false
        var cons = [
            title.leadingAnchor.constraint(equalTo: row.leadingAnchor),
            title.centerYAnchor.constraint(equalTo: row.centerYAnchor),
            title.widthAnchor.constraint(equalToConstant: 110),
            control.leadingAnchor.constraint(equalTo: title.trailingAnchor, constant: 12),
            control.centerYAnchor.constraint(equalTo: row.centerYAnchor),
            control.heightAnchor.constraint(equalToConstant: 22)
        ]
        if let trailing {
            trailing.translatesAutoresizingMaskIntoConstraints = false
            row.addSubview(trailing)
            cons.append(contentsOf: [
                trailing.leadingAnchor.constraint(equalTo: control.trailingAnchor, constant: 10),
                trailing.centerYAnchor.constraint(equalTo: row.centerYAnchor),
                row.trailingAnchor.constraint(equalTo: trailing.trailingAnchor)
            ])
        } else {
            // 无 trailing 控件：行宽由外层 stack 约束撑满，控件靠左即可
        }
        row.heightAnchor.constraint(equalToConstant: 24).isActive = true
        NSLayoutConstraint.activate(cons)
        return row
    }

    /// 圆形「?」帮助徽标，鼠标悬浮显示提示
    private func makeHelpBadge(_ tooltip: String) -> NSView {
        let size: CGFloat = 16
        let badge = NSView()
        badge.wantsLayer = true
        badge.layer?.cornerRadius = size / 2
        badge.layer?.backgroundColor = NSColor.secondaryLabelColor.withAlphaComponent(0.10).cgColor
        badge.layer?.borderWidth = 1
        badge.layer?.borderColor = NSColor.secondaryLabelColor.withAlphaComponent(0.35).cgColor
        badge.translatesAutoresizingMaskIntoConstraints = false

        let q = NSTextField(labelWithString: "?")
        let base = NSFont.systemFont(ofSize: 10, weight: .heavy)
        if let rounded = base.fontDescriptor.withDesign(.rounded),
           let roundedFont = NSFont(descriptor: rounded, size: 10) {
            q.font = roundedFont
        } else {
            q.font = base
        }
        q.textColor = .secondaryLabelColor
        q.alignment = .center
        q.translatesAutoresizingMaskIntoConstraints = false
        badge.addSubview(q)

        NSLayoutConstraint.activate([
            badge.widthAnchor.constraint(equalToConstant: size),
            badge.heightAnchor.constraint(equalToConstant: size),
            q.centerXAnchor.constraint(equalTo: badge.centerXAnchor),
            q.centerYAnchor.constraint(equalTo: badge.centerYAnchor, constant: -0.5)
        ])

        badge.toolTip = tooltip
        return badge
    }

    private func currentHold() -> Double {
        let v = UserDefaults.standard.double(forKey: Settings.holdDuration)
        return v > 0 ? v : Settings.defaultHoldDuration
    }

    private func updateHoldLabel() {
        holdValueLabel.stringValue = String(format: "%.1f s", holdSlider.doubleValue)
    }

    @objc private func holdChanged(_ sender: NSSlider) {
        UserDefaults.standard.set(sender.doubleValue, forKey: Settings.holdDuration)
        updateHoldLabel()
    }

    @objc private func loginChanged(_ sender: NSSwitch) {
        let on = sender.state == .on
        UserDefaults.standard.set(on, forKey: Settings.launchAtLogin)
        setLaunchAtLogin(on)
    }

    @objc private func holdOptionChanged(_ sender: NSSwitch) {
        let on = sender.state == .on
        UserDefaults.standard.set(on, forKey: Settings.holdOptionEnabled)
        AppDelegate.shared?.applyTriggerSettings()
    }

    @objc private func hotkeyChanged(_ sender: NSSwitch) {
        let on = sender.state == .on
        UserDefaults.standard.set(on, forKey: Settings.hotkeyEnabled)
        AppDelegate.shared?.applyTriggerSettings()
    }

    @objc private func iconQualityChanged(_ sender: NSPopUpButton) {
        UserDefaults.standard.set(sender.indexOfSelectedItem, forKey: Settings.iconQuality)
        // 重新读取 Dock，按新清晰度重建图标
        AppDelegate.shared?.reloadDockApps()
    }
}

final class SettingsWindowController: NSWindowController {
    convenience init() {
        let vc = SettingsViewController()
        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 440, height: 240),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        window.title = "DashKey 设置"
        window.contentViewController = vc
        window.center()
        self.init(window: window)
    }
}

// MARK: - 登录项（SMAppService.mainApp；macOS 13+，替代已废弃且在本机崩溃的 LSSharedFileList）

func isLaunchAtLogin() -> Bool {
    if #available(macOS 13.0, *) {
        return SMAppService.mainApp.status == .enabled
    }
    return UserDefaults.standard.bool(forKey: Settings.launchAtLogin)
}

func setLaunchAtLogin(_ enabled: Bool) {
    UserDefaults.standard.set(enabled, forKey: Settings.launchAtLogin)
    guard #available(macOS 13.0, *) else { return }
    do {
        if enabled {
            if SMAppService.mainApp.status != .enabled {
                try SMAppService.mainApp.register()
            }
        } else if SMAppService.mainApp.status == .enabled {
            try SMAppService.mainApp.unregister()
        }
    } catch {
        NSLog("DashKey: 设置登录启动失败（%@）。若从 Build 目录运行，请把 DashKey.app 拷到 /Applications 后再试。",
              error.localizedDescription)
    }
}

// MARK: - 单实例保护

/// 用文件锁保证同一时间只有一个 DashKey 实例；进程退出时内核自动释放，无需手动清理。
private func acquireSingleInstanceLock() -> Bool {
    let path = (NSTemporaryDirectory() as NSString)
        .appendingPathComponent("com.local.dashkey.\(getuid()).lock")
    let fd = open(path, O_CREAT | O_RDWR, 0o644)
    guard fd >= 0 else { return true } // 拿不到锁文件时放行，避免误伤启动
    if flock(fd, LOCK_EX | LOCK_NB) != 0 {
        close(fd)
        return false
    }
    return true
}

if !acquireSingleInstanceLock() {
    NSLog("DashKey: 已有实例在运行，退出本次启动")
    if let bid = Bundle.main.bundleIdentifier,
       let existing = NSRunningApplication
           .runningApplications(withBundleIdentifier: bid)
           .first(where: { $0.processIdentifier != getpid() }) {
        existing.activate(options: [])
    }
    exit(0)
}

// MARK: - 入口

let application = NSApplication.shared
let appDelegate = AppDelegate()
application.delegate = appDelegate
application.setActivationPolicy(.accessory)
application.run()
