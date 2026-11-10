import SwiftUI
import AppKit
import PDFKit

// MARK: - Vẽ một trang PDF (vector, nét trên Retina và máy chiếu 4K)

final class PDFPageNSView: NSView {
    var page: PDFPage? { didSet { if page !== oldValue { needsDisplay = true } } }

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layerContentsRedrawPolicy = .duringViewResize
    }
    required init?(coder: NSCoder) { fatalError() }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        needsDisplay = true
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let page, let ctx = NSGraphicsContext.current?.cgContext else { return }
        let box = page.bounds(for: .cropBox)
        let rotated = page.rotation % 180 != 0
        let pw = rotated ? box.height : box.width
        let ph = rotated ? box.width : box.height
        guard pw > 0, ph > 0 else { return }
        let scale = min(bounds.width / pw, bounds.height / ph)
        let dw = pw * scale, dh = ph * scale
        let origin = CGPoint(x: (bounds.width - dw) / 2, y: (bounds.height - dh) / 2)

        ctx.setFillColor(NSColor.white.cgColor)
        ctx.fill(CGRect(origin: origin, size: CGSize(width: dw, height: dh)))
        ctx.saveGState()
        ctx.interpolationQuality = .high
        ctx.translateBy(x: origin.x, y: origin.y)
        ctx.scaleBy(x: scale, y: scale)
        page.draw(with: .cropBox, to: ctx)
        ctx.restoreGState()
    }
}

struct PDFPageView: NSViewRepresentable {
    let page: PDFPage?
    func makeNSView(context: Context) -> PDFPageNSView { PDFPageNSView(frame: .zero) }
    func updateNSView(_ v: PDFPageNSView, context: Context) { v.page = page }
}

// MARK: - Màn hình chính

struct ContentView: View {
    @EnvironmentObject var m: ViewerModel
    @State private var showLayoutPicker = false

    var body: some View {
        HStack(spacing: 0) {
            if m.showThumbnails && m.document != nil {
                ThumbnailList().frame(width: 190)
                Divider()
            }
            ZStack {
                Color.black.ignoresSafeArea()
                if m.document != nil || m.editing { PagesGrid() } else { EmptyState() }
                if m.blackout { Color.black.ignoresSafeArea() }
                if !m.typedNumber.isEmpty {
                    Text(L("Go to page", "Đi tới trang") + " \(m.typedNumber)  ↩")
                        .font(.system(size: 28, weight: .semibold))
                        .padding(.horizontal, 20).padding(.vertical, 10)
                        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 12))
                }
                if m.editing { EditBanner() }
                if let msg = m.busyMessage {
                    VStack(spacing: 14) {
                        ProgressView().controlSize(.large)
                        Text(msg).font(.title3).multilineTextAlignment(.center)
                    }
                    .padding(28)
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
                }
            }
        }
        .navigationTitle(m.title)
        .toolbar { toolbarContent }
        .windowToolbarFullScreenVisibility(.onHover)
        .dropDestination(for: URL.self) { urls, _ in
            guard let u = urls.first else { return false }
            m.open(u)
            return true
        }
        .alert(L("Couldn't open the file", "Không mở được"), isPresented: Binding(
            get: { m.errorMessage != nil }, set: { if !$0 { m.errorMessage = nil } })
        ) {
            Button(L("Close", "Đóng"), role: .cancel) {}
        } message: { Text(m.errorMessage ?? "") }
    }

    @ToolbarContentBuilder
    var toolbarContent: some ToolbarContent {
        ToolbarItemGroup(placement: .navigation) {
            Button { m.openPanel() } label: { Label(L("Open", "Mở"), systemImage: "folder") }
                .help(L("Open a PDF or PowerPoint file (⌘O)", "Mở file PDF hoặc PowerPoint (⌘O)"))
            Toggle(isOn: $m.showThumbnails) { Label(L("Page list", "Danh sách trang"), systemImage: "sidebar.left") }
                .help(L("Page thumbnails - drag a page onto a cell (T)", "Danh sách trang thu nhỏ - kéo trang thả vào ô (T)"))
        }
        ToolbarItemGroup(placement: .principal) {
            Button { showLayoutPicker.toggle() } label: {
                HStack(spacing: 6) {
                    LayoutIcon(tree: m.tree).frame(width: 30, height: 18)
                    Text(m.layoutName).lineLimit(1)
                    Image(systemName: "chevron.down").font(.caption2)
                }
            }
            .help(L("Choose a layout", "Chọn bố cục"))
            .popover(isPresented: $showLayoutPicker, arrowEdge: .bottom) {
                LayoutPicker(close: { showLayoutPicker = false }).environmentObject(m)
            }
            Toggle(isOn: $m.editing) { Label(L("Edit layout", "Sửa bố cục"), systemImage: "square.split.bottomrightquarter") }
                .help(L("Edit layout: drag dividers, split or remove cells (E)", "Sửa bố cục: kéo đường chia, chia hoặc xoá ô (E)"))
        }
        ToolbarItemGroup(placement: .primaryAction) {
            Button { m.previous() } label: { Label(L("Previous", "Trước"), systemImage: "chevron.left") }
                .help(L("Previous (←)", "Trang trước (←)"))
            Text(m.rangeText).monospacedDigit().frame(minWidth: 120)
            Button { m.next() } label: { Label(L("Next", "Sau"), systemImage: "chevron.right") }
                .help(L("Next (→)", "Trang sau (→)"))
            Picker(L("Flip", "Lật"), selection: $m.stepByGroup) {
                Text(L("Flip whole group", "Lật cả nhóm")).tag(true)
                Text(L("Flip 1 page", "Lật 1 trang")).tag(false)
            }
            .pickerStyle(.menu)
            .help(L("Does → move the whole group or just one page?", "Mỗi lần bấm → thì nhảy cả nhóm hay chỉ 1 trang"))
            Menu {
                Section(L("Present full screen on", "Chiếu toàn màn hình lên")) {
                    ForEach(Array(m.screens.enumerated()), id: \.offset) { i, s in
                        Button(screenName(s, i)) { m.present(on: s) }
                    }
                }
                Divider()
                Toggle(L("Page numbers (H)", "Hiện số trang (H)"), isOn: $m.showLabels)
                Button(L("Black screen (B)", "Màn hình đen (B)")) { m.blackout.toggle() }
                if m.sourceURL != nil {
                    Button(L("Reload file (⌘R)", "Tải lại file (⌘R)")) { m.reload() }
                }
            } label: {
                Label(L("Present", "Chiếu"), systemImage: "play.display")
            }
            .help(L("Choose the projector / TV screen", "Chọn màn hình máy chiếu / TV"))
        }
    }

    func screenName(_ s: NSScreen, _ i: Int) -> String {
        let size = "\(Int(s.frame.width))×\(Int(s.frame.height))"
        let tag = (s == NSScreen.screens.first) ? L(" - this Mac", " - màn hình máy") : ""
        return "\(s.localizedName) (\(size))\(tag)"
    }
}

struct EditBanner: View {
    @EnvironmentObject var m: ViewerModel
    var body: some View {
        VStack {
            Spacer()
            HStack(spacing: 14) {
                Image(systemName: "square.split.bottomrightquarter")
                Text(L("Editing layout: drag the blue dividers to resize · use the buttons on each cell to split or remove it", "Đang sửa bố cục: kéo đường chia màu xanh để đổi rộng hẹp · dùng nút trên từng ô để chia hoặc xoá"))
                Button(L("Done", "Xong")) { m.editing = false }.keyboardShortcut(.defaultAction)
            }
            .font(.callout)
            .padding(.horizontal, 16).padding(.vertical, 10)
            .background(.regularMaterial, in: Capsule())
            .padding(.bottom, 18)
        }
    }
}

struct EmptyState: View {
    @EnvironmentObject var m: ViewerModel
    var body: some View {
        VStack(spacing: 18) {
            Image(systemName: "rectangle.split.3x1")
                .font(.system(size: 56, weight: .light))
                .foregroundStyle(.secondary)
            Text(L("Drop a PDF or PowerPoint file here", "Kéo thả file PDF hoặc PowerPoint vào đây"))
                .font(.title2)
            Button(L("Open file…", "Mở file…")) { m.openPanel() }
                .controlSize(.large)
                .keyboardShortcut(.defaultAction)
            Text(L("→ / Space / PageDown: next   ·   ←: previous   ·   type a number + ↩: go to page\nClick a cell to flip it on its own   ·   double-click: zoom a cell   ·   B: black screen   ·   F: full screen\nE: edit layout (resize, split, remove cells)   ·   T: page list, drag a page onto a cell", "→ / Space / PageDown: trang sau   ·   ←: trang trước   ·   gõ số + ↩: tới trang\nBấm một ô để lật riêng ô đó   ·   bấm đúp: phóng to ô   ·   B: màn hình đen   ·   F: toàn màn hình\nE: sửa bố cục (kéo rộng hẹp, chia, xoá ô)   ·   T: danh sách trang, kéo trang thả vào ô"))
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .padding(.top, 8)
        }
        .foregroundStyle(.white)
        .padding(40)
    }
}

// MARK: - Lưới các trang

struct PagesGrid: View {
    @EnvironmentObject var m: ViewerModel

    var body: some View {
        GeometryReader { geo in
            let g = CGFloat(m.gap)
            let area = CGRect(origin: .zero, size: geo.size).insetBy(dx: g / 2, dy: g / 2)
            ZStack(alignment: .topLeading) {
                if let z = m.zoomed {
                    PageCell(cell: z, path: [])
                        .frame(width: area.width - g, height: area.height - g)
                        .offset(x: area.minX + g / 2, y: area.minY + g / 2)
                } else {
                    let lay = m.tree.layout(in: area)
                    ForEach(lay.leaves, id: \.index) { leaf in
                        let r = leaf.rect.insetBy(dx: g / 2, dy: g / 2)
                        PageCell(cell: leaf.index, path: leaf.path)
                            .frame(width: max(1, r.width), height: max(1, r.height))
                            .offset(x: r.minX, y: r.minY)
                    }
                    ForEach(lay.dividers) { d in
                        DividerHandle(divider: d)
                    }
                }
            }
            .frame(width: geo.size.width, height: geo.size.height, alignment: .topLeading)
        }
    }
}

/// Đường chia giữa hai vùng: kéo để đổi tỉ lệ rộng/hẹp.
struct DividerHandle: View {
    let divider: LayoutNode.Divider
    @EnvironmentObject var m: ViewerModel
    @State private var startRatios: [Double]?
    @State private var hovering = false

    var body: some View {
        let d = divider
        let horizontal = d.axis == .horizontal   // chia trái/phải → đường dọc
        let thick: CGFloat = 14
        let frame = horizontal
            ? CGRect(x: d.position - thick / 2, y: d.parentRect.minY, width: thick, height: d.parentRect.height)
            : CGRect(x: d.parentRect.minX, y: d.position - thick / 2, width: d.parentRect.width, height: thick)
        let active = m.editing || hovering || startRatios != nil

        ZStack {
            Color.white.opacity(0.001)
            if active {
                Rectangle()
                    .fill(Color.accentColor)
                    .frame(width: horizontal ? 3 : nil, height: horizontal ? nil : 3)
                Capsule()
                    .fill(Color.accentColor)
                    .frame(width: horizontal ? 8 : 44, height: horizontal ? 44 : 8)
            }
        }
        .frame(width: frame.width, height: frame.height)
        .offset(x: frame.minX, y: frame.minY)
        .onHover { h in
            hovering = h
            if h { (horizontal ? NSCursor.resizeLeftRight : NSCursor.resizeUpDown).push() } else { NSCursor.pop() }
        }
        .gesture(
            DragGesture(minimumDistance: 1, coordinateSpace: .local)
                .onChanged { v in
                    guard case .split(_, let current, _) = m.tree.node(at: d.path) else { return }
                    if startRatios == nil { startRatios = current }
                    guard let r0 = startRatios, d.index + 1 < r0.count else { return }
                    let extent = horizontal ? d.parentRect.width : d.parentRect.height
                    guard extent > 0 else { return }
                    let delta = Double((horizontal ? v.translation.width : v.translation.height) / extent)
                    let pair = r0[d.index] + r0[d.index + 1]
                    let minR = 0.08
                    var a = r0[d.index] + delta
                    a = min(max(a, minR), pair - minR)
                    var r = r0
                    r[d.index] = a
                    r[d.index + 1] = pair - a
                    m.setRatios(at: d.path, r)
                }
                .onEnded { _ in startRatios = nil }
        )
    }
}

struct PageCell: View {
    let cell: Int
    let path: [Int]
    @EnvironmentObject var m: ViewerModel
    @State private var dropTargeted = false

    var body: some View {
        let idx = m.pageIndex(for: cell)
        let page = m.page(at: idx)
        ZStack(alignment: .bottomTrailing) {
            if m.editing {
                RoundedRectangle(cornerRadius: 4)
                    .fill(Color.white.opacity(0.06))
                    .overlay(RoundedRectangle(cornerRadius: 4)
                        .strokeBorder(style: StrokeStyle(lineWidth: 1.5, dash: [6, 4]))
                        .foregroundStyle(Color.white.opacity(0.5)))
            }
            PDFPageView(page: page)
                .opacity(m.editing ? 0.55 : 1)
            if m.showLabels, page != nil, !m.editing {
                Text("\(idx + 1)")
                    .font(.system(size: 13, weight: .semibold)).monospacedDigit()
                    .foregroundStyle(.white)
                    .padding(.horizontal, 7).padding(.vertical, 2)
                    .background(Color.black.opacity(0.55), in: Capsule())
                    .padding(6)
            }
            if m.editing { CellEditControls(cell: cell, path: path) }
        }
        .overlay(
            Rectangle().stroke(Color.accentColor, lineWidth: (m.selected == cell || dropTargeted) ? 4 : 0)
        )
        .contentShape(Rectangle())
        .gesture(
            TapGesture(count: 2).onEnded { if !m.editing { m.toggleZoom(cell) } }
                .exclusively(before: TapGesture().onEnded { if !m.editing { m.toggleSelect(cell) } })
        )
        .dropDestination(for: String.self) { items, _ in
            guard let s = items.first else { return false }
            if s.hasPrefix("page:"), let n = Int(s.dropFirst(5)) {
                m.assign(page: n, toCell: cell)
                return true
            }
            if let u = URL(string: s), u.isFileURL { m.open(u); return true }
            if s.hasPrefix("/") { m.open(URL(fileURLWithPath: s)); return true }
            return false
        } isTargeted: { dropTargeted = $0 }
        .contextMenu {
            Button(m.selected == cell ? L("Deselect cell", "Bỏ chọn ô") : L("Select to flip this cell only", "Chọn ô này để lật riêng")) { m.toggleSelect(cell) }
            Button(m.zoomed == nil ? L("Zoom this cell", "Phóng to ô này") : L("Zoom out", "Thu nhỏ lại")) { m.toggleZoom(cell) }
            Divider()
            Button(L("Split left - right", "Chia đôi trái - phải")) { m.split(at: path, axis: .horizontal) }
            Button(L("Split top - bottom", "Chia đôi trên - dưới")) { m.split(at: path, axis: .vertical) }
            Button(L("Remove this cell", "Xoá ô này")) { m.remove(at: path) }.disabled(m.cellCount <= 1)
            Divider()
            Button(L("Reset cells to consecutive pages", "Đưa mọi ô về trang liên tiếp")) { m.deltas = [:]; m.selected = nil }
        }
    }
}

struct CellEditControls: View {
    let cell: Int
    let path: [Int]
    @EnvironmentObject var m: ViewerModel

    var body: some View {
        VStack(spacing: 10) {
            Text(L("Cell", "Ô") + " \(cell + 1)")
                .font(.system(size: 22, weight: .semibold))
                .foregroundStyle(.white)
            HStack(spacing: 8) {
                Button { m.split(at: path, axis: .horizontal) } label: {
                    Image(systemName: "rectangle.split.2x1")
                }
                .help(L("Split left - right", "Chia đôi trái - phải"))
                Button { m.split(at: path, axis: .vertical) } label: {
                    Image(systemName: "rectangle.split.1x2")
                }
                .help(L("Split top - bottom", "Chia đôi trên - dưới"))
                Button(role: .destructive) { m.remove(at: path) } label: {
                    Image(systemName: "trash")
                }
                .help(L("Remove this cell", "Xoá ô này"))
                .disabled(m.cellCount <= 1)
            }
            .controlSize(.large)
        }
        .padding(12)
        .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 12))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

// MARK: - Cột trang thu nhỏ

struct ThumbnailList: View {
    @EnvironmentObject var m: ViewerModel

    var body: some View {
        let shown = m.shownPages
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(spacing: 12) {
                    ForEach(0..<m.pageCount, id: \.self) { i in
                        VStack(spacing: 4) {
                            if let img = m.thumbnail(i) {
                                Image(nsImage: img)
                                    .resizable()
                                    .aspectRatio(contentMode: .fit)
                                    .overlay(RoundedRectangle(cornerRadius: 3)
                                        .stroke(shown.contains(i) ? Color.accentColor : Color.secondary.opacity(0.3),
                                                lineWidth: shown.contains(i) ? 3 : 1))
                            }
                            Text("\(i + 1)").font(.caption).monospacedDigit()
                                .foregroundStyle(shown.contains(i) ? Color.accentColor : .secondary)
                        }
                        .id(i)
                        .contentShape(Rectangle())
                        .onTapGesture {
                            if let s = m.selected { m.assign(page: i, toCell: s) } else { m.goTo(pageNumber: i + 1) }
                        }
                        .draggable("page:\(i)") {
                            if let img = m.thumbnail(i) {
                                Image(nsImage: img).resizable().aspectRatio(contentMode: .fit).frame(width: 160)
                            }
                        }
                        .help(L("Click: jump the group here (or change only the selected cell) · Drag onto a cell: show this page there", "Bấm: đưa nhóm tới trang này (nếu đang chọn một ô thì chỉ ô đó đổi) · Kéo thả vào một ô: ô đó hiện trang này"))
                    }
                }
                .padding(12)
            }
            .onChange(of: m.start) { _, s in withAnimation { proxy.scrollTo(s, anchor: .center) } }
        }
        .background(.background)
    }
}

// MARK: - Chọn bố cục

struct LayoutIcon: View {
    let tree: LayoutNode
    var body: some View {
        Canvas { ctx, size in
            for leaf in tree.layout(in: CGRect(origin: .zero, size: size)).leaves {
                ctx.fill(Path(roundedRect: leaf.rect.insetBy(dx: 1, dy: 1), cornerRadius: 1.5),
                         with: .color(.primary.opacity(0.75)))
            }
        }
    }
}

struct LayoutTile: View {
    let preset: LayoutPreset
    let selected: Bool
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            VStack(spacing: 6) {
                LayoutIcon(tree: preset.tree).frame(width: 64, height: 36)
                Text(preset.title).font(.caption).multilineTextAlignment(.center)
                    .lineLimit(2).frame(height: 30)
            }
            .padding(8)
            .frame(maxWidth: .infinity)
            .background(RoundedRectangle(cornerRadius: 8)
                .fill(selected ? Color.accentColor.opacity(0.25) : Color.secondary.opacity(0.08)))
            .overlay(RoundedRectangle(cornerRadius: 8)
                .stroke(selected ? Color.accentColor : .clear, lineWidth: 2))
        }
        .buttonStyle(.plain)
    }
}

struct LayoutPicker: View {
    @EnvironmentObject var m: ViewerModel
    let close: () -> Void
    @State private var newName = ""
    private let cols = [GridItem(.adaptive(minimum: 104), spacing: 10)]

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(L("Built-in layouts", "Bố cục có sẵn")).font(.headline)
            LazyVGrid(columns: cols, spacing: 10) {
                ForEach(LayoutPreset.builtIn) { p in
                    LayoutTile(preset: p, selected: m.tree == p.tree) { m.apply(p) }
                }
            }
            HStack {
                Text(L("Custom grid:", "Lưới tự chọn:"))
                Stepper("\(m.gridRows) " + L("rows", "hàng"), value: $m.gridRows, in: 1...4)
                Stepper("\(m.gridCols) " + L("columns", "cột"), value: $m.gridCols, in: 1...5)
                Spacer()
                Button(L("Use this grid", "Dùng lưới này")) { m.applyGrid() }
            }

            if !m.savedLayouts.isEmpty {
                Divider()
                Text(L("Your layouts", "Bố cục của bạn")).font(.headline)
                LazyVGrid(columns: cols, spacing: 10) {
                    ForEach(m.savedLayouts) { p in
                        LayoutTile(preset: p, selected: m.tree == p.tree) { m.apply(p) }
                            .contextMenu { Button(L("Delete this layout", "Xoá bố cục này")) { m.deleteSaved(p) } }
                    }
                }
                Text(L("Right-click a layout to delete it.", "Chuột phải vào một bố cục để xoá.")).font(.caption).foregroundStyle(.secondary)
            }

            Divider()
            HStack {
                Button {
                    m.editing = true
                    close()
                } label: { Label(L("Edit current layout (E)", "Sửa bố cục hiện tại (E)"), systemImage: "square.split.bottomrightquarter") }
                Spacer()
            }
            HStack {
                TextField(L("Layout name, e.g. Quiz 3 slides", "Tên bố cục, ví dụ: Đề 3 slide"), text: $newName)
                    .onSubmit(save)
                Button(L("Save current layout", "Lưu bố cục hiện tại"), action: save)
                    .disabled(newName.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            HStack {
                Text(L("Gap between pages", "Khoảng cách giữa các trang"))
                Slider(value: $m.gap, in: 0...30)
                Text("\(Int(m.gap))").monospacedDigit().frame(width: 28)
            }
        }
        .padding(18)
        .frame(width: 560)
    }

    func save() {
        m.saveCurrentLayout(named: newName)
        newName = ""
    }
}
