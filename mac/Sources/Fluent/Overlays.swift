import AppKit
import FluentMacKit
import SwiftUI

/// A borderless, non-activating floating panel. Clicking it never makes Fluent the active app,
/// so the text box the user was typing in keeps its focus (Android: FLAG_NOT_FOCUSABLE).
final class OverlayPanel: NSPanel {
    init(size: NSSize) {
        super.init(contentRect: NSRect(origin: .zero, size: size),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        isFloatingPanel = true
        level = .statusBar
        backgroundColor = .clear
        isOpaque = false
        hasShadow = false
        hidesOnDeactivate = false
        becomesKeyOnlyIfNeeded = true
        isReleasedWhenClosed = false
        animationBehavior = .none
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// Buttons in a panel that is never key must still work on the first click.
final class FirstMouseHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// The bubble's view: draws the orb, and handles click (dictate), press-and-hold (push-to-talk),
/// drag (move it) and right-click (menu).
final class BubbleHostView: NSView {
    var onClick: (() -> Void)?
    var onHoldStart: (() -> Void)?
    var onHoldEnd: (() -> Void)?
    var onDragEnd: ((NSPoint) -> Void)?
    var menuProvider: (() -> NSMenu?)?
    private(set) var dragging = false

    private let hosting: NSHostingView<BubbleContent>
    private var content: BubbleContent
    private var downAt: NSPoint = .zero
    private var originAtDown: NSPoint = .zero
    private var holdTimer: Timer?
    private var holdFired = false

    init(content: BubbleContent) {
        self.content = content
        hosting = NSHostingView(rootView: content)
        super.init(frame: NSRect(x: 0, y: 0, width: content.size + 16, height: content.size + 16))
        hosting.frame = bounds
        hosting.autoresizingMask = [.width, .height]
        addSubview(hosting)
    }

    required init?(coder: NSCoder) { fatalError("not used") }

    func update(_ content: BubbleContent) {
        self.content = content
        hosting.rootView = content
    }

    override func hitTest(_ point: NSPoint) -> NSView? { frame.contains(point) ? self : nil }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        downAt = NSEvent.mouseLocation
        originAtDown = window?.frame.origin ?? .zero
        dragging = false
        holdFired = false
        setPressed(true)
        holdTimer?.invalidate()
        holdTimer = Timer.scheduledTimer(withTimeInterval: 0.45, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self, !self.dragging else { return }
                self.holdFired = true
                self.onHoldStart?()
            }
        }
    }

    override func mouseDragged(with event: NSEvent) {
        let now = NSEvent.mouseLocation
        let dx = now.x - downAt.x, dy = now.y - downAt.y
        if !dragging && !holdFired && hypot(dx, dy) > 4 {
            dragging = true
            holdTimer?.invalidate()
        }
        if dragging {
            window?.setFrameOrigin(NSPoint(x: originAtDown.x + dx, y: originAtDown.y + dy))
        }
    }

    override func mouseUp(with event: NSEvent) {
        holdTimer?.invalidate()
        setPressed(false)
        if holdFired {
            onHoldEnd?()
        } else if dragging {
            onDragEnd?(window?.frame.origin ?? .zero)
        } else {
            onClick?()
        }
        dragging = false
        holdFired = false
    }

    override func rightMouseDown(with event: NSEvent) {
        if let menu = menuProvider?() { NSMenu.popUpContextMenu(menu, with: event, for: self) }
    }

    private func setPressed(_ on: Bool) {
        content.pressed = on
        hosting.rootView = content
    }
}

/// Owns the bubble and the capsule and keeps them in step with the focused field and the
/// dictation, the job of Android's `BubbleService`.
@MainActor
final class OverlayController: NSObject {
    let model: AppModel
    private var dictation: DictationController { model.dictation }

    private var bubblePanel: OverlayPanel?
    private var bubbleView: BubbleHostView?
    private var capsulePanel: OverlayPanel?
    private var capsuleHost: FirstMouseHostingView<CapsuleContent>?
    private var timer: Timer?
    private var ticks = 0
    private(set) var currentField: FocusedField?
    /// The spot the bubble would take with no drag offset, in AX coordinates.
    private var autoOrigin: CGPoint = .zero
    private var capsuleShown = false

    /// Demo screenshots pin the overlays instead of following a real field.
    var pinned = false

    init(model: AppModel) {
        self.model = model
        super.init()
    }

    func start() {
        timer?.invalidate()
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.tick() }
        }
    }

    private func tick() {
        ticks += 1
        if ticks % 4 == 0 { model.refreshPermissions() }
        syncCapsule()
        guard !pinned else { return }
        syncBubble()
    }

    // MARK: bubble

    private func syncBubble() {
        let phase = dictation.phase
        if phase.isBusy {
            // One control at a time: the capsule owns the session. A press-and-hold turn keeps the
            // bubble (invisible) because it is holding the mouse that ends the turn.
            if dictation.source == .bubbleHold && phase.isLive { bubblePanel?.alphaValue = 0 } else { hideBubble() }
            return
        }
        guard model.axTrusted, !(bubbleView?.dragging ?? false) else {
            if !model.axTrusted { hideBubble() }
            return
        }
        let front = NSWorkspace.shared.frontmostApplication
        let field = front?.bundleIdentifier == Bundle.main.bundleIdentifier ? nil : FieldFinder.frontmost()
        currentField = field
        let allowed = model.mac.bubbleAllowed(termsAccepted: model.termsAccepted, field: field?.kind,
                                              bundleID: field?.bundleID, ownBundleID: Bundle.main.bundleIdentifier)
        if allowed, let field { showBubble(for: field) } else { hideBubble() }
    }

    func showBubble(for field: FocusedField?, atAX point: CGPoint? = nil) {
        let size = CGFloat(model.bubbleSize)
        let content = BubbleContent(palette: model.overlayPalette, size: size, opacity: model.bubbleOpacity, pressed: false)
        let panelSize = NSSize(width: size + 16, height: size + 16)
        if bubblePanel == nil {
            let panel = OverlayPanel(size: panelSize)
            let view = BubbleHostView(content: content)
            view.onClick = { [weak self] in self?.bubbleClicked() }
            view.onHoldStart = { [weak self] in self?.bubbleHoldStart() }
            view.onHoldEnd = { [weak self] in self?.dictation.stop() }
            view.onDragEnd = { [weak self] origin in self?.bubbleDragged(to: origin) }
            view.menuProvider = { [weak self] in self?.bubbleMenu() }
            panel.contentView = view
            bubblePanel = panel
            bubbleView = view
        } else {
            bubbleView?.update(content)
        }
        guard let panel = bubblePanel else { return }
        if panel.frame.size != panelSize { panel.setContentSize(panelSize) }

        let mainHeight = NSScreen.screens.first?.frame.height ?? 900
        let fieldFrame = field?.frame ?? .zero
        let screen = screenFor(axPoint: point ?? CGPoint(x: fieldFrame.midX, y: fieldFrame.midY), mainHeight: mainHeight)
        let axVisible = BubblePlacement.toAppKit(screen.visibleFrame, mainHeight: mainHeight)   // flip is symmetric
        let origin: CGPoint
        if let point {
            origin = point
            autoOrigin = point
        } else {
            autoOrigin = BubblePlacement.origin(field: fieldFrame, bubble: panelSize.width, screen: axVisible)
            origin = BubblePlacement.origin(field: fieldFrame, bubble: panelSize.width, screen: axVisible,
                                            offset: model.mac.bubbleOffset)
        }
        let appKit = BubblePlacement.toAppKit(CGRect(origin: origin, size: panelSize), mainHeight: mainHeight)
        if panel.frame.origin != appKit.origin { panel.setFrameOrigin(appKit.origin) }
        panel.alphaValue = 1
        if !panel.isVisible { panel.orderFrontRegardless() }
    }

    func hideBubble() {
        bubblePanel?.orderOut(nil)
    }

    private func screenFor(axPoint: CGPoint, mainHeight: CGFloat) -> NSScreen {
        let p = NSPoint(x: axPoint.x, y: mainHeight - axPoint.y)
        return NSScreen.screens.first { $0.frame.contains(p) } ?? NSScreen.main ?? NSScreen.screens[0]
    }

    private func bubbleClicked() {
        dictation.toggle(source: .bubble, target: currentField)
    }

    private func bubbleHoldStart() {
        dictation.start(source: .bubbleHold, target: currentField)
    }

    private func bubbleDragged(to appKitOrigin: NSPoint) {
        let mainHeight = NSScreen.screens.first?.frame.height ?? 900
        let size = bubblePanel?.frame.size ?? .zero
        let ax = BubblePlacement.toAppKit(CGRect(origin: appKitOrigin, size: size), mainHeight: mainHeight).origin
        model.mac.bubbleOffset = CGSize(width: ax.x - autoOrigin.x, height: ax.y - autoOrigin.y)
    }

    private func bubbleMenu() -> NSMenu {
        let menu = NSMenu()
        menu.addItem(ClosureMenuItem("Dictate") { [weak self] in self?.bubbleClicked() })
        menu.addItem(.separator())
        menu.addItem(ClosureMenuItem("Snooze for \(model.snoozeChoice.label)") { [weak self] in
            self?.model.snooze(); self?.hideBubble()
        })
        if let field = currentField, let id = field.bundleID {
            menu.addItem(ClosureMenuItem("Never show in \(field.appName ?? id)") { [weak self] in
                self?.model.exclude(id); self?.hideBubble()
            })
        }
        menu.addItem(ClosureMenuItem("Reset bubble position") { [weak self] in self?.model.mac.bubbleOffset = .zero })
        menu.addItem(.separator())
        menu.addItem(ClosureMenuItem("Open Fluent…") { AppDelegate.showMainWindow() })
        return menu
    }

    // MARK: capsule

    private func syncCapsule() {
        let want = dictation.phase != .idle && dictation.source != .inApp
        if want && !capsuleShown { showCapsule() }
        if !want && capsuleShown { hideCapsule() }
    }

    func showCapsule() {
        let content = CapsuleContent(dictation: dictation, palette: model.overlayPalette,
                                     onPause: { [weak self] in self?.dictation.togglePause() },
                                     onStop: { [weak self] in self?.dictation.stop() },
                                     onCancel: { [weak self] in self?.dictation.cancel() })
        let size = NSSize(width: CapsuleContent.size.width + 24, height: CapsuleContent.size.height + 24)
        if capsulePanel == nil {
            let panel = OverlayPanel(size: size)
            let host = FirstMouseHostingView(rootView: content)
            host.frame = NSRect(origin: .zero, size: size)
            panel.contentView = host
            capsulePanel = panel
            capsuleHost = host
        } else {
            capsuleHost?.rootView = content
        }
        guard let panel = capsulePanel else { return }
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { $0.frame.contains(mouse) } ?? NSScreen.main ?? NSScreen.screens[0]
        let origin = BubblePlacement.capsuleOrigin(visibleFrame: screen.visibleFrame, width: size.width, height: size.height)
        panel.setFrameOrigin(NSPoint(x: origin.x, y: origin.y + 10))
        panel.alphaValue = 0
        panel.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.18
            panel.animator().alphaValue = 1
            panel.animator().setFrameOrigin(NSPoint(x: origin.x, y: origin.y))
        }
        capsuleShown = true
    }

    func hideCapsule() {
        capsulePanel?.orderOut(nil)
        capsuleShown = false
    }
}

/// Target for a menu item that runs a closure. The item keeps it alive as its represented object.
final class MenuAction: NSObject {
    private let handler: () -> Void
    init(_ handler: @escaping () -> Void) { self.handler = handler }
    @objc func run() { handler() }
}

@MainActor
func ClosureMenuItem(_ title: String, handler: @escaping () -> Void) -> NSMenuItem {
    let action = MenuAction(handler)
    let item = NSMenuItem(title: title, action: #selector(MenuAction.run), keyEquivalent: "")
    item.target = action
    item.representedObject = action
    return item
}

/// Hold-to-talk on a modifier key and the toggle shortcut.
@MainActor
final class HotkeyManager {
    private let model: AppModel
    private var gesture = HoldGesture()
    private var holdTimer: Timer?
    private var monitors: [Any] = []
    private let toggle = ToggleHotKey()
    var fieldProvider: () -> FocusedField? = { nil }

    init(model: AppModel) {
        self.model = model
        fieldProvider = { FieldFinder.frontmost() }
    }

    func install() {
        if let m = NSEvent.addGlobalMonitorForEvents(matching: [.flagsChanged, .keyDown], handler: { [weak self] e in
            MainActor.assumeIsolated { self?.handle(e) }
        }) { monitors.append(m) }
        if let m = NSEvent.addLocalMonitorForEvents(matching: [.flagsChanged, .keyDown], handler: { [weak self] e in
            MainActor.assumeIsolated { self?.handle(e) }
            return e
        }) { monitors.append(m) }
        toggle.action = { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.model.dictation.toggle(source: .toggle, target: self.fieldProvider())
            }
        }
        registerToggle()
        model.onShortcutsChanged = { [weak self] in self?.registerToggle() }
    }

    func registerToggle() {
        let s = model.toggleShortcut
        if s.isOff { toggle.unregister() } else { toggle.register(keyCode: s.keyCode, modifiers: s.carbonModifiers) }
    }

    private func handle(_ e: NSEvent) {
        let dictation = model.dictation
        if e.type == .keyDown {
            // Esc cancels a dictation in progress.
            if e.keyCode == 53, dictation.phase.isLive { dictation.cancel() }
            act(gesture.handle(.otherKey))
            return
        }
        guard e.type == .flagsChanged, let code = model.holdKey.keyCode, model.termsAccepted else { return }
        if e.keyCode == code {
            let down = Self.isDown(model.holdKey, e.modifierFlags)
            if down && gesture.pressedAt == nil {
                act(gesture.handle(.down(at: e.timestamp)))
            } else if !down && gesture.pressedAt != nil {
                act(gesture.handle(.up(at: e.timestamp)))
            }
        } else if gesture.pressedAt != nil {
            act(gesture.handle(.otherKey))   // ⌥⇧… is a shortcut, not a hold
        }
    }

    private func act(_ action: HoldGesture.Action) {
        switch action {
        case .none: break
        case .armTimer:
            holdTimer?.invalidate()
            holdTimer = Timer.scheduledTimer(withTimeInterval: HoldGesture.holdDelay, repeats: false) { [weak self] _ in
                MainActor.assumeIsolated { self?.holdTimerFired() }
            }
        case .cancelTimer:
            holdTimer?.invalidate()
        case .stopAndInsert:
            holdTimer?.invalidate()
            model.dictation.stop()
        }
    }

    private func holdTimerFired() {
        guard gesture.timerFired(), !model.snoozed else { return }
        let dictation = model.dictation
        if !dictation.phase.isLive { dictation.start(source: .holdKey, target: fieldProvider()) }
    }

    /// Left and right modifiers share a flag; the device-dependent bits tell them apart.
    static func isDown(_ key: HoldKey, _ flags: NSEvent.ModifierFlags) -> Bool {
        let raw = flags.rawValue
        switch key {
        case .rightOption: return raw & 0x40 != 0
        case .rightCommand: return raw & 0x10 != 0
        case .rightControl: return raw & 0x2000 != 0
        case .fn: return flags.contains(.function)
        case .off: return false
        }
    }
}
