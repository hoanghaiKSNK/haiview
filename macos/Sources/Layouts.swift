import Foundation
import CoreGraphics

/// horizontal: các con xếp trái → phải. vertical: trên → dưới.
enum SplitAxis: String, Codable { case horizontal, vertical }

/// Bố cục dạng cây chia vùng: mỗi vùng là một ô (leaf) hoặc được chia tiếp
/// thành nhiều vùng con theo tỉ lệ. Kiểu này cho phép chia, xoá, kéo rộng hẹp tuỳ ý.
indirect enum LayoutNode: Codable, Equatable {
    case leaf
    case split(SplitAxis, [Double], [LayoutNode])

    var leafCount: Int {
        switch self {
        case .leaf: return 1
        case .split(_, _, let ch): return ch.reduce(0) { $0 + $1.leafCount }
        }
    }

    static func row(_ n: Int) -> LayoutNode {
        n <= 1 ? .leaf : .split(.horizontal, Array(repeating: 1 / Double(n), count: n), Array(repeating: .leaf, count: n))
    }
    static func column(_ n: Int) -> LayoutNode {
        n <= 1 ? .leaf : .split(.vertical, Array(repeating: 1 / Double(n), count: n), Array(repeating: .leaf, count: n))
    }
    static func grid(rows: Int, cols: Int) -> LayoutNode {
        if rows <= 1 { return row(cols) }
        return .split(.vertical, Array(repeating: 1 / Double(rows), count: rows), Array(repeating: row(cols), count: rows))
    }

    // MARK: Hình học

    struct Leaf { let index: Int; let path: [Int]; let rect: CGRect }
    struct Divider: Identifiable {
        let path: [Int]       // đường dẫn tới vùng cha
        let index: Int        // ranh giới giữa con index và index+1
        let axis: SplitAxis
        let parentRect: CGRect
        let position: CGFloat // toạ độ x (horizontal) hoặc y (vertical) của ranh giới
        var id: String { path.map(String.init).joined(separator: ".") + "#\(index)" }
    }

    func layout(in rect: CGRect) -> (leaves: [Leaf], dividers: [Divider]) {
        var leaves: [Leaf] = [], dividers: [Divider] = []
        var counter = 0
        func walk(_ n: LayoutNode, _ r: CGRect, _ path: [Int]) {
            switch n {
            case .leaf:
                leaves.append(Leaf(index: counter, path: path, rect: r))
                counter += 1
            case .split(let axis, let ratios, let ch):
                var offset: CGFloat = 0
                for (i, c) in ch.enumerated() {
                    let f = CGFloat(ratios[i])
                    let sub: CGRect = axis == .horizontal
                        ? CGRect(x: r.minX + offset * r.width, y: r.minY, width: f * r.width, height: r.height)
                        : CGRect(x: r.minX, y: r.minY + offset * r.height, width: r.width, height: f * r.height)
                    walk(c, sub, path + [i])
                    offset += f
                    if i < ch.count - 1 {
                        let pos = axis == .horizontal ? r.minX + offset * r.width : r.minY + offset * r.height
                        dividers.append(Divider(path: path, index: i, axis: axis, parentRect: r, position: pos))
                    }
                }
            }
        }
        walk(self, rect, [])
        return (leaves, dividers)
    }

    // MARK: Sửa cây

    func node(at path: [Int]) -> LayoutNode {
        guard let first = path.first, case .split(_, _, let ch) = self, first < ch.count else { return self }
        return ch[first].node(at: Array(path.dropFirst()))
    }

    mutating func modify(_ path: [Int], _ f: (inout LayoutNode) -> Void) {
        guard let first = path.first else { f(&self); return }
        if case .split(let a, let r, var ch) = self, first < ch.count {
            ch[first].modify(Array(path.dropFirst()), f)
            self = .split(a, r, ch)
        }
    }

    mutating func setRatios(at path: [Int], _ ratios: [Double]) {
        modify(path) { n in
            if case .split(let a, _, let ch) = n { n = .split(a, ratios, ch) }
        }
    }

    /// Chia đôi một ô. Nếu vùng cha cùng hướng thì chèn thêm ô cạnh nó cho gọn cây.
    mutating func splitLeaf(at path: [Int], axis: SplitAxis) {
        if let last = path.last {
            let parentPath = Array(path.dropLast())
            if case .split(let a, var r, var ch) = node(at: parentPath), a == axis {
                let half = r[last] / 2
                r[last] = half
                r.insert(half, at: last + 1)
                ch.insert(.leaf, at: last + 1)
                modify(parentPath) { $0 = .split(a, r, ch) }
                return
            }
        }
        modify(path) { $0 = .split(axis, [0.5, 0.5], [.leaf, .leaf]) }
    }

    /// Xoá một ô; phần diện tích của nó chia cho các ô anh em.
    mutating func removeLeaf(at path: [Int]) {
        guard let last = path.last else { return }
        let parentPath = Array(path.dropLast())
        guard case .split(let a, var r, var ch) = node(at: parentPath) else { return }
        let freed = r.remove(at: last)
        ch.remove(at: last)
        let rest = r.reduce(0, +)
        r = r.map { $0 + freed * ($0 / max(rest, 0.0001)) }
        if ch.count == 1 {
            let only = ch[0]
            modify(parentPath) { $0 = only }
        } else {
            modify(parentPath) { $0 = .split(a, r, ch) }
        }
    }
}

struct LayoutPreset: Identifiable, Codable, Equatable {
    var id: String
    var title: String
    var tree: LayoutNode

    static let builtIn: [LayoutPreset] = [
        .init(id: "single", title: L("1 page", "1 trang"), tree: .leaf),
        .init(id: "row2", title: L("2 side by side", "2 trang cạnh nhau"), tree: .row(2)),
        .init(id: "col2", title: L("2 stacked", "2 trang trên dưới"), tree: .column(2)),
        .init(id: "row3", title: L("3 side by side", "3 trang cạnh nhau"), tree: .row(3)),
        .init(id: "2top1", title: L("2 top, 1 bottom", "2 trên, 1 dưới"), tree: .split(.vertical, [0.5, 0.5], [.row(2), .leaf])),
        .init(id: "1top2", title: L("1 top, 2 bottom", "1 trên, 2 dưới"), tree: .split(.vertical, [0.5, 0.5], [.leaf, .row(2)])),
        .init(id: "bigL", title: L("1 large left, 2 right", "1 lớn trái, 2 nhỏ phải"), tree: .split(.horizontal, [0.62, 0.38], [.leaf, .column(2)])),
        .init(id: "bigR", title: L("2 left, 1 large right", "2 nhỏ trái, 1 lớn phải"), tree: .split(.horizontal, [0.38, 0.62], [.column(2), .leaf])),
        .init(id: "grid22", title: L("Grid 2 × 2", "Lưới 2 × 2"), tree: .grid(rows: 2, cols: 2)),
        .init(id: "grid23", title: L("Grid 2 × 3", "Lưới 2 × 3"), tree: .grid(rows: 2, cols: 3)),
    ]
}
