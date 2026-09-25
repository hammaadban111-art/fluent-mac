import FluentCore
import Foundation

/// Android decides the Style category from the package of the app in front
/// (`StyleCategory.forPackage`). The Mac does the same from the bundle id of the frontmost app,
/// with Android's package lists mapped to their Mac apps.
public enum AppCategories {
    public static let personal: Set<String> = [
        "com.apple.MobileSMS",                       // Messages
        "net.whatsapp.WhatsApp", "desktop.WhatsApp", "WhatsApp",
        "ru.keepcoder.Telegram", "org.telegram.desktop", "com.tdesktop.Telegram",
        "com.facebook.archon", "com.facebook.archon.developerID",   // Messenger
        "org.whispersystems.signal-desktop",
        "com.hnc.Discord", "com.hnc.DiscordPTB", "com.hnc.DiscordCanary",
        "jp.naver.line.mac", "com.viber.osx", "com.tencent.xinWeChat", "com.kakao.KakaoTalkMac",
        "com.burbn.instagram", "com.snapchat.Snapchat",
    ]

    public static let work: Set<String> = [
        "com.tinyspeck.slackmacgap",                 // Slack
        "com.microsoft.teams", "com.microsoft.teams2",
        "com.google.Chat", "us.zoom.xos", "Cisco-Systems.Spark", "com.mattermost.desktop",
        "chat.rocket", "com.electron.lark", "com.bytedance.lark.Feishu", "com.zoho.chat",
        "com.linkedin.LinkedIn", "com.facebook.Workplace", "com.workplace.chat",
    ]

    public static let email: Set<String> = [
        "com.apple.mail",                            // Mail
        "com.microsoft.Outlook",
        "com.readdle.smartemail-Mac", "com.readdle.SparkDesktop",
        "ch.protonmail.desktop", "com.yahoo.mail", "de.tutao.tutanota",
        "org.mozilla.thunderbird", "com.mimestream.Mimestream", "com.superhuman.electron",
        "com.airmailapp.airmail5", "com.freron.MailMate", "it.bloop.airmail2", "com.canarymail.mac",
        "com.fastmail.mac.Fastmail", "com.edisonmail.edisonmail", "io.newton.mac",
    ]

    public static func category(for bundleID: String?) -> StyleCategory {
        guard let id = bundleID, !id.isEmpty else { return .other }
        if personal.contains(id) { return .personal }
        if work.contains(id) { return .work }
        if email.contains(id) { return .email }
        return .other
    }

    /// Mac wording for the Style screen ("This style applies in …"), like Android's `appliesIn`.
    public static func appliesIn(_ c: StyleCategory) -> String {
        switch c {
        case .personal: "Messages, WhatsApp, Telegram, Signal, Discord and friends"
        case .work: "Slack, Teams, Google Chat, Zoom and other work chat"
        case .email: "Mail, Outlook, Spark, Superhuman and other email apps"
        case .other: "Every other app"
        }
    }
}
