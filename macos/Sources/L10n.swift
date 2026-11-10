import Foundation

/// Ngôn ngữ giao diện: theo máy (tiếng Việt nếu máy để tiếng Việt, còn lại tiếng Anh),
/// hoặc chọn cố định trong menu (lưu ở UserDefaults "lang": "en" / "vi").
enum AppLanguage {
    static var current: String {
        if let forced = UserDefaults.standard.string(forKey: "lang"), forced == "en" || forced == "vi" { return forced }
        return (Locale.preferredLanguages.first ?? "en").hasPrefix("vi") ? "vi" : "en"
    }
    static let isVietnamese = current == "vi"
}

/// Chuỗi song ngữ: L("English", "Tiếng Việt").
@inline(__always) func L(_ en: String, _ vi: String) -> String { AppLanguage.isVietnamese ? vi : en }
