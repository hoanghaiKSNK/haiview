import Foundation
import CryptoKit

/// Chuyển PPT/PPTX sang PDF bằng chính Microsoft PowerPoint trên máy (qua AppleScript),
/// nên bố cục, phông chữ giống hệt khi mở bằng PowerPoint. Slide ẩn không được xuất.
enum PowerPointConverter {
    static let extensions: Set<String> = ["ppt", "pptx", "pps", "ppsx", "pptm", "potx"]

    struct Failure: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    /// PowerPoint chạy trong sandbox, chỉ ghi được vào thư mục container của nó.
    static var cacheDir: URL {
        let home = FileManager.default.homeDirectoryForCurrentUser
        return home.appendingPathComponent(
            "Library/Containers/com.microsoft.Powerpoint/Data/Library/Caches/HaiView",
            isDirectory: true)
    }

    private static let script = """
    on run argv
      set srcPath to item 1 of argv
      set outPath to item 2 of argv
      set wasRunning to application "Microsoft PowerPoint" is running
      tell application "Microsoft PowerPoint"
        set thePres to missing value
        set alreadyOpen to false
        repeat with p in presentations
          if (full name of p) is srcPath then
            set thePres to contents of p
            set alreadyOpen to true
            exit repeat
          end if
        end repeat
        if thePres is missing value then
          open (POSIX file srcPath)
          set thePres to active presentation
        end if
        save thePres in (POSIX file outPath) as save as PDF
        if not alreadyOpen then close thePres saving no
      end tell
      if not wasRunning then tell application "Microsoft PowerPoint" to quit
      return "ok"
    end run
    """

    static func cachedPDFURL(for src: URL) -> URL {
        let attrs = try? FileManager.default.attributesOfItem(atPath: src.path)
        let mod = (attrs?[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0
        let size = (attrs?[.size] as? NSNumber)?.intValue ?? 0
        let key = "\(src.path)|\(mod)|\(size)"
        let hash = SHA256.hash(data: Data(key.utf8)).prefix(6).map { String(format: "%02x", $0) }.joined()
        let base = src.deletingPathExtension().lastPathComponent
        return cacheDir.appendingPathComponent("\(base)-\(hash).pdf")
    }

    /// Trả về PDF đã chuyển. Dùng lại bản cũ nếu file PowerPoint chưa đổi, trừ khi `force`.
    static func convert(_ src: URL, force: Bool = false) async throws -> URL {
        let out = cachedPDFURL(for: src)
        if !force, FileManager.default.fileExists(atPath: out.path) { return out }
        guard FileManager.default.fileExists(atPath: "/Applications/Microsoft PowerPoint.app") else {
            throw Failure(message: L("Microsoft PowerPoint was not found in Applications. Export your slides to PDF and open the PDF instead.", "Không thấy Microsoft PowerPoint trong thư mục Applications. Hãy xuất slide ra PDF rồi mở file PDF."))
        }
        try FileManager.default.createDirectory(at: cacheDir, withIntermediateDirectories: true)
        try? FileManager.default.removeItem(at: out)

        return try await withCheckedThrowingContinuation { cont in
            DispatchQueue.global(qos: .userInitiated).async {
                let p = Process()
                p.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
                p.arguments = ["-e", script, src.path, out.path]
                let err = Pipe()
                p.standardError = err
                p.standardOutput = Pipe()
                do {
                    try p.run()
                    p.waitUntilExit()
                } catch {
                    cont.resume(throwing: error)
                    return
                }
                let errText = String(data: err.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
                if p.terminationStatus == 0, FileManager.default.fileExists(atPath: out.path) {
                    cont.resume(returning: out)
                } else if errText.contains("-1743") || errText.lowercased().contains("not authorized") {
                    cont.resume(throwing: Failure(message:
                        L("macOS hasn't allowed HaiView to control PowerPoint yet. Open System Settings → Privacy & Security → Automation, turn on PowerPoint for HaiView, then open the file again.", "macOS chưa cho phép HaiView điều khiển PowerPoint. Vào Cài đặt hệ thống → Quyền riêng tư & Bảo mật → Tự động hoá, bật PowerPoint cho HaiView, rồi mở lại file.")))
                } else {
                    cont.resume(throwing: Failure(message:
                        L("PowerPoint couldn't export a PDF.", "PowerPoint không xuất được PDF.") + "\n\(errText.trimmingCharacters(in: .whitespacesAndNewlines))"))
                }
            }
        }
    }
}
