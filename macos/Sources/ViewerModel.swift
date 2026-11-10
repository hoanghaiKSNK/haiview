import SwiftUI
import AppKit
import PDFKit
import UniformTypeIdentifiers

final class ViewerModel: ObservableObject {
    static let shared = ViewerModel()

    // Tài liệu
    @Published private(set) var document: PDFDocument?
    @Published private(set) var sourceURL: URL?
    @Published private(set) var title = "HaiView"
    @Published var busyMessage: String?
    @Published var errorMessage: String?

    // Bố cục (nhớ giữa các lần mở app)
    @Published private(set) var tree: LayoutNode { didSet { saveJSON(tree, "tree") } }
    @Published private(set) var layoutName: String { didSet { UserDefaults.standard.set(layoutName, forKey: "layoutName") } }
    @Published private(set) var savedLayouts: [LayoutPreset] { didSet { saveJSON(savedLayouts, "savedLayouts") } }
    @Published var gridRows: Int { didSet { UserDefaults.standard.set(gridRows, forKey: "rows") } }
    @Published var gridCols: Int { didSet { UserDefaults.standard.set(gridCols, forKey: "cols") } }
    @Published var gap: Double { didSet { UserDefaults.standard.set(gap, forKey: "gap") } }
    @Published var showLabels: Bool { didSet { UserDefaults.standard.set(showLabels, forKey: "labels") } }
    /// true: lật cả nhóm (3 ô → nhảy 3 trang). false: nhảy 1 trang.
    @Published var stepByGroup: Bool { didSet { UserDefaults.standard.set(stepByGroup, forKey: "stepByGroup") } }

    // Giao diện
    @Published var editing = false
    @Published var showThumbnails = false

    // Trạng thái trình chiếu
    @Published var start = 0
    @Published var deltas: [Int: Int] = [:]
    @Published var selected: Int?
    @Published var zoomed: Int?
    @Published var blackout = false
    @Published var typedNumber = ""
    @Published private(set) var screens: [NSScreen] = NSScreen.screens

    private var keyMonitor: Any?
    private let thumbCache = NSCache<NSNumber, NSImage>()

    private init() {
        let d = UserDefaults.standard
        tree = Self.loadJSON(LayoutNode.self, "tree") ?? LayoutPreset.builtIn[4].tree
        layoutName = d.string(forKey: "layoutName") ?? LayoutPreset.builtIn[4].title
        savedLayouts = Self.loadJSON([LayoutPreset].self, "savedLayouts") ?? []
        gridRows = d.object(forKey: "rows") as? Int ?? 2
        gridCols = d.object(forKey: "cols") as? Int ?? 2
        gap = d.object(forKey: "gap") as? Double ?? 6
        showLabels = d.object(forKey: "labels") as? Bool ?? true
        stepByGroup = d.object(forKey: "stepByGroup") as? Bool ?? true
        installKeyMonitor()
        NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.screens = NSScreen.screens }
    }

    private func saveJSON<T: Encodable>(_ v: T, _ key: String) {
        if let data = try? JSONEncoder().encode(v) { UserDefaults.standard.set(data, forKey: key) }
    }
    private static func loadJSON<T: Decodable>(_ t: T.Type, _ key: String) -> T? {
        guard let data = UserDefaults.standard.data(forKey: key) else { return nil }
        return try? JSONDecoder().decode(t, from: data)
    }

    // MARK: - Bố cục

    var cellCount: Int { tree.leafCount }

    func apply(_ p: LayoutPreset) {
        tree = p.tree
        layoutName = p.title
        resetPanes()
    }

    func applyGrid() {
        apply(LayoutPreset(id: "grid", title: L("Grid", "Lưới") + " \(gridRows) × \(gridCols)", tree: .grid(rows: gridRows, cols: gridCols)))
    }

    private func markCustom() {
        let mark = L(" (edited)", " (đã chỉnh)")
        if !layoutName.hasSuffix(mark) { layoutName += mark }
    }

    func setRatios(at path: [Int], _ ratios: [Double]) {
        tree.setRatios(at: path, ratios)
        markCustom()
    }

    func split(at path: [Int], axis: SplitAxis) {
        guard cellCount < 16 else { return }
        tree.splitLeaf(at: path, axis: axis)
        markCustom()
        resetPanes()
    }

    func remove(at path: [Int]) {
        guard cellCount > 1 else { return }
        tree.removeLeaf(at: path)
        markCustom()
        resetPanes()
    }

    func saveCurrentLayout(named name: String) {
        let n = name.trimmingCharacters(in: .whitespaces)
        guard !n.isEmpty else { return }
        savedLayouts.removeAll { $0.title == n }
        savedLayouts.append(LayoutPreset(id: UUID().uuidString, title: n, tree: tree))
        layoutName = n
    }

    func deleteSaved(_ p: LayoutPreset) { savedLayouts.removeAll { $0.id == p.id } }

    // MARK: - Ô và trang

    var pageCount: Int { document?.pageCount ?? 0 }

    func pageIndex(for cell: Int) -> Int { start + cell + (deltas[cell] ?? 0) }

    func page(at index: Int) -> PDFPage? {
        guard let doc = document, index >= 0, index < doc.pageCount else { return nil }
        return doc.page(at: index)
    }

    var shownPages: Set<Int> {
        Set((0..<cellCount).map { pageIndex(for: $0) }.filter { $0 >= 0 && $0 < pageCount })
    }

    var rangeText: String {
        guard pageCount > 0 else { return "" }
        let shown = shownPages
        guard let lo = shown.min(), let hi = shown.max() else { return "- / \(pageCount)" }
        return lo == hi ? L("Page", "Trang") + " \(lo + 1) / \(pageCount)" : L("Pages", "Trang") + " \(lo + 1)-\(hi + 1) / \(pageCount)"
    }

    func thumbnail(_ i: Int) -> NSImage? {
        if let img = thumbCache.object(forKey: NSNumber(value: i)) { return img }
        guard let p = page(at: i) else { return nil }
        let img = p.thumbnail(of: CGSize(width: 360, height: 220), for: .cropBox)
        thumbCache.setObject(img, forKey: NSNumber(value: i))
        return img
    }

    /// Kéo một trang từ cột thu nhỏ thả vào ô.
    func assign(page: Int, toCell cell: Int) {
        guard page >= 0, page < pageCount else { return }
        deltas[cell] = page - start - cell
    }

    private func resetPanes() {
        deltas = [:]
        selected = nil
        zoomed = nil
    }

    // MARK: - Điều hướng

    func next() {
        guard pageCount > 0 else { return }
        if let s = selected {
            if pageIndex(for: s) < pageCount - 1 { deltas[s, default: 0] += 1 }
            return
        }
        let step = stepByGroup ? cellCount : 1
        if start + step <= pageCount - 1 { start += step }
    }

    func previous() {
        guard pageCount > 0 else { return }
        if let s = selected {
            if pageIndex(for: s) > 0 { deltas[s, default: 0] -= 1 }
            return
        }
        let step = stepByGroup ? cellCount : 1
        start = max(0, start - step)
    }

    func first() { start = 0; deltas = [:] }
    func last() {
        guard pageCount > 0 else { return }
        start = max(0, pageCount - cellCount)
        deltas = [:]
    }

    /// Đi tới trang số `n` (đếm từ 1). Nếu đang chọn một ô thì chỉ đổi ô đó.
    func goTo(pageNumber n: Int) {
        guard pageCount > 0 else { return }
        let target = min(max(n, 1), pageCount) - 1
        if let s = selected {
            deltas[s] = target - start - s
        } else {
            start = target
            deltas = [:]
        }
    }

    func toggleSelect(_ cell: Int) { selected = (selected == cell) ? nil : cell }
    func toggleZoom(_ cell: Int) { zoomed = (zoomed == nil) ? cell : nil; selected = nil }

    // MARK: - Mở file

    func openPanel() {
        let panel = NSOpenPanel()
        var types: [UTType] = [.pdf]
        for ext in PowerPointConverter.extensions {
            if let t = UTType(filenameExtension: ext) { types.append(t) }
        }
        panel.allowedContentTypes = types
        panel.allowsMultipleSelection = false
        panel.message = L("Choose a PDF or PowerPoint file", "Chọn file PDF hoặc PowerPoint")
        if panel.runModal() == .OK, let url = panel.url { open(url) }
    }

    func reload() {
        guard let url = sourceURL else { return }
        open(url, force: true, keepPosition: true)
    }

    func open(_ url: URL, force: Bool = false, keepPosition: Bool = false) {
        let ext = url.pathExtension.lowercased()
        if ext == "pdf" {
            load(pdfAt: url, source: url, keepPosition: keepPosition)
        } else if PowerPointConverter.extensions.contains(ext) {
            busyMessage = L("Asking PowerPoint to convert “\(url.lastPathComponent)” to PDF…", "Đang nhờ PowerPoint chuyển “\(url.lastPathComponent)” sang PDF…")
            Task { @MainActor in
                do {
                    let pdf = try await PowerPointConverter.convert(url, force: force)
                    self.busyMessage = nil
                    self.load(pdfAt: pdf, source: url, keepPosition: keepPosition)
                } catch {
                    self.busyMessage = nil
                    self.errorMessage = error.localizedDescription
                }
            }
        } else {
            errorMessage = L("HaiView opens PDF and PowerPoint files (.pdf, .pptx, .ppt).", "Chỉ mở được file PDF hoặc PowerPoint (.pdf, .pptx, .ppt).")
        }
    }

    private func load(pdfAt pdf: URL, source: URL, keepPosition: Bool) {
        guard let doc = PDFDocument(url: pdf) else {
            errorMessage = L("Couldn't read the PDF: ", "Không đọc được file PDF: ") + pdf.lastPathComponent
            return
        }
        thumbCache.removeAllObjects()
        document = doc
        sourceURL = source
        title = source.lastPathComponent
        NSDocumentController.shared.noteNewRecentDocumentURL(source)
        if keepPosition {
            start = min(start, max(0, doc.pageCount - 1))
        } else {
            start = 0
            resetPanes()
        }
    }

    // MARK: - Màn hình chiếu

    var mainWindow: NSWindow? {
        NSApp.windows.first { $0.isVisible && !($0 is NSPanel) && $0.canBecomeMain }
    }

    func toggleFullScreen() { mainWindow?.toggleFullScreen(nil) }

    /// Đưa cửa sổ sang màn hình (máy chiếu / TV) và bật toàn màn hình.
    func present(on screen: NSScreen) {
        guard let win = mainWindow else { return }
        let move = {
            win.setFrame(screen.visibleFrame, display: true)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { win.toggleFullScreen(nil) }
        }
        if win.styleMask.contains(.fullScreen) {
            win.toggleFullScreen(nil)
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.9, execute: move)
        } else {
            move()
        }
    }

    // MARK: - Bàn phím và bút trình chiếu

    private func installKeyMonitor() {
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self else { return event }
            return self.handle(event) ? nil : event
        }
    }

    /// Trả về true nếu đã xử lý phím.
    private func handle(_ e: NSEvent) -> Bool {
        // Đang gõ vào ô nhập, popover, hộp thoại → để hệ thống xử lý.
        if let w = NSApp.keyWindow, w !== mainWindow { return false }
        if NSApp.keyWindow?.firstResponder is NSText { return false }
        let mods = e.modifierFlags.intersection([.command, .control, .option])
        if !mods.isEmpty { return false }
        let shift = e.modifierFlags.contains(.shift)

        switch e.keyCode {
        case 124, 125, 121, 49:   // → ↓ PageDown Space
            if shift && selected == nil { let s = stepByGroup; stepByGroup = false; next(); stepByGroup = s }
            else { next() }
            return true
        case 123, 126, 116, 51:   // ← ↑ PageUp Delete
            if shift && selected == nil { let s = stepByGroup; stepByGroup = false; previous(); stepByGroup = s }
            else { previous() }
            return true
        case 115: first(); return true          // Home
        case 119: last(); return true           // End
        case 36, 76:                             // Return
            if let n = Int(typedNumber) { goTo(pageNumber: n) }
            typedNumber = ""
            return true
        case 53:                                 // Esc
            if !typedNumber.isEmpty { typedNumber = "" }
            else if blackout { blackout = false }
            else if editing { editing = false }
            else if zoomed != nil { zoomed = nil }
            else if selected != nil { selected = nil }
            else if mainWindow?.styleMask.contains(.fullScreen) == true { toggleFullScreen() }
            return true
        default: break
        }

        guard let ch = e.charactersIgnoringModifiers?.lowercased() else { return false }
        if let c = ch.first, c.isNumber, ch.count == 1 {
            typedNumber.append(c)
            return true
        }
        switch ch {
        case "b", ".": blackout.toggle(); return true      // màn hình đen (bút trình chiếu thường có nút này)
        case "f": toggleFullScreen(); return true
        case "h": showLabels.toggle(); return true
        case "e": editing.toggle(); return true
        case "t": showThumbnails.toggle(); return true
        case "z":
            if let s = selected { toggleZoom(s) } else if zoomed != nil { zoomed = nil }
            return true
        case "r": deltas = [:]; selected = nil; return true
        default: return false
        }
    }
}
