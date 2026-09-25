import Foundation

/// Where the bubble sits next to a text box. All rectangles are in Accessibility coordinates:
/// origin at the top-left of the main display, y growing downwards.
public enum BubblePlacement {
    public static let gap: CGFloat = 8

    /// Just past the right end of a one-line field, centred on it; inside the bottom-right corner
    /// of a tall one (a document or a message box that fills the window). `offset` is where the
    /// user dragged it to, relative to that spot. The result is kept on the field's screen.
    public static func origin(field: CGRect, bubble size: CGFloat, screen: CGRect,
                              offset: CGSize = .zero) -> CGPoint {
        var p: CGPoint
        if field.isEmpty || field.width < 1 || field.height < 1 {
            p = CGPoint(x: screen.maxX - size - 24, y: screen.midY - size / 2)
        } else if field.height > size * 2.2 {
            p = CGPoint(x: field.maxX - size - 14, y: field.maxY - size - 14)
        } else {
            p = CGPoint(x: field.maxX + gap, y: field.midY - size / 2)
            // No room on the right: tuck it inside the field's right end instead.
            if p.x + size > screen.maxX - 4 { p.x = field.maxX - size - 6 }
        }
        p.x += offset.width
        p.y += offset.height
        return clamp(p, size: size, into: screen)
    }

    public static func clamp(_ p: CGPoint, size: CGFloat, into screen: CGRect) -> CGPoint {
        CGPoint(x: min(max(p.x, screen.minX + 4), screen.maxX - size - 4),
                y: min(max(p.y, screen.minY + 4), screen.maxY - size - 4))
    }

    /// Accessibility (top-left) to AppKit (bottom-left) coordinates. `mainHeight` is the height of
    /// the display that holds the menu bar, which both systems measure from.
    public static func toAppKit(_ r: CGRect, mainHeight: CGFloat) -> CGRect {
        CGRect(x: r.minX, y: mainHeight - r.maxY, width: r.width, height: r.height)
    }

    /// Top-centre of the screen, under the menu bar: where the recording capsule goes (AppKit
    /// coordinates, the visible frame already excludes the menu bar).
    public static func capsuleOrigin(visibleFrame v: CGRect, width: CGFloat, height: CGFloat) -> CGPoint {
        CGPoint(x: v.midX - width / 2, y: v.maxY - height - 10)
    }
}
