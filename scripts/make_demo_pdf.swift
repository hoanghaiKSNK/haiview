// Generates docs/demo/HaiView-demo.pdf: 3 teaching cases × 3 slides (scenario, question, answer).
import AppKit
let out = CommandLine.arguments[1]
let W: CGFloat = 1280, H: CGFloat = 720
var box = CGRect(x: 0, y: 0, width: W, height: H)
let ctx = CGContext(URL(fileURLWithPath: out) as CFURL, mediaBox: &box, nil)!
let palette: [NSColor] = [NSColor(red: 0.09, green: 0.36, blue: 0.65, alpha: 1),
                          NSColor(red: 0.06, green: 0.43, blue: 0.34, alpha: 1),
                          NSColor(red: 0.60, green: 0.24, blue: 0.11, alpha: 1)]
let cases: [(String, [String], String, [String], [String])] = [
  ("Case 1 · A fever after surgery",
   ["Day 4 after a hip replacement", "New fever 38.6 °C, wound is red and warm", "Drain output looks cloudy"],
   "What would you check first?",
   ["A. Repeat the temperature tomorrow", "B. Look at the wound and take a sample", "C. Change the dressing only"],
   ["Answer: B", "Examine the wound and send a sample before changing antibiotics.", "Record the date: it decides whether this counts as a surgical site infection."]),
  ("Case 2 · A cluster on one ward",
   ["Three patients with the same organism in 10 days", "All in rooms 4 to 7", "Shared equipment: one bladder scanner"],
   "Is this an outbreak signal?",
   ["A. No, three cases are too few", "B. Yes, investigate now", "C. Wait for a fourth case"],
   ["Answer: B", "Same organism, same place, same time: start a line list today.", "Check the shared equipment and its cleaning log."]),
  ("Case 3 · Hand hygiene audit",
   ["Ward audit: 120 opportunities observed", "78 performed correctly", "Most misses: before touching the patient"],
   "What is the compliance rate?",
   ["A. 54%", "B. 65%", "C. 78%"],
   ["Answer: B · 78 / 120 = 65%", "Feed back the result to the team within a week.", "Focus training on moment 1: before patient contact."]),
]
func text(_ s: String, _ size: CGFloat, _ color: NSColor, _ x: CGFloat, _ y: CGFloat, bold: Bool = false, width: CGFloat = 1100) {
  let font = NSFont.systemFont(ofSize: size, weight: bold ? .bold : .regular)
  NSAttributedString(string: s, attributes: [.font: font, .foregroundColor: color])
    .draw(with: CGRect(x: x, y: y, width: width, height: size * 1.5), options: [.usesLineFragmentOrigin])
}
for (ci, c) in cases.enumerated() {
  let color = palette[ci]
  let slides: [(String, String, [String])] = [("Scenario", c.0, c.1), ("Question", c.2, c.3), ("Answer", "Discussion", c.4)]
  for (si, s) in slides.enumerated() {
    ctx.beginPDFPage(nil)
    NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx, flipped: false)
    NSColor.white.setFill(); box.fill()
    color.setFill(); CGRect(x: 0, y: H - 120, width: W, height: 120).fill()
    text("\(s.0.uppercased())  ·  \(si + 1)/3", 22, NSColor.white.withAlphaComponent(0.85), 60, H - 62, bold: true)
    text(s.1, 40, .white, 60, H - 112, bold: true)
    for (i, line) in s.2.enumerated() {
      let y = H - 230 - CGFloat(i) * 88
      color.withAlphaComponent(0.12).setFill()
      NSBezierPath(roundedRect: CGRect(x: 60, y: y - 8, width: W - 120, height: 66), xRadius: 10, yRadius: 10).fill()
      text(line, 32, NSColor(white: 0.12, alpha: 1), 90, y, bold: si == 2 && i == 0)
    }
    NSColor(white: 0.55, alpha: 1).setFill()
    text("HaiView demo deck  ·  page \(ci * 3 + si + 1) of 9", 16, NSColor(white: 0.5, alpha: 1), 60, 26)
    NSGraphicsContext.current = nil
    ctx.endPDFPage()
  }
}
ctx.closePDF()
