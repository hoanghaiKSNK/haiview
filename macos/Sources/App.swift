import SwiftUI
import AppKit

final class AppDelegate: NSObject, NSApplicationDelegate {
    // Mở file từ Finder ("Mở bằng…"), kéo vào biểu tượng Dock, hoặc Mở gần đây.
    func application(_ application: NSApplication, open urls: [URL]) {
        if let u = urls.first { ViewerModel.shared.open(u) }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

/// Đổi ngôn ngữ giao diện rồi mở lại app.
func relaunch(language: String?) {
    if let language { UserDefaults.standard.set(language, forKey: "lang") }
    else { UserDefaults.standard.removeObject(forKey: "lang") }
    UserDefaults.standard.synchronize()
    let p = Process()
    p.executableURL = URL(fileURLWithPath: "/usr/bin/open")
    p.arguments = ["-n", Bundle.main.bundlePath]
    try? p.run()
    NSApp.terminate(nil)
}

@main
struct HaiViewApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @StateObject private var model = ViewerModel.shared

    var body: some Scene {
        Window("HaiView", id: "main") {
            ContentView()
                .environmentObject(model)
                .frame(minWidth: 720, minHeight: 420)
        }
        .defaultSize(width: 1280, height: 760)
        .commands {
            CommandGroup(after: .appSettings) {
                Menu("Language / Ngôn ngữ") {
                    Button("Automatic / Tự động") { relaunch(language: nil) }
                    Button("English") { relaunch(language: "en") }
                    Button("Tiếng Việt") { relaunch(language: "vi") }
                }
            }
            CommandGroup(replacing: .newItem) {
                Button(L("Open…", "Mở…")) { model.openPanel() }.keyboardShortcut("o")
                Button(L("Reload file", "Tải lại file")) { model.reload() }.keyboardShortcut("r")
            }
            CommandMenu(L("Layout", "Bố cục")) {
                ForEach(Array(LayoutPreset.builtIn.enumerated()), id: \.offset) { i, p in
                    if i < 9 {
                        Button(p.title) { model.apply(p) }
                            .keyboardShortcut(KeyEquivalent(Character(String(i + 1))), modifiers: .command)
                    } else {
                        Button(p.title) { model.apply(p) }
                    }
                }
                if !model.savedLayouts.isEmpty {
                    Divider()
                    ForEach(model.savedLayouts) { p in
                        Button(p.title) { model.apply(p) }
                    }
                }
                Divider()
                Button(L("Edit layout  (E)", "Sửa bố cục  (E)")) { model.editing.toggle() }
                Button(L("Page list  (T)", "Danh sách trang  (T)")) { model.showThumbnails.toggle() }
            }
            CommandMenu(L("Present", "Trình chiếu")) {
                Button(L("Next  (→, Space, PageDown)", "Trang sau  (→, Space, PageDown)")) { model.next() }
                Button(L("Previous  (←, PageUp)", "Trang trước  (←, PageUp)")) { model.previous() }
                Button(L("First page  (Home)", "Về trang đầu  (Home)")) { model.first() }
                Divider()
                Button(L("Black screen  (B)", "Màn hình đen  (B)")) { model.blackout.toggle() }
                Button(L("Show/hide page numbers  (H)", "Ẩn/hiện số trang  (H)")) { model.showLabels.toggle() }
                Button(L("Reset cells to consecutive pages  (R)", "Đưa mọi ô về trang liên tiếp  (R)")) { model.deltas = [:]; model.selected = nil }
                Divider()
                Button(L("Full screen", "Toàn màn hình")) { model.toggleFullScreen() }
                    .keyboardShortcut("f", modifiers: [.command, .control])
            }
        }
    }
}
